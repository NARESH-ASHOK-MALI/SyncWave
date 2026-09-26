using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SyncWave.Models;
using SyncWave.Utils;

namespace SyncWave.Core
{
    /// <summary>
    /// Manages parallel audio output streams to multiple devices.
    /// Each device gets its own WasapiOut + BufferedWaveProvider.
    /// Supports hot-plug add/remove of devices during playback.
    /// </summary>
    public class AudioOutputService : IDisposable
    {
        /// <summary>
        /// Internal state for one output device stream.
        /// </summary>
        private class DeviceStream : IDisposable
        {
            public string DeviceId { get; }
            public WasapiOut? Player { get; set; }
            public BufferedWaveProvider? Buffer { get; set; }
            public VolumeWaveProvider? VolumeProvider { get; set; }
            public bool IsActive { get; set; }
            public bool HasError { get; set; }

            public DeviceStream(string deviceId)
            {
                DeviceId = deviceId;
            }

            public void Dispose()
            {
                try
                {
                    if (Player != null)
                    {
                        if (Player.PlaybackState == PlaybackState.Playing)
                            Player.Stop();
                        Player.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Error disposing device stream {DeviceId}: {ex.Message}");
                }
                Player = null;
                Buffer = null;
                VolumeProvider = null;
                IsActive = false;
            }
        }

        /// <summary>
        /// Simple WaveProvider that returns impulse data once then silence.
        /// </summary>
        private class ImpulseWaveProvider : IWaveProvider
        {
            private readonly byte[] _impulseData;
            private readonly WaveFormat _format;
            private long _position;

            public ImpulseWaveProvider(byte[] impulseData, WaveFormat format)
            {
                _impulseData = impulseData;
                _format = format;
                _position = 0;
            }

            public WaveFormat WaveFormat => _format;

            public int Read(byte[] buffer, int offset, int count)
            {
                if (_position >= _impulseData.Length)
                {
                    // Return silence after impulse
                    int bytesToCopy = Math.Min(count, _impulseData.Length); // Actually should be count for silence
                    // Clear the buffer for silence
                    for (int i = offset; i < offset + count; i++)
                    {
                        buffer[i] = 0;
                    }
                    return count;
                }

                int remaining = _impulseData.Length - (int)_position;
                int toCopy = Math.Min(count, remaining);
                Buffer.BlockCopy(_impulseData, (int)_position, buffer, offset, toCopy);
                _position += toCopy;

                // Fill remainder with silence
                if (toCopy < count)
                {
                    for (int i = offset + toCopy; i < offset + count; i++)
                    {
                        buffer[i] = 0;
                    }
                }

                return count;
            }
        }

        private readonly ConcurrentDictionary<string, DeviceStream> _streams = new();
        private readonly LatencyManager _latencyManager;
        private readonly ConcurrentDictionary<string, float> _deviceVolumes = new();
        private readonly ConcurrentDictionary<string, float> _originalSystemVolumes = new();
        private readonly ConcurrentDictionary<string, double> _measuredLatencies = new();
        private WaveFormat? _sourceFormat;
        private bool _isPlaying;
        private readonly object _lock = new();

        // Reconnection
        private CancellationTokenSource? _reconnectCts;
        private readonly ConcurrentDictionary<string, AudioDeviceModel> _disconnectedDevices = new();

        /// <summary>Raised when a device disconnects unexpectedly.</summary>
        public event Action<string, string>? DeviceError;

        /// <summary>Raised when a device is reconnected.</summary>
        public event Action<string>? DeviceReconnected;

        public AudioOutputService(LatencyManager latencyManager)
        {
            _latencyManager = latencyManager;
        }

        public bool IsHighPerformanceModeEnabled { get; set; }

        /// <summary>
        /// Sets the source audio format. Must be called before adding devices.
        /// </summary>
        public void SetSourceFormat(WaveFormat format)
        {
            _sourceFormat = format;
            _latencyManager.SetFormat(format);
            Logger.Info($"Output source format set: {format.SampleRate} Hz, {format.BitsPerSample} bit, " +
                        $"{format.Channels} ch, Encoding: {format.Encoding}");
        }

        /// <summary>
        /// Determines the optimal latency for a device based on its connection type.
        /// Bluetooth devices need higher latency to avoid underruns.
        /// </summary>
        private int GetDesiredLatency(string deviceType)
        {
            if (IsHighPerformanceModeEnabled)
                return 15;

            return deviceType.ToLowerInvariant() switch
            {
                "bluetooth" => 80,  // BT needs more buffer headroom
                "hdmi" => 50,       // HDMI is moderate latency
                _ => 30             // Wired/USB can handle very low latency
            };
        }

        /// <summary>
        /// Measures the actual latency of a device using loopback capture.
        /// Returns the latency in milliseconds, or null if measurement fails.
        /// </summary>
        public async Task<double?> MeasureDeviceLatencyAsync(string deviceId, int timeoutSeconds = 5)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                var token = cts.Token;

                var enumerator = new MMDeviceEnumerator();
                var mmDevice = enumerator.GetDevice(deviceId);

                // Get the output format of the device by creating a temporary WasapiOut
                WaveFormat recordFormat;
                using (var tempOut = new WasapiOut(mmDevice, AudioClientShareMode.Shared, false, 10))
                {
                    recordFormat = tempOut.OutputWaveFormat;
                }

                // Prepare impulse: a short buffer of silence with a single spike
                int impulseLengthSamples = recordFormat.SampleRate / 100; // 10 ms impulse buffer
                float[] impulse = new float[impulseLengthSamples];
                // Place a spike at the middle
                int spikeIndex = impulseLengthSamples / 2;
                impulse[spikeIndex] = 1.0f; // full scale float

                // Convert impulse to byte array based on recordFormat
                byte[] impulseBytes;
                using (var impulseStream = new MemoryStream())
                {
                    using (var writer = new BinaryWriter(impulseStream))
                    {
                        foreach (var sample in impulse)
                        {
                            // Write as 32-bit float
                            writer.Write(sample);
                        }
                    }
                    impulseBytes = impulseStream.ToArray();
                }

                // Set up output device (WasapiOut)
                using var output = new WasapiOut(
                    mmDevice,
                    AudioClientShareMode.Shared,
                    false, // useEventSync: false for simplicity
                    10 // latency in milliseconds (low latency for measurement)
                );

                // We need to provide our impulse via a WaveProvider
                // Create a custom WaveProvider that returns our impulse once
                var impulseProvider = new ImpulseWaveProvider(impulseBytes, recordFormat);
                output.Init(impulseProvider);

                // Set up loopback capture from the same render device
                using var capture = new WasapiLoopbackCapture(mmDevice)
                {
                    WaveFormat = recordFormat
                };

                // Buffer to hold captured samples
                var capturedSamples = new List<float>();
                capture.DataAvailable += (s, e) =>
                {
                    if (token.IsCancellationRequested) return;
                    // Convert captured bytes to float[]
                    int bytesPerSample = recordFormat.BitsPerSample / 8;
                    int samples = e.BytesRecorded / bytesPerSample;
                    float[] floats = new float[samples];

                    if (recordFormat.Encoding == WaveFormatEncoding.Pcm)
                    {
                        // For PCM, we need to convert based on bits per sample
                        if (recordFormat.BitsPerSample == 16)
                        {
                            for (int i = 0; i < samples; i++)
                            {
                                short sample = BitConverter.ToInt16(e.Buffer, i * 2);
                                floats[i] = sample / 32768.0f;
                            }
                        }
                        else if (recordFormat.BitsPerSample == 24)
                        {
                            // 24-bit is tricky, we'll skip for now and fall back to float
                            for (int i = 0; i < samples; i++)
                            {
                                // Read 3 bytes and convert to 32-bit int then float
                                int sample = (e.Buffer[i * 3] |
                                             (e.Buffer[i * 3 + 1] << 8) |
                                             (e.Buffer[i * 3 + 2] << 16));
                                // Sign extend from 24 to 32 bits
                                if ((sample & 0x800000) != 0)
                                    sample |= unchecked((int)0xFF000000);
                                floats[i] = sample / 8388608.0f;
                            }
                        }
                        else // 32-bit PCM
                        {
                            for (int i = 0; i < samples; i++)
                            {
                                int sample = BitConverter.ToInt32(e.Buffer, i * 4);
                                floats[i] = sample / 2147483648.0f;
                            }
                        }
                    }
                    else if (recordFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                    {
                        // Already float, just copy
                        Buffer.BlockCopy(e.Buffer, 0, floats, 0, (int)e.BytesRecorded);
                    }

                    capturedSamples.AddRange(floats);
                };

                // Start capture
                capture.StartRecording();

                // Wait a short time before playing impulse to ensure capture is ready
                await Task.Delay(10, token);

                // Play the impulse
                output.Play();

                // Wait for the impulse to play out plus some extra time for capture
                // We'll wait for impulse length + 200 ms
                int waitTimeMs = (int)((impulseLengthSamples * 1000.0) / recordFormat.SampleRate) + 200;
                await Task.Delay(waitTimeMs, token);

                // Stop capture and output
                capture.StopRecording();
                output.Stop();

                // Now we have capturedSamples as list of floats
                if (capturedSamples.Count == 0)
                {
                    return null;
                }

                float[] captured = capturedSamples.ToArray();

                // Compute cross-correlation between captured and impulse
                // We'll compute for lags from -maxLag to +maxLag where maxLag is maybe 200 ms
                int maxLagSamples = (int)(recordFormat.SampleRate * 0.2); // 200 ms
                double maxCorr = double.MinValue;
                int bestLag = 0;

                for (int lag = -maxLagSamples; lag <= maxLagSamples; lag++)
                {
                    double sum = 0.0;
                    int samplesOverlap = Math.Min(impulse.Length, captured.Length - Math.Abs(lag));
                    if (samplesOverlap <= 0) continue;

                    for (int i = 0; i < samplesOverlap; i++)
                    {
                        int capturedIndex = i + Math.Max(lag, 0);
                        int impulseIndex = i + Math.Max(-lag, 0);
                        sum += captured[capturedIndex] * impulse[impulseIndex];
                    }

                    if (sum > maxCorr)
                    {
                        maxCorr = sum;
                        bestLag = lag;
                    }
                }

                // The delay in seconds is bestLag / sampleRate
                // Positive lag means captured signal lags behind impulse (output then capture)
                double delaySeconds = bestLag / (double)recordFormat.SampleRate;
                double delayMs = delaySeconds * 1000.0;

                // Ensure non-negative
                if (delayMs < 0) delayMs = 0;

                return delayMs;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Latency measurement failed for device {deviceId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Initializes and starts output on the given device.
        /// Uses event-driven WASAPI with adaptive latency based on device type.
        /// </summary>
        public void AddDevice(AudioDeviceModel device)
        {
            if (_sourceFormat == null)
            {
                Logger.Error("Cannot add device: source format not set.");
                device.HasError = true;
                device.StatusText = "Error: No capture format";
                return;
            }

            lock (_lock)
            {
                if (_streams.ContainsKey(device.DeviceId))
                {
                    Logger.Warn($"Device already added: {device.FriendlyName}");
                    return;
                }

                try
                {
                    Logger.Info($"Adding output device: {device.FriendlyName} ({device.DeviceId})");

                    var enumerator = new MMDeviceEnumerator();
                    var mmDevice = enumerator.GetDevice(device.DeviceId);

                    if (mmDevice.State != DeviceState.Active)
                    {
                        Logger.Warn($"Device not active: {device.FriendlyName} (state: {mmDevice.State})");
                        device.HasError = true;
                        device.StatusText = "Device not active";
                        return;
                    }

                    var stream = new DeviceStream(device.DeviceId);

                    // Create buffered provider (2 seconds — reduced from 5s, still generous)
                    stream.Buffer = new BufferedWaveProvider(_sourceFormat)
                    {
                        BufferLength = _sourceFormat.AverageBytesPerSecond * 2,
                        DiscardOnBufferOverflow = true
                    };

                    Logger.Info($"Created buffer: {stream.Buffer.BufferLength} bytes, format: {_sourceFormat}");

                    // Wrap buffer in VolumeWaveProvider for instant real-time volume control
                    stream.VolumeProvider = new VolumeWaveProvider(stream.Buffer);

                    // Set initial volume from the device model
                    float initVol = _deviceVolumes.GetValueOrDefault(device.DeviceId, 1.0f);
                    stream.VolumeProvider.Volume = initVol;

                    // ── Endpoint volume normalization ──────────────────────
                    // Save original volume BEFORE any corrections, then ensure
                    // the endpoint is unmuted and not at zero, then lock to 100%
                    // so SyncWave's slider has full control.
                    try
                    {
                        var epVol = mmDevice.AudioEndpointVolume;
                        if (epVol != null)
                        {
                            // Save original system volume for restoration on StopAll()
                            _originalSystemVolumes.TryAdd(device.DeviceId, epVol.MasterVolumeLevelScalar);
                            Logger.Info($"Saved original system volume: {epVol.MasterVolumeLevelScalar * 100:F0}% for {device.FriendlyName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Could not save system volume for {device.FriendlyName}: {ex.Message}");
                    }

                    // Unmute + raise from zero if needed (one-time correction, not a persistent lock)
                    AudioEndpointUtils.EnsureEndpointIsAudible(mmDevice);

                    // Unconditionally set to 100% so the app slider is the sole volume control
                    try
                    {
                        var epVol = mmDevice.AudioEndpointVolume;
                        if (epVol != null)
                        {
                            epVol.MasterVolumeLevelScalar = 1.0f;
                            Logger.Info($"Set system volume to 100% for {device.FriendlyName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Could not save system volume for {device.FriendlyName}: {ex.Message}");
                    }

                    // Try to get measured latency, fallback to desired latency based on device type
                    double latencyToUseMs = GetDesiredLatency(device.DeviceType);
                    if (_measuredLatencies.TryGetValue(device.DeviceId, out var measuredLatency))
                    {
                        latencyToUseMs = measuredLatency;
                    }

                    // Event-driven mode (useEventSync: true) — lower jitter than timer mode
                    // Falls back to shared mode for compatibility
                    stream.Player = new WasapiOut(mmDevice, AudioClientShareMode.Shared, true, (int)latencyToUseMs);
                    stream.Player.PlaybackStopped += (s, e) => OnPlaybackStopped(device.DeviceId, device.FriendlyName, e);
                    stream.Player.Init(stream.VolumeProvider);

                    // Pre-fill with silence matching the desired latency to prevent underruns
                    if (!IsHighPerformanceModeEnabled)
                    {
                        int prefillBytes = _sourceFormat.AverageBytesPerSecond * (int)latencyToUseMs / 1000;
                        prefillBytes = (prefillBytes / _sourceFormat.BlockAlign) * _sourceFormat.BlockAlign;
                        var silence = new byte[prefillBytes];
                        stream.Buffer.AddSamples(silence, 0, silence.Length);
                    }

                    Logger.Info($"WasapiOut initialized for {device.FriendlyName}, " +
                                $"mode: event-driven, latency: {(int)latencyToUseMs}ms, " +
                                $"output format: {stream.Player.OutputWaveFormat}");

                    if (_isPlaying)
                    {
                        stream.Player.Play();
                        stream.IsActive = true;
                        Logger.Info($"Playback started immediately for {device.FriendlyName}");
                    }

                    _streams[device.DeviceId] = stream;

                    // Use the measured latency (or desired if not measured) for latency management
                    device.MeasuredLatency = latencyToUseMs;
                    _latencyManager.SetDeviceLatency(device.DeviceId, latencyToUseMs);

                    device.IsActive = true;
                    device.HasError = false;
                    device.StatusText = _isPlaying ? "Streaming" : "Ready";
                    device.BufferHealth = 50;

                    Logger.Info($"✓ Device added successfully: {device.FriendlyName}");

                    // Start measurement task to update latency for future use (fire and forget)
                    _ = MeasureDeviceLatencyAsync(device.DeviceId).ContinueWith(t =>
                    {
                        if (t.Result.HasValue)
                        {
                            double measured = t.Result.Value;
                            _measuredLatencies[device.DeviceId] = measured;
                            // Update the latency manager and the device model
                            _latencyManager.SetDeviceLatency(device.DeviceId, measured);
                            // Update the device model if it's still active
                            if (_streams.TryGetValue(device.DeviceId, out var currentStream) && currentStream.VolumeProvider != null)
                            {
                                // We don't have direct access to the device model here, but we could store it elsewhere.
                                // For now, we rely on the latency manager having the updated value.
                                // The device model's MeasuredLatency will be updated when the device is next accessed via the UI?
                                // We could update it by finding the device model in a collection, but we don't have that here.
                                // We'll leave it to the latency manager to have the correct value.
                            }
                        }
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to add device: {device.FriendlyName}", ex);
                    device.HasError = true;
                    device.IsActive = false;
                    device.StatusText = $"Error: {ex.Message}";
                    DeviceError?.Invoke(device.DeviceId, ex.Message);
                }
            }
        }

        /// <summary>
        /// Stops and removes a device from the output set.
        /// </summary>
        public void RemoveDevice(string deviceId)
        {
            lock (_lock)
            {
                if (_streams.TryRemove(deviceId, out var stream))
                {
                    stream.Dispose();
                    _latencyManager.RemoveDevice(deviceId);
                    Logger.Info($"Removed output device: {deviceId}");
                }
            }
        }

        /// <summary>
        /// Starts playback on all added devices.
        /// </summary>
        public void StartAll()
        {
            lock (_lock)
            {
                _isPlaying = true;
                int started = 0;

                foreach (var kvp in _streams)
                {
                    try
                    {
                        if (kvp.Value.Player != null && kvp.Value.Player.PlaybackState != PlaybackState.Playing)
                        {
                            kvp.Value.Player.Play();
                            kvp.Value.IsActive = true;
                            started++;
                            Logger.Info($"Started playback on device: {kvp.Key}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to start playback on {kvp.Key}", ex);
                        kvp.Value.HasError = true;
                        kvp.Value.IsActive = false;
                        DeviceError?.Invoke(kvp.Key, ex.Message);
                    }
                }

                // Start reconnection monitoring
                _reconnectCts = new CancellationTokenSource();
                Task.Run(() => ReconnectionLoop(_reconnectCts.Token));

                Logger.Info($"Playback started on {started}/{_streams.Count} devices.");
            }
        }

        /// <summary>
        /// Stops playback on all devices and cleans up.
        /// </summary>
        public void StopAll()
        {
            lock (_lock)
            {
                _isPlaying = false;
                _reconnectCts?.Cancel();
                _reconnectCts = null;

                foreach (var kvp in _streams)
                {
                    kvp.Value.Dispose();
                }
                _streams.Clear();
                _disconnectedDevices.Clear();
                _latencyManager.ClearAll();

                // Restore original system volumes
                RestoreSystemVolumes();

                Logger.Info("All output streams stopped and cleaned up.");
            }
        }

        /// <summary>
        /// Sets the volume for a specific device (0.0 to 2.0).
        /// </summary>
        public void SetDeviceVolume(string deviceId, float volume)
        {
            float clamped = Math.Clamp(volume, 0f, 2.0f);
            _deviceVolumes[deviceId] = clamped;

            // Apply to the VolumeWaveProvider immediately for instant effect
            if (_streams.TryGetValue(deviceId, out var stream) && stream.VolumeProvider != null)
            {
                stream.VolumeProvider.Volume = clamped;
            }
        }

        /// <summary>
        /// Distributes captured audio data to all active output buffers,
        /// applying per-device latency compensation and volume scaling.
        /// </summary>
        public void DistributeAudio(byte[] data, int byteCount)
        {
            if (!_isPlaying || byteCount <= 0) return;

            foreach (var kvp in _streams)
            {
                var stream = kvp.Value;
                if (!stream.IsActive || stream.HasError || stream.Buffer == null)
                    continue;

                try
                {
                    // Apply latency compensation delay
                    var delayedData = _latencyManager.ApplyDelay(kvp.Key, data, byteCount, _sourceFormat!, out int outputLength);

                    // Volume is applied at read-time by VolumeWaveProvider
                    // so we just write the raw (delayed) data to the buffer
                    stream.Buffer.AddSamples(delayedData, 0, outputLength);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Buffer write error for {kvp.Key}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Gets the buffer health percentage for a specific device.
        /// Returns ratio of buffered data to total buffer.
        /// </summary>
        public double GetBufferHealth(string deviceId)
        {
            if (_streams.TryGetValue(deviceId, out var stream) && stream.Buffer != null)
            {
                // BufferLength is the total size in bytes
                // BufferedBytes is how many bytes are currently buffered
                return (double)stream.Buffer.BufferedBytes / stream.Buffer.BufferLength * 100.0;
            }
            return 0;
        }

        /// <summary>
        /// Restoration logic for system volumes when stopping playback.
        /// </summary>
        private void RestoreSystemVolumes()
        {
            try
            {
                var enumerator = new MMDeviceEnumerator();
                foreach (var kvp in _originalSystemVolumes)
                {
                    string deviceId = kvp.Key;
                    float originalVolume = kvp.Value;

                    try
                    {
                        var mmDevice = enumerator.GetDevice(deviceId);
                        var epVol = mmDevice.AudioEndpointVolume;
                        if (epVol != null)
                        {
                            epVol.MasterVolumeLevelScalar = originalVolume;
                            Logger.Info($"Restored system volume to {originalVolume * 100:F0}% for device {deviceId}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to restore system volume for device {deviceId}: {ex.Message}");
                    }
                }
                _originalSystemVolumes.Clear();
            }
            catch (Exception ex)
            {
                Logger.Warn($"Error restoring system volumes: {ex.Message}");
            }
        }

        /// <summary>
        /// Reconnection loop that monitors for disconnected devices and attempts to reconnect them.
        /// </summary>
        private void ReconnectionLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Check each disconnected device
                    var disconnected = _disconnectedDevices.ToList();
                    foreach (var kvp in disconnected)
                    {
                        string deviceId = kvp.Key;
                        AudioDeviceModel device = kvp.Value;

                        try
                        {
                            var enumerator = new MMDeviceEnumerator();
                            var mmDevice = enumerator.GetDevice(deviceId);

                            // If device is now active, attempt to reconnect
                            if (mmDevice.State == DeviceState.Active)
                            {
                                Logger.Info($"Device {device.FriendlyName} reconnected, attempting recovery...");
                                _disconnectedDevices.TryRemove(deviceId, out _);

                                // Re-add the device
                                AddDevice(device);

                                // Notify UI of reconnection
                                DeviceReconnected?.Invoke(deviceId);
                            }
                        }
                        catch (Exception ex)
                        {
                            // Device still not available or other error, try again later
                            Logger.Warn($"Reconnection attempt failed for {device.FriendlyName}: {ex.Message}");
                        }
                    }

                    // Wait before next check
                    Thread.Sleep(5000); // 5 seconds
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Error in reconnection loop: {ex.Message}");
                    Thread.Sleep(5000); // Wait before retrying
                }
            }
        }

        /// <summary>
        /// Handles playback stopped events from WasapiOut.
        /// </summary>
        private void OnPlaybackStopped(string deviceId, string friendlyName, NAudio.Wave.StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                Logger.Error($"Playback stopped on device {friendlyName} due to error: {e.Exception.Message}");
                if (_streams.TryGetValue(deviceId, out var stream))
                {
                    stream.HasError = true;
                    stream.IsActive = false;
                    DeviceError?.Invoke(deviceId, e.Exception.Message);
                }
            }
            else
            {
                Logger.Info($"Playback stopped on device {friendlyName}");
                if (_streams.TryGetValue(deviceId, out var stream))
                {
                    stream.IsActive = false;
                }
            }
        }

        /// <summary>
        /// Gets whether a device is currently active (streaming).
        /// </summary>
        public bool IsDeviceActive(string deviceId)
        {
            return _streams.TryGetValue(deviceId, out var stream) && stream.IsActive;
        }

        public void Dispose()
        {
            StopAll();
            _streams.Clear();
            _disconnectedDevices.Clear();
            _measuredLatencies.Clear();
            _deviceVolumes.Clear();
            _originalSystemVolumes.Clear();
        }
    }
}