using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SyncWave.Utils;

namespace SyncWave.Core
{
    /// <summary>
    /// Captures system audio via WASAPI loopback (records what you hear).
    /// Raises DataAvailable events with timestamped PCM buffers.
    /// </summary>
    public class AudioCaptureService : IDisposable
    {
        private WasapiLoopbackCapture? _capture;
        private bool _isCapturing;
        private readonly object _lock = new();

        /// <summary>Fired when new audio data is captured. Provides buffer + byte count.</summary>
        public event Action<byte[], int>? DataAvailable;

        /// <summary>Fired when the capture format is determined.</summary>
        public event Action<WaveFormat>? CaptureFormatAvailable;

        /// <summary>Current capture state.</summary>
        public bool IsCapturing => _isCapturing;

        /// <summary>The active capture wave format (set after Start).</summary>
        public WaveFormat? CaptureFormat => _capture?.WaveFormat;

        /// <summary>
        /// Starts WASAPI loopback capture on the default render device.
        /// </summary>
        public void Start()
        {
            lock (_lock)
            {
                if (_isCapturing)
                {
                    Logger.Warn("Capture already running — ignoring Start().");
                    return;
                }

                try
                {
                    _capture = new WasapiLoopbackCapture();
                    
                    Logger.Info($"Capture format: {_capture.WaveFormat.SampleRate} Hz, " +
                                $"{_capture.WaveFormat.BitsPerSample} bit, " +
                                $"{_capture.WaveFormat.Channels} ch, " +
                                $"Encoding: {_capture.WaveFormat.Encoding}");

                    _capture.DataAvailable += OnDataAvailable;
                    _capture.RecordingStopped += OnRecordingStopped;

                    _capture.StartRecording();
                    _isCapturing = true;

                    CaptureFormatAvailable?.Invoke(_capture.WaveFormat);
                    Logger.Info("WASAPI loopback capture started.");
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to start audio capture", ex);
                    Cleanup();
                    throw;
                }
            }
        }

        /// <summary>
        /// Stops the loopback capture gracefully.
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                if (!_isCapturing)
                    return;

                try
                {
                    _capture?.StopRecording();
                    Logger.Info("WASAPI loopback capture stopped.");
                }
                catch (Exception ex)
                {
                    Logger.Error("Error stopping capture", ex);
                }
                finally
                {
                    _isCapturing = false;
                }
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded > 0)
            {
                // Copy buffer to avoid NAudio overwriting it
                var buffer = new byte[e.BytesRecorded];
                Buffer.BlockCopy(e.Buffer, 0, buffer, 0, e.BytesRecorded);
                DataAvailable?.Invoke(buffer, e.BytesRecorded);
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                Logger.Error("Capture stopped unexpectedly", e.Exception);
            }
            Cleanup();
        }

        private void Cleanup()
        {
            if (_capture != null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.Dispose();
                _capture = null;
            }
            _isCapturing = false;
        }

        public void Dispose()
        {
            Stop();
            Cleanup();
            GC.SuppressFinalize(this);
        }
    }
}
