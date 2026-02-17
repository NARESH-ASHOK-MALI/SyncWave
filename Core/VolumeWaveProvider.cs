using System;
using NAudio.Wave;

namespace SyncWave.Core
{
    /// <summary>
    /// Wraps a WaveProvider and applies volume scaling at read-time.
    /// Volume changes take effect INSTANTLY because scaling happens
    /// when WasapiOut pulls data, not when data is written to the buffer.
    /// </summary>
    public class VolumeWaveProvider : IWaveProvider
    {
        private readonly IWaveProvider _source;
        private volatile float _volume = 1.0f;

        public WaveFormat WaveFormat => _source.WaveFormat;

        /// <summary>
        /// Volume level from 0.0 (mute) to 2.0 (200% boost). Thread-safe.
        /// Changes take effect on the very next audio read.
        /// </summary>
        public float Volume
        {
            get => _volume;
            set => _volume = Math.Clamp(value, 0f, 2.0f);
        }

        public VolumeWaveProvider(IWaveProvider source)
        {
            _source = source;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = _source.Read(buffer, offset, count);

            float vol = _volume;
            if ((vol >= 0.999f && vol <= 1.001f) || bytesRead == 0)
                return bytesRead;

            // Scale IEEE float32 samples in-place with soft clipping
            int sampleCount = bytesRead / 4;
            for (int i = 0; i < sampleCount; i++)
            {
                int pos = offset + i * 4;
                float sample = BitConverter.ToSingle(buffer, pos);
                sample *= vol;
                // Soft clip to prevent harsh distortion when boosting
                if (sample > 1.0f) sample = 1.0f;
                else if (sample < -1.0f) sample = -1.0f;
                BitConverter.TryWriteBytes(buffer.AsSpan(pos, 4), sample);
            }

            return bytesRead;
        }
    }
}
