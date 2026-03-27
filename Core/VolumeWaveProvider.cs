using System;
using NAudio.Wave;

namespace SyncWave.Core
{
    /// <summary>
    /// Wraps a WaveProvider and applies volume scaling at read-time.
    /// Volume changes take effect INSTANTLY because scaling happens
    /// when WasapiOut pulls data, not when data is written to the buffer.
    /// 
    /// Uses perceptual (squared) volume curve and tanh soft-clipping
    /// for natural loudness control and distortion-free boosting.
    /// </summary>
    public class VolumeWaveProvider : IWaveProvider
    {
        private readonly IWaveProvider _source;
        private volatile float _volume = 1.0f;

        public WaveFormat WaveFormat => _source.WaveFormat;

        /// <summary>
        /// Volume level from 0.0 (mute) to 2.0 (200% boost). Thread-safe.
        /// A perceptual curve is applied internally so the slider feels linear to humans.
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

            // Apply perceptual (squared) volume curve:
            // slider at 50% → effective gain ~25% → sounds like half volume
            // This makes different devices much easier to volume-match
            float effectiveVol = vol * vol;

            // Scale IEEE float32 samples in-place with tanh soft clipping
            int sampleCount = bytesRead / 4;
            for (int i = 0; i < sampleCount; i++)
            {
                int pos = offset + i * 4;
                float sample = BitConverter.ToSingle(buffer, pos);
                sample *= effectiveVol;

                // Tanh soft clipping — smoothly saturates instead of hard chopping.
                // Only apply when sample exceeds ±0.95 to avoid unnecessary math
                // on normal-level signals (tanh is identity-like near zero).
                if (sample > 0.95f || sample < -0.95f)
                {
                    sample = MathF.Tanh(sample);
                }

                BitConverter.TryWriteBytes(buffer.AsSpan(pos, 4), sample);
            }

            return bytesRead;
        }
    }
}
