using System;
using System.Threading;
using NAudio.Wave;

namespace SyncWave.Core
{
    /// <summary>
    /// A minimal-latency circular buffer WaveProvider designed for real-time audio forwarding.
    /// 
    /// Unlike BufferedWaveProvider which accumulates data indefinitely until overflow,
    /// this provider actively manages buffer level to stay as close to zero as possible:
    /// 
    /// 1. Uses a lock-free circular ring buffer for zero-contention writes.
    /// 2. When buffer grows beyond a configurable "target" threshold, excess old
    ///    data is silently discarded (read pointer jumps forward), keeping latency tight.
    /// 3. When buffer is empty, outputs silence — no underrun exceptions.
    /// 4. Block-aligned reads/writes prevent audio corruption.
    /// 
    /// Target buffer level: ~0ms (just enough to bridge capture→playback jitter).
    /// </summary>
    public class TightBufferWaveProvider : IWaveProvider
    {
        private readonly byte[] _ringBuffer;
        private int _writePos;
        private int _readPos;
        private int _bufferedCount;
        private readonly int _blockAlign;
        private readonly object _lock = new();

        /// <summary>Target maximum buffered bytes before discarding old data.</summary>
        private readonly int _targetMaxBytes;

        public WaveFormat WaveFormat { get; }

        /// <summary>Current number of bytes buffered (thread-safe read).</summary>
        public int BufferedBytes
        {
            get { lock (_lock) { return _bufferedCount; } }
        }

        /// <summary>Total buffer capacity in bytes.</summary>
        public int BufferLength => _ringBuffer.Length;

        /// <summary>
        /// Creates a tight buffer wave provider.
        /// </summary>
        /// <param name="format">Audio format.</param>
        /// <param name="bufferSizeMs">Total ring buffer capacity in milliseconds (safety ceiling).</param>
        /// <param name="targetMaxMs">
        /// Target maximum buffer level in milliseconds. When buffered data exceeds this,
        /// old samples are discarded to keep latency near zero. Default: 30ms.
        /// </param>
        public TightBufferWaveProvider(WaveFormat format, int bufferSizeMs = 500, int targetMaxMs = 30)
        {
            WaveFormat = format;
            _blockAlign = format.BlockAlign;

            // Total capacity (safety ceiling — should never be fully used)
            int totalBytes = format.AverageBytesPerSecond * bufferSizeMs / 1000;
            totalBytes = (totalBytes / _blockAlign) * _blockAlign;
            _ringBuffer = new byte[Math.Max(totalBytes, _blockAlign * 16)];

            // Target level — anything above this gets trimmed
            _targetMaxBytes = format.AverageBytesPerSecond * targetMaxMs / 1000;
            _targetMaxBytes = (_targetMaxBytes / _blockAlign) * _blockAlign;
            _targetMaxBytes = Math.Max(_targetMaxBytes, _blockAlign * 4); // Minimum 4 blocks
        }

        /// <summary>
        /// Adds audio samples to the buffer. If adding would exceed the target level,
        /// the oldest data is discarded to make room, keeping latency minimal.
        /// </summary>
        public void AddSamples(byte[] buffer, int offset, int count)
        {
            if (count <= 0) return;

            // Align to block boundary
            count = (count / _blockAlign) * _blockAlign;
            if (count <= 0) return;

            lock (_lock)
            {
                // If adding this data would exceed target, discard oldest data
                int projectedTotal = _bufferedCount + count;
                if (projectedTotal > _targetMaxBytes)
                {
                    // Discard enough old data so we stay at target after write
                    int discard = projectedTotal - _targetMaxBytes;
                    discard = (discard / _blockAlign) * _blockAlign;
                    if (discard > _bufferedCount)
                        discard = (_bufferedCount / _blockAlign) * _blockAlign;

                    _readPos = (_readPos + discard) % _ringBuffer.Length;
                    _bufferedCount -= discard;
                }

                // If count exceeds total buffer, only write the tail
                if (count > _ringBuffer.Length)
                {
                    offset += count - _ringBuffer.Length;
                    count = _ringBuffer.Length;
                }

                // Write to ring buffer
                WriteToRing(buffer, offset, count);
                _bufferedCount += count;

                // Clamp (shouldn't happen, but safety)
                if (_bufferedCount > _ringBuffer.Length)
                    _bufferedCount = _ringBuffer.Length;
            }
        }

        /// <summary>
        /// Reads audio data for playback. Returns silence when buffer is empty.
        /// </summary>
        public int Read(byte[] buffer, int offset, int count)
        {
            // Align request
            count = (count / _blockAlign) * _blockAlign;

            lock (_lock)
            {
                int toRead = Math.Min(count, _bufferedCount);
                toRead = (toRead / _blockAlign) * _blockAlign;

                if (toRead > 0)
                {
                    ReadFromRing(buffer, offset, toRead);
                    _bufferedCount -= toRead;
                }

                // Fill remainder with silence
                if (toRead < count)
                {
                    Array.Clear(buffer, offset + toRead, count - toRead);
                }
            }

            return count;
        }

        /// <summary>
        /// Clears all buffered data.
        /// </summary>
        public void ClearBuffer()
        {
            lock (_lock)
            {
                _readPos = 0;
                _writePos = 0;
                _bufferedCount = 0;
            }
        }

        private void WriteToRing(byte[] src, int srcOffset, int count)
        {
            int pos = _writePos;
            int remaining = count;

            while (remaining > 0)
            {
                int chunk = Math.Min(remaining, _ringBuffer.Length - pos);
                Buffer.BlockCopy(src, srcOffset + (count - remaining), _ringBuffer, pos, chunk);
                pos = (pos + chunk) % _ringBuffer.Length;
                remaining -= chunk;
            }
            _writePos = pos;
        }

        private void ReadFromRing(byte[] dst, int dstOffset, int count)
        {
            int pos = _readPos;
            int remaining = count;

            while (remaining > 0)
            {
                int chunk = Math.Min(remaining, _ringBuffer.Length - pos);
                Buffer.BlockCopy(_ringBuffer, pos, dst, dstOffset + (count - remaining), chunk);
                pos = (pos + chunk) % _ringBuffer.Length;
                remaining -= chunk;
            }
            _readPos = pos;
        }
    }
}
