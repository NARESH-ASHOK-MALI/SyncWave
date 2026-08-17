using System;
using NAudio.Wave;

namespace SyncWave.Core
{
    /// <summary>
    /// Generates a synthesized melodic phrase as an ISampleProvider for perceptual
    /// delay calibration. The pattern uses distinct pitches (an arpeggio) so that
    /// each note is individually identifiable by ear.
    ///
    /// The melody provides exactly ONE delay offset at which two devices' patterns align,
    /// eliminating the periodicity/aliasing ambiguity that would exist with a regular repeating click.
    ///
    /// The total loop is ~3000ms — well above the 500ms max slider range.
    /// </summary>
    public class SyncMelodyGenerator : ISampleProvider
    {
        // ── Melody sequence ───────────────────────────────────────
        // Pitches: C5, E5, G5, C6
        private static readonly float[] FrequenciesHz = { 523.25f, 659.25f, 783.99f, 1046.50f };
        // Intervals until NEXT note starts (ms)
        private static readonly int[] IntervalMs = { 600, 600, 600, 1200 };

        // ── Note parameters ───────────────────────────────────────
        private const int NoteDurationMs = 200;
        private const float BaseAmplitude = 0.65f;

        private readonly WaveFormat _waveFormat;
        private readonly float[] _patternBuffer;    // Pre-rendered full loop
        private readonly int _patternLengthSamples;
        private int _position;
        private volatile bool _isPlaying;

        public WaveFormat WaveFormat => _waveFormat;

        /// <summary>Whether the generator is actively producing the melody.</summary>
        public bool IsPlaying
        {
            get => _isPlaying;
            set => _isPlaying = value;
        }

        /// <summary>
        /// Creates a melody generator at the specified sample rate.
        /// </summary>
        /// <param name="sampleRate">Sample rate (typically 48000 from WASAPI).</param>
        /// <param name="channels">Channel count (typically 2 for stereo).</param>
        public SyncMelodyGenerator(int sampleRate = 48000, int channels = 2)
        {
            _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
            _patternBuffer = BuildPattern(sampleRate, channels);
            _patternLengthSamples = _patternBuffer.Length / channels;
            _position = 0;
            _isPlaying = false;
        }

        /// <summary>
        /// Pre-renders the entire melody loop into a float buffer.
        /// Each note is an enveloped sine wave.
        /// </summary>
        private static float[] BuildPattern(int sampleRate, int channels)
        {
            int totalMs = 0;
            foreach (var ms in IntervalMs)
                totalMs += ms;

            int totalSamples = (int)(sampleRate * totalMs / 1000.0);
            var buffer = new float[totalSamples * channels];

            int noteSamples = (int)(sampleRate * NoteDurationMs / 1000.0);
            int offsetSamples = 0;

            for (int noteIdx = 0; noteIdx < FrequenciesHz.Length; noteIdx++)
            {
                float freq = FrequenciesHz[noteIdx];
                int interval = IntervalMs[noteIdx];

                for (int i = 0; i < noteSamples && (offsetSamples + i) < totalSamples; i++)
                {
                    // Simple envelope: sharp attack, smooth decay
                    // We'll use a modified Hann window or ADSR.
                    // Attack: 20ms, Decay/Sustain: remainder.
                    int attackSamples = (int)(sampleRate * 20 / 1000.0);
                    double env = 1.0;

                    if (i < attackSamples)
                    {
                        // Sine-based attack
                        env = Math.Sin(Math.PI / 2.0 * i / attackSamples);
                    }
                    else
                    {
                        // Exponential decay
                        double decayProgress = (double)(i - attackSamples) / (noteSamples - attackSamples);
                        env = Math.Pow(1.0 - decayProgress, 2.0); // smooth drop to 0
                    }

                    // Base sine wave
                    double t = (double)i / sampleRate;
                    float sample = (float)(BaseAmplitude * env * Math.Sin(2.0 * Math.PI * freq * t));

                    // Write to all channels
                    int bufPos = (offsetSamples + i) * channels;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        if (bufPos + ch < buffer.Length)
                            buffer[bufPos + ch] = sample;
                    }
                }

                // Advance by the interval duration
                offsetSamples += (int)(sampleRate * interval / 1000.0);
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
