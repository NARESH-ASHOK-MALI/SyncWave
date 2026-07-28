using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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

        private readonly ConcurrentDictionary<string, DeviceStream> _streams = new();
        private readonly LatencyManager _latencyManager;
        private readonly ConcurrentDictionary<string, float> _deviceVolumes = new();
        private readonly ConcurrentDictionary<string, float> _originalSystemVolumes = new();
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
        private static int GetDesiredLatency(string deviceType)
        {
            return deviceType.ToLowerInvariant() switch
            {
                "bluetooth" => 80,  // BT needs more buffer headroom
                "hdmi" => 50,       // HDMI is moderate latency
                _ => 30             // Wired/USB can handle very low latency
            };
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

                    // Adaptive latency based on device type
                    int desiredLatencyMs = GetDesiredLatency(device.DeviceType);

                    // Event-driven mode (useEventSync: true) — lower jitter than timer mode
                    // Falls back to shared mode for compatibility
                    stream.Player = new WasapiOut(mmDevice, AudioClientShareMode.Shared, true, desiredLatencyMs);
                    stream.Player.PlaybackStopped += (s, e) => OnPlaybackStopped(device.DeviceId, device.FriendlyName, e);
                    stream.Player.Init(stream.VolumeProvider);

                    // Pre-fill with silence matching the desired latency to prevent underruns
                    int prefillBytes = _sourceFormat.AverageBytesPerSecond * desiredLatencyMs / 1000;
                    prefillBytes = (prefillBytes / _sourceFormat.BlockAlign) * _sourceFormat.BlockAlign;
                    var silence = new byte[prefillBytes];
                    stream.Buffer.AddSamples(silence, 0, silence.Length);

                    Logger.Info($"WasapiOut initialized for {device.FriendlyName}, " +
                                $"mode: event-driven, latency: {desiredLatencyMs}ms, " +
                                $"output format: {stream.Player.OutputWaveFormat}");

                    if (_isPlaying)
                    {
                        stream.Player.Play();
                        stream.IsActive = true;
                        Logger.Info($"Playback started immediately for {device.FriendlyName}");
                    }

                    _streams[device.DeviceId] = stream;

                    // Use the desired latency as the measured latency estimate
                    device.MeasuredLatency = desiredLatencyMs;
                    _latencyManager.SetDeviceLatency(device.DeviceId, desiredLatencyMs);

                    // Set system volume to 100% so SyncWave's slider has full control
                    try
                    {
                        var vol = mmDevice.AudioEndpointVolume;
                        if (vol != null)
                        {
                            // Save original system volume for restoration
                            _originalSystemVolumes.TryAdd(device.DeviceId, vol.MasterVolumeLevelScalar);
                            vol.MasterVolumeLevelScalar = 1.0f; // 100%
                            Logger.Info($"Set system volume to 100% for {device.FriendlyName} (was {_originalSystemVolumes[device.DeviceId] * 100:F0}%)");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Could not set system volume for {device.FriendlyName}: {ex.Message}");
                    }

                    device.IsActive = true;
                    device.HasError = false;
                    device.StatusText = _isPlaying ? "Streaming" : "Ready";
                    device.BufferHealth = 50;

                    Logger.Info($"✓ Device added successfully: {device.FriendlyName}");
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
                var usedPercent = (double)stream.Buffer.BufferedBytes / stream.Buffer.BufferLength * 100;
                return Math.Clamp(usedPercent, 0, 100);
            }
            return 0;
        }

        /// <summary>
        /// Checks if a specific device stream is currently active and healthy.
        /// </summary>
        public bool IsDeviceActive(string deviceId)
        {
            return _streams.TryGetValue(deviceId, out var stream) && stream.IsActive && !stream.HasError;
        }

        /// <summary>
        /// Handles unexpected playback stops (device disconnection).
        /// </summary>
        private void OnPlaybackStopped(string deviceId, string friendlyName, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                Logger.Error($"Device '{friendlyName}' ({deviceId}) playback stopped with error", e.Exception);
                if (_streams.TryGetValue(deviceId, out var stream))
                {
                    stream.HasError = true;
                    stream.IsActive = false;
                }

                // Track for reconnection
                var model = new AudioDeviceModel { DeviceId = deviceId, FriendlyName = friendlyName };
                _disconnectedDevices[deviceId] = model;

                DeviceError?.Invoke(deviceId, e.Exception.Message);
            }
            else
            {
                Logger.Info($"Device '{friendlyName}' playback stopped normally.");
            }
        }

        /// <summary>
        /// Background loop that attempts to reconnect disconnected devices.
        /// </summary>
        private async Task ReconnectionLoop(CancellationToken ct)
        {
            Logger.Info("Reconnection monitor started.");
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(3000, ct);

                    foreach (var kvp in _disconnectedDevices.ToArray())
                    {
                        if (ct.IsCancellationRequested) break;

                        try
                        {
                            var enumerator = new MMDeviceEnumerator();
                            var mmDevice = enumerator.GetDevice(kvp.Key);

                            if (mmDevice.State == DeviceState.Active)
                            {
                                Logger.Info($"Device reconnected: {kvp.Value.FriendlyName}");
                                _disconnectedDevices.TryRemove(kvp.Key, out _);

                                // Clean up old stream
                                if (_streams.TryRemove(kvp.Key, out var oldStream))
                                {
                                    oldStream.Dispose();
                                }

                                DeviceReconnected?.Invoke(kvp.Key);
                            }
                        }
                        catch
                        {
                            // Device still unavailable — continue polling
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error("Reconnection loop error", ex);
                }
            }
            Logger.Info("Reconnection monitor stopped.");
        }

        /// <summary>
        /// Restores original Windows system volume levels for all devices.
        /// </summary>
        private void RestoreSystemVolumes()
        {
            foreach (var kvp in _originalSystemVolumes)
            {
                try
                {
                    var enumerator = new MMDeviceEnumerator();
                    var mmDevice = enumerator.GetDevice(kvp.Key);
                    if (mmDevice.State == DeviceState.Active)
                    {
                        mmDevice.AudioEndpointVolume.MasterVolumeLevelScalar = kvp.Value;
                        Logger.Info($"Restored system volume to {kvp.Value * 100:F0}% for {mmDevice.FriendlyName}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Could not restore system volume for {kvp.Key}: {ex.Message}");
                }
            }
            _originalSystemVolumes.Clear();
        }

        public void Dispose()
        {
            StopAll();
            GC.SuppressFinalize(this);
        }
    }
}
