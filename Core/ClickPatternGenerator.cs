using System;
using NAudio.Wave;

namespace SyncWave.Core
{
    /// <summary>
    /// Generates an aperiodic click pattern as an ISampleProvider for perceptual
    /// delay calibration. The pattern uses irregular inter-click intervals so that
    /// there is exactly ONE delay offset at which two devices' patterns align —
    /// eliminating the periodicity/aliasing ambiguity that would exist with a
    /// regular repeating click.
    ///
    /// The click itself is a short filtered-noise transient (~15ms) with
    /// high-frequency content for sharp perceptual onset.
    /// </summary>
    public class ClickPatternGenerator : ISampleProvider
    {
        // ── Aperiodic interval sequence (ms) ──────────────────────
        // Total loop ≈ 1310ms — well above the 500ms max slider range,
        // ensuring no false-lock at offset + loop_period.
        private static readonly int[] IntervalMs = { 180, 340, 110, 420, 260 };

        // ── Click parameters ──────────────────────────────────────
        private const int ClickDurationMs = 15;
        private const float ClickFrequencyHz = 3200f;  // Sharp, easily localizable
        private const float ClickAmplitude = 0.65f;     // Comfortable but clearly audible

        private readonly WaveFormat _waveFormat;
        private readonly float[] _patternBuffer;    // Pre-rendered full loop
        private readonly int _patternLengthSamples;
        private int _position;
        private volatile bool _isPlaying;

        public WaveFormat WaveFormat => _waveFormat;

        /// <summary>Whether the generator is actively producing clicks.</summary>
        public bool IsPlaying
        {
            get => _isPlaying;
            set => _isPlaying = value;
        }

        /// <summary>
        /// Creates a click pattern generator at the specified sample rate.
        /// </summary>
        /// <param name="sampleRate">Sample rate (typically 48000 from WASAPI).</param>
        /// <param name="channels">Channel count (typically 2 for stereo).</param>
        public ClickPatternGenerator(int sampleRate = 48000, int channels = 2)
        {
            _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
            _patternBuffer = BuildPattern(sampleRate, channels);
            _patternLengthSamples = _patternBuffer.Length / channels;
            _position = 0;
            _isPlaying = false;
        }

        /// <summary>
        /// Pre-renders the entire aperiodic click loop into a float buffer.
        /// Each "click" is a windowed sine burst at ClickFrequencyHz.
        /// </summary>
        private static float[] BuildPattern(int sampleRate, int channels)
        {
            // Calculate total loop duration from intervals
            int totalMs = 0;
            foreach (var ms in IntervalMs)
                totalMs += ms;

            int totalSamples = (int)(sampleRate * totalMs / 1000.0);
            var buffer = new float[totalSamples * channels];

            int clickSamples = (int)(sampleRate * ClickDurationMs / 1000.0);
            int offsetSamples = 0;

            foreach (var intervalMs in IntervalMs)
            {
                // Render a click at the current offset
                for (int i = 0; i < clickSamples && (offsetSamples + i) < totalSamples; i++)
                {
                    // Hann window for smooth envelope (no click/pop at edges)
                    double window = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / clickSamples));

                    // Sine burst
                    double t = (double)i / sampleRate;
                    float sample = (float)(ClickAmplitude * window * Math.Sin(2.0 * Math.PI * ClickFrequencyHz * t));

                    // Write to all channels
                    int bufPos = (offsetSamples + i) * channels;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        if (bufPos + ch < buffer.Length)
                            buffer[bufPos + ch] = sample;
                    }
                }

                // Advance by the interval duration
                offsetSamples += (int)(sampleRate * intervalMs / 1000.0);
            }

            return buffer;
        }

        /// <summary>
        /// Reads samples from the looping pattern. Returns silence when not playing.
        /// </summary>
        public int Read(float[] buffer, int offset, int count)
        {
            if (!_isPlaying)
            {
                // Fill with silence
                Array.Clear(buffer, offset, count);
                return count;
            }

            int channels = _waveFormat.Channels;
            int samplesWritten = 0;

            while (samplesWritten < count)
            {
                int bufferIndex = _position * channels;
                int remaining = count - samplesWritten;
                int availableInPattern = (_patternLengthSamples - _position) * channels;
                int toCopy = Math.Min(remaining, availableInPattern);

                Array.Copy(_patternBuffer, bufferIndex, buffer, offset + samplesWritten, toCopy);
                samplesWritten += toCopy;
                _position += toCopy / channels;

                // Loop back to start
                if (_position >= _patternLengthSamples)
                    _position = 0;
            }

            return count;
        }

        /// <summary>Resets playback position to the start of the pattern.</summary>
        public void Reset()
        {
            _position = 0;
        }

        /// <summary>Total duration of one full loop in milliseconds.</summary>
        public int LoopDurationMs
        {
            get
            {
                int total = 0;
                foreach (var ms in IntervalMs) total += ms;
                return total;
            }
        }
    }
}
