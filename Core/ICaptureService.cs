using System;
using NAudio.Wave;

namespace SyncWave.Core
{
    /// <summary>
    /// Common interface for audio capture services.
    /// Both AudioCaptureService (WASAPI loopback) and ProcessLoopbackCaptureService
    /// implement this so MainViewModel can swap between them with a single line change.
    /// </summary>
    public interface ICaptureService : IDisposable
    {
        /// <summary>Fired when new audio data is captured. Provides buffer + byte count.</summary>
        event Action<byte[], int>? DataAvailable;

        /// <summary>Fired when the capture format is determined.</summary>
        event Action<WaveFormat>? CaptureFormatAvailable;

        /// <summary>Current capture state.</summary>
        bool IsCapturing { get; }

        /// <summary>The active capture wave format (set after Start).</summary>
        WaveFormat? CaptureFormat { get; }

        /// <summary>Starts audio capture.</summary>
        void Start();

        /// <summary>Stops audio capture gracefully.</summary>
        void Stop();
    }
}
