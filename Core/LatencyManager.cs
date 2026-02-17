using System;
using System.Collections.Concurrent;
using System.Linq;
using NAudio.Wave;
using SyncWave.Utils;

namespace SyncWave.Core
{
    /// <summary>
    /// Manages per-device latency measurement and compensation.
    /// 
    /// Algorithm:
    /// 1. On sync start, each device's stream latency is measured/estimated.
    /// 2. The maximum latency across all devices is determined.
    /// 3. For each device: compensationDelay = maxLatency - deviceLatency + manualOffset.
    /// 4. A delay buffer per device inserts the required delay.
    /// 5. Users can adjust the manual slider in real-time; the delay adjusts dynamically.
    /// </summary>
    public class LatencyManager
    {
        /// <summary>
        /// High-performance delay buffer using a circular buffer with BlockCopy.
        /// Avoids per-byte operations and minimises allocations.
        /// </summary>
        private class DelayBuffer
        {
            private byte[] _ringBuffer;
            private int _writePos;
            private int _readPos;
            private int _bufferedCount;
            private int _delayBytes;
            private readonly object _lock = new();

            // Re-usable output buffer to avoid per-call allocation
            private byte[] _outputBuffer = Array.Empty<byte>();

            public double MeasuredLatencyMs { get; set; }
            public double ManualDelayMs { get; set; }

            public DelayBuffer(int maxBufferSize)
            {
                _ringBuffer = new byte[maxBufferSize];
            }

            /// <summary>
            /// Sets the delay in bytes. Resets and pre-fills with silence.
            /// </summary>
            public void SetDelay(int delayBytes)
            {
                lock (_lock)
                {
                    if (delayBytes == _delayBytes) return;

                    _delayBytes = Math.Clamp(delayBytes, 0, _ringBuffer.Length / 2);

                    // Reset buffer and pre-fill with silence for the delay amount
                    Array.Clear(_ringBuffer);
                    _writePos = _delayBytes;
                    _readPos = 0;
                    _bufferedCount = _delayBytes;
                }
            }

            /// <summary>
            /// Writes incoming audio data and returns delayed audio data.
            /// Uses BlockCopy for efficient bulk transfers and reuses output buffer.
            /// </summary>
            public byte[] Process(byte[] input, int count)
            {
                lock (_lock)
                {
                    if (_delayBytes <= 0)
                    {
                        // Zero delay — clone the data (must not return same reference
                        // because caller may apply volume scaling in-place)
                        EnsureOutputSize(count);
                        Buffer.BlockCopy(input, 0, _outputBuffer, 0, count);
                        return _outputBuffer;
                    }

                    // ── Write input into ring buffer ──
                    WriteToRing(input, count);
                    _bufferedCount += count;

                    // ── Read delayed output ──
                    int readCount = Math.Min(count, _bufferedCount);
                    EnsureOutputSize(readCount);
                    ReadFromRing(readCount);
                    _bufferedCount -= readCount;

                    return _outputBuffer;
                }
            }

            /// <summary>
            /// Returns the current output buffer length (matches last Process call).
            /// </summary>
            public int LastOutputLength { get; private set; }

            private void EnsureOutputSize(int needed)
            {
                if (_outputBuffer.Length < needed)
                    _outputBuffer = new byte[needed];
                LastOutputLength = needed;
            }

            private void WriteToRing(byte[] src, int count)
            {
                int pos = _writePos;
                int remaining = count;

                while (remaining > 0)
                {
                    int chunk = Math.Min(remaining, _ringBuffer.Length - pos);
                    Buffer.BlockCopy(src, count - remaining, _ringBuffer, pos, chunk);
                    pos = (pos + chunk) % _ringBuffer.Length;
                    remaining -= chunk;
                }
                _writePos = pos;
            }

            private void ReadFromRing(int count)
            {
                int pos = _readPos;
                int remaining = count;

                while (remaining > 0)
                {
                    int chunk = Math.Min(remaining, _ringBuffer.Length - pos);
                    Buffer.BlockCopy(_ringBuffer, pos, _outputBuffer, count - remaining, chunk);
                    pos = (pos + chunk) % _ringBuffer.Length;
                    remaining -= chunk;
                }
                _readPos = pos;
            }
        }

        private readonly ConcurrentDictionary<string, DelayBuffer> _buffers = new();
        private readonly ConcurrentDictionary<string, double> _manualDelays = new();
        private WaveFormat? _format;

        // Max buffer: ~600ms worth of audio (updated when format is known)
        private int _maxBufferBytes = 48000 * 4 * 2;

        /// <summary>
        /// Sets the audio format used for latency byte calculations.
        /// Must be called before setting device latencies.
        /// </summary>
        public void SetFormat(WaveFormat format)
        {
            _format = format;
            _maxBufferBytes = (int)(format.AverageBytesPerSecond * 0.6);
            Logger.Info($"LatencyManager format set: {format.SampleRate} Hz, {format.BitsPerSample} bit, " +
                        $"{format.Channels} ch. Max delay buffer: {_maxBufferBytes} bytes");
        }

        /// <summary>
        /// Registers a device's measured/estimated latency.
        /// </summary>
        public void SetDeviceLatency(string deviceId, double latencyMs)
        {
            var buffer = _buffers.GetOrAdd(deviceId, _ => new DelayBuffer(_maxBufferBytes));
            buffer.MeasuredLatencyMs = latencyMs;
            RecalculateDelays();
            Logger.Info($"Device {deviceId} latency set to {latencyMs:F1} ms");
        }

        /// <summary>
        /// Updates the manual delay offset for a specific device.
        /// </summary>
        public void SetManualDelay(string deviceId, double delayMs)
        {
            _manualDelays[deviceId] = delayMs;
            if (_buffers.TryGetValue(deviceId, out var buffer))
            {
                buffer.ManualDelayMs = delayMs;
            }
            RecalculateDelays();
        }

        /// <summary>
        /// Removes a device from latency management.
        /// </summary>
        public void RemoveDevice(string deviceId)
        {
            _buffers.TryRemove(deviceId, out _);
            _manualDelays.TryRemove(deviceId, out _);
            RecalculateDelays();
        }

        /// <summary>
        /// Clears all device latency data.
        /// </summary>
        public void ClearAll()
        {
            _buffers.Clear();
            _manualDelays.Clear();
        }

        /// <summary>
        /// Applies the calculated delay to audio data for the specified device.
        /// Returns the delayed audio buffer and its valid length via out parameter.
        /// </summary>
        public byte[] ApplyDelay(string deviceId, byte[] data, int byteCount, WaveFormat format, out int outputLength)
        {
            if (!_buffers.TryGetValue(deviceId, out var buffer))
            {
                // No delay buffer — pass through
                outputLength = byteCount;
                var result = new byte[byteCount];
                Buffer.BlockCopy(data, 0, result, 0, byteCount);
                return result;
            }

            var output = buffer.Process(data, byteCount);
            outputLength = buffer.LastOutputLength;
            return output;
        }

        /// <summary>
        /// Gets the computed latency difference for a device relative to the slowest.
        /// </summary>
        public double GetLatencyDiff(string deviceId)
        {
            if (!_buffers.Any() || !_buffers.TryGetValue(deviceId, out var buffer))
                return 0;

            var maxLatency = _buffers.Values.Max(b => b.MeasuredLatencyMs);
            return maxLatency - buffer.MeasuredLatencyMs;
        }

        /// <summary>
        /// Recalculates all delay buffers based on current latencies.
        /// Delays faster devices to match the slowest device.
        /// </summary>
        private void RecalculateDelays()
        {
            if (_buffers.IsEmpty) return;

            int sampleRate = _format?.SampleRate ?? 48000;
            int channels = _format?.Channels ?? 2;
            int bytesPerSample = (_format?.BitsPerSample ?? 32) / 8;
            int blockAlign = channels * bytesPerSample;

            var maxLatency = _buffers.Values.Max(b => b.MeasuredLatencyMs);

            foreach (var kvp in _buffers)
            {
                var buffer = kvp.Value;
                var manualDelay = _manualDelays.GetValueOrDefault(kvp.Key, 0);

                var totalDelayMs = (maxLatency - buffer.MeasuredLatencyMs) + manualDelay;
                totalDelayMs = Math.Max(0, totalDelayMs);

                int delayBytes = (int)(sampleRate * blockAlign * totalDelayMs / 1000.0);
                delayBytes = (delayBytes / blockAlign) * blockAlign; // Align to sample boundary

                buffer.SetDelay(delayBytes);
            }
        }
    }
}
