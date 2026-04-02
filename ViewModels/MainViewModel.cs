using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using SyncWave.Core;
using SyncWave.Models;
using SyncWave.Utils;

namespace SyncWave.ViewModels
{
    /// <summary>
    /// Main ViewModel — wires capture → latency → output pipeline,
    /// exposes device list, commands, status, and waveform data for binding.
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        // ── Services ──────────────────────────────────────────────
        private readonly AudioCaptureService _captureService;
        private readonly AudioOutputService _outputService;
        private readonly LatencyManager _latencyManager;

        // ── Timers ────────────────────────────────────────────────
        private readonly DispatcherTimer _deviceRefreshTimer;
        private readonly DispatcherTimer _monitorTimer;

        // ── Backing fields ────────────────────────────────────────
        private string _playbackStatus = "Idle";
        private SolidColorBrush _statusBrush = new(Color.FromRgb(0x88, 0x88, 0x88));
        private double _masterDelayOffset;
        private int _activeDeviceCount;
        private double _maxLatencyDiff;
        private bool _isSyncing;
        private string _errorMessage = string.Empty;
        private double _audioLevel;

        // ── Saved profiles (loaded once at startup) ───────────────
        private Dictionary<string, DeviceProfile> _savedProfiles = new();

        // ── Waveform ──────────────────────────────────────────────
        private double[] _waveformData = new double[200];
        private int _waveformWritePos;
        public double[] WaveformData
        {
            get => _waveformData;
            private set { _waveformData = value; OnPropertyChanged(); }
        }

        // ── Properties ────────────────────────────────────────────
        public ObservableCollection<AudioDeviceModel> Devices { get; } = new();

        public string PlaybackStatus
        {
            get => _playbackStatus;
            set { _playbackStatus = value; OnPropertyChanged(); UpdateStatusBrush(); }
        }

        /// <summary>SolidColorBrush for status indicator — bindable directly to Fill/Foreground.</summary>
        public SolidColorBrush StatusBrush
        {
            get => _statusBrush;
            set { _statusBrush = value; OnPropertyChanged(); }
        }

        public double MasterDelayOffset
        {
            get => _masterDelayOffset;
            set
            {
                _masterDelayOffset = value;
                OnPropertyChanged();
                foreach (var d in Devices.Where(d => d.IsSelected))
                    _latencyManager.SetManualDelay(d.DeviceId, d.ManualDelay + _masterDelayOffset);
            }
        }

        public int ActiveDeviceCount
        {
            get => _activeDeviceCount;
            set { _activeDeviceCount = value; OnPropertyChanged(); }
        }

        public double MaxLatencyDiff
        {
            get => _maxLatencyDiff;
            set { _maxLatencyDiff = value; OnPropertyChanged(); }
        }

        public bool IsSyncing
        {
            get => _isSyncing;
            set { _isSyncing = value; OnPropertyChanged(); RelayCommand.RaiseCanExecuteChanged(); }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        /// <summary>Current audio input level (0–1) for level meter.</summary>
        public double AudioLevel
        {
            get => _audioLevel;
            set { _audioLevel = Math.Clamp(value, 0, 1); OnPropertyChanged(); }
        }

        // ── Commands ──────────────────────────────────────────────
        public ICommand StartSyncCommand { get; }
        public ICommand StopSyncCommand { get; }
        public ICommand RefreshDevicesCommand { get; }

        // ── Constructor ───────────────────────────────────────────
        public MainViewModel()
        {
            _latencyManager = new LatencyManager();
            _captureService = new AudioCaptureService();
            _outputService = new AudioOutputService(_latencyManager);

            // Wire events
            _captureService.DataAvailable += OnCaptureDataAvailable;
            _outputService.DeviceError += OnDeviceError;
            _outputService.DeviceReconnected += OnDeviceReconnected;

            // Commands — use lambda-wrapped CanExecute for proper evaluation
            StartSyncCommand = new RelayCommand(
                _ => StartSync(),
                _ => !IsSyncing && Devices.Any(d => d.IsSelected));
            StopSyncCommand = new RelayCommand(
                _ => StopSync(),
                _ => IsSyncing);
            RefreshDevicesCommand = new RelayCommand(_ => RefreshDevices(syncVolume: true));

            // Device refresh timer (every 5 seconds)
            _deviceRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _deviceRefreshTimer.Tick += (_, _) =>
            {
                if (!IsSyncing) RefreshDevices(syncVolume: false);
            };

            // Monitoring timer (every 250ms for smooth UI updates)
            _monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _monitorTimer.Tick += (_, _) => UpdateMonitoring();

            // Load saved profiles before device enumeration
            _savedProfiles = DeviceProfileManager.Load();

            // Initial device enumeration
            RefreshDevices(syncVolume: true);

            _deviceRefreshTimer.Start();

            Logger.Info("MainViewModel initialized.");
        }

        // ── Methods ───────────────────────────────────────────────

        /// <summary>
        /// Reads the Windows system volume for a specific device (0–100%).
        /// Returns null if the volume cannot be read.
        /// </summary>
        private static double? ReadSystemVolume(MMDevice mmDevice)
        {
            try
            {
                var volume = mmDevice.AudioEndpointVolume;
                if (volume != null)
                {
                    // MasterVolumeLevelScalar is 0.0–1.0, convert to 0–100
                    return Math.Round(volume.MasterVolumeLevelScalar * 100, 0);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not read system volume for {mmDevice.FriendlyName}: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Enumerates all active audio render (output) devices.
        /// </summary>
        private void RefreshDevices(bool syncVolume = false)
        {
            try
            {
                var enumerator = new MMDeviceEnumerator();
                var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

                // Detect the default render device (loopback source)
                string defaultDeviceId = "";
                try
                {
                    var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    defaultDeviceId = defaultDevice.ID;
                }
                catch { /* No default device */ }

                // Track existing selections
                var existingSelections = Devices.Where(d => d.IsSelected)
                    .ToDictionary(d => d.DeviceId, d => d.ManualDelay);
                var existingDelays = Devices.ToDictionary(d => d.DeviceId, d => d.ManualDelay);

                var currentIds = endpoints.Select(e => e.ID).ToHashSet();
                var existingIds = Devices.Select(d => d.DeviceId).ToHashSet();

                // Remove devices that disappeared
                var toRemove = Devices.Where(d => !currentIds.Contains(d.DeviceId)).ToList();
                foreach (var d in toRemove)
                {
                    d.PropertyChanged -= OnDevicePropertyChanged;
                    Devices.Remove(d);
                }

                // Add new devices
                foreach (var ep in endpoints)
                {
                    if (!existingIds.Contains(ep.ID))
                    {
                        bool isDefault = ep.ID == defaultDeviceId;
                        var model = new AudioDeviceModel
                        {
                            DeviceId = ep.ID,
                            FriendlyName = ep.FriendlyName,
                            DeviceType = isDefault ? "🔈 Source" : DetectDeviceType(ep),
                            IsDefaultDevice = isDefault
                        };

                        // Volume initialization priority (updated per user request):
                        // 1. Windows system volume (Always synced on launch)
                        // 2. Saved profile volume (Fallback only)
                        var sysVol = ReadSystemVolume(ep);

                        if (_savedProfiles.TryGetValue(ep.ID, out var profile))
                        {
                            model.IsSelected = true;
                            model.ManualDelay = profile.Delay;
                            model.Volume = sysVol ?? profile.Volume;
                        }
                        else if (sysVol.HasValue)
                        {
                            model.Volume = sysVol.Value;
                        }

                        // Subscribe to property changes for delay slider and checkbox
                        model.PropertyChanged += OnDevicePropertyChanged;
                        Devices.Add(model);
                    }
                }

                // Update default device flag and sync system volume for existing devices
                foreach (var d in Devices)
                {
                    bool isDefault = d.DeviceId == defaultDeviceId;
                    if (d.IsDefaultDevice != isDefault)
                    {
                        d.IsDefaultDevice = isDefault;
                        d.DeviceType = isDefault ? "🔈 Source" : DetectDeviceType(enumerator.GetDevice(d.DeviceId));
                    }

                    // Sync volume from Windows system only on manual refresh
                    if (syncVolume)
                    {
                        try
                        {
                            var mmDevice = enumerator.GetDevice(d.DeviceId);
                            var sysVol = ReadSystemVolume(mmDevice);
                            if (sysVol.HasValue)
                            {
                                d.Volume = sysVol.Value;
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Could not sync volume for {d.FriendlyName}: {ex.Message}");
                        }
                    }
                }

                Logger.Info($"Device refresh: {Devices.Count} devices found. Default: {defaultDeviceId}");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to refresh devices", ex);
            }
        }

        /// <summary>
        /// Detects device type based on name heuristics.
        /// Covers common Bluetooth brands and connection types.
        /// </summary>
        private string DetectDeviceType(MMDevice device)
        {
            try
            {
                var name = device.FriendlyName.ToLowerInvariant();

                // Check multiple Bluetooth indicators
                string[] btKeywords = {
                    "bluetooth", "bt ", "bt-",
                    "airpods", "galaxy buds", "buds pro", "buds live",
                    "jbl ", "sony wh-", "sony wf-", "bose qc", "bose nc",
                    "boat", "rockerz", "airdopes", "bassheads",
                    "jabra", "sennheiser momentum", "beats ",
                    "marshall", "nothing ear", "pixel buds",
                    "oneplus buds", "realme buds",
                    "hands-free", "handsfree", "stereo"
                };

                foreach (var kw in btKeywords)
                {
                    if (name.Contains(kw))
                        return "Bluetooth";
                }

                // Also check the device's friendly name (adapter level)
                try
                {
                    var deviceName = device.DeviceFriendlyName?.ToLowerInvariant() ?? "";
                    if (deviceName.Contains("bluetooth") || deviceName.Contains("bth"))
                        return "Bluetooth";
                }
                catch { }

                if (name.Contains("usb"))
                    return "USB";
                if (name.Contains("hdmi") || name.Contains("displayport"))
                    return "HDMI";

                return "Wired";
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// Starts synchronized playback to all selected devices.
        /// </summary>
        private void StartSync()
        {
            Logger.Info(">>> StartSync() invoked.");
            try
            {
                var selectedDevices = Devices.Where(d => d.IsSelected).ToList();
                if (!selectedDevices.Any())
                {
                    ErrorMessage = "⚠ Please select at least one output device first.";
                    Logger.Warn("Start sync attempted with no devices selected.");
                    return;
                }

                ErrorMessage = string.Empty;
                PlaybackStatus = "Starting...";
                Logger.Info($"Starting sync with {selectedDevices.Count} device(s)...");

                // Start WASAPI loopback capture
                _captureService.Start();

                if (_captureService.CaptureFormat == null)
                {
                    ErrorMessage = "⚠ Failed to determine audio capture format. Is audio playing?";
                    Logger.Error("Capture format is null after starting capture.");
                    _captureService.Stop();
                    PlaybackStatus = "Error";
                    return;
                }

                Logger.Info($"Capture format: {_captureService.CaptureFormat}");

                // Configure output service with capture format
                _outputService.SetSourceFormat(_captureService.CaptureFormat);

                // Add selected devices to output (skip default device — echo prevention)
                int addedCount = 0;
                int skippedDefault = 0;
                foreach (var device in selectedDevices)
                {
                    if (device.IsDefaultDevice)
                    {
                        Logger.Info($"Skipping default device '{device.FriendlyName}' to prevent echo.");
                        device.StatusText = "Source (no echo)";
                        skippedDefault++;
                        continue;
                    }

                    _latencyManager.SetManualDelay(device.DeviceId, device.ManualDelay + MasterDelayOffset);
                    _outputService.SetDeviceVolume(device.DeviceId, (float)(device.Volume / 100.0));
                    _outputService.AddDevice(device);
                    if (!device.HasError) addedCount++;
                }

                if (addedCount == 0)
                {
                    ErrorMessage = "⚠ Failed to initialize any output devices. Check device connections.";
                    Logger.Error("No devices could be initialized.");
                    _captureService.Stop();
                    _outputService.StopAll();
                    PlaybackStatus = "Error";
                    return;
                }

                // Start all outputs
                _outputService.StartAll();

                IsSyncing = true;
                PlaybackStatus = "Syncing";
                ActiveDeviceCount = addedCount;
                _monitorTimer.Start();

                Logger.Info($"✓ Sync started successfully with {addedCount} device(s).");

                if (addedCount < selectedDevices.Count - skippedDefault)
                {
                    ErrorMessage = $"⚠ {selectedDevices.Count - skippedDefault - addedCount} device(s) failed to initialize.";
                }
                else if (skippedDefault > 0 && addedCount == 0)
                {
                    ErrorMessage = "⚠ Only your source device was selected. Select additional devices to route audio to.";
                    _captureService.Stop();
                    _outputService.StopAll();
                    IsSyncing = false;
                    PlaybackStatus = "Idle";
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to start sync", ex);
                PlaybackStatus = "Error";
                ErrorMessage = $"⚠ Start failed: {ex.Message}";
                StopSync();
            }
        }

        /// <summary>
        /// Stops all playback and cleans up resources.
        /// </summary>
        private void StopSync()
        {
            try
            {
                _monitorTimer.Stop();
                _captureService.Stop();
                _outputService.StopAll();

                foreach (var d in Devices)
                {
                    d.IsActive = false;
                    d.BufferHealth = 0;
                    d.StatusText = "Ready";
                    d.HasError = false;
                }

                IsSyncing = false;
                PlaybackStatus = "Idle";
                ActiveDeviceCount = 0;
                MaxLatencyDiff = 0;
                AudioLevel = 0;
                ErrorMessage = string.Empty;

                // Save current profiles (volume + delay)
                SaveProfiles();

                // Reset waveform
                WaveformData = new double[200];
                _waveformWritePos = 0;

                Logger.Info("Sync stopped.");
            }
            catch (Exception ex)
            {
                Logger.Error("Error during stop", ex);
                IsSyncing = false;
                PlaybackStatus = "Error";
            }
        }

        /// <summary>
        /// Called on each capture data event — distributes audio + updates waveform.
        /// </summary>
        private void OnCaptureDataAvailable(byte[] buffer, int byteCount)
        {
            // Distribute to output devices (runs on capture thread)
            _outputService.DistributeAudio(buffer, byteCount);

            // Update waveform + audio level
            UpdateWaveform(buffer, byteCount);
        }

        /// <summary>
        /// Downsamples captured PCM data for waveform visualization and audio level.
        /// </summary>
        private void UpdateWaveform(byte[] buffer, int byteCount)
        {
            try
            {
                // IEEE float 32-bit from WASAPI loopback
                int sampleCount = byteCount / 4;
                if (sampleCount == 0) return;

                int step = Math.Max(1, sampleCount / 10);
                var waveform = _waveformData;
                float maxSample = 0;

                for (int i = 0; i < sampleCount && i < step * 10; i += step)
                {
                    int bytePos = i * 4;
                    if (bytePos + 4 > byteCount) break;

                    float sample = BitConverter.ToSingle(buffer, bytePos);
                    float absSample = Math.Abs(sample);
                    if (absSample > maxSample) maxSample = absSample;

                    waveform[_waveformWritePos] = Math.Clamp(sample, -1.0, 1.0);
                    _waveformWritePos = (_waveformWritePos + 1) % waveform.Length;
                }

                // Update audio level (peak)
                var level = Math.Clamp(maxSample, 0, 1);

                // Trigger UI update on dispatcher (throttled — UI timer handles rendering)
                Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    AudioLevel = level;
                    OnPropertyChanged(nameof(WaveformData));
                });
            }
            catch
            {
                // Non-critical — skip waveform update on errors
            }
        }

        /// <summary>
        /// Timer-driven monitoring: updates buffer health, active count, latency diff.
        /// </summary>
        private void UpdateMonitoring()
        {
            if (!IsSyncing) return;

            int activeCount = 0;
            double maxDiff = 0;

            foreach (var device in Devices.Where(d => d.IsSelected))
            {
                device.BufferHealth = _outputService.GetBufferHealth(device.DeviceId);

                bool isActive = _outputService.IsDeviceActive(device.DeviceId);
                if (isActive)
                {
                    activeCount++;
                    device.IsActive = true;
                    if (!device.HasError) device.StatusText = "Streaming";
                }

                var diff = _latencyManager.GetLatencyDiff(device.DeviceId);
                if (diff > maxDiff)
                    maxDiff = diff;
            }

            ActiveDeviceCount = activeCount;
            MaxLatencyDiff = Math.Round(maxDiff, 1);
        }

        /// <summary>
        /// Handles per-device property changes (delay slider, checkbox).
        /// </summary>
        private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is AudioDeviceModel device)
            {
                if (e.PropertyName == nameof(AudioDeviceModel.ManualDelay) && IsSyncing)
                {
                    _latencyManager.SetManualDelay(device.DeviceId, device.ManualDelay + MasterDelayOffset);
                }

                if (e.PropertyName == nameof(AudioDeviceModel.Volume) && IsSyncing)
                {
                    _outputService.SetDeviceVolume(device.DeviceId, (float)(device.Volume / 100.0));
                }

                if (e.PropertyName == nameof(AudioDeviceModel.IsSelected))
                {
                    var anySelected = Devices.Any(d => d.IsSelected);
                    Logger.Info($"Device '{device.FriendlyName}' IsSelected={device.IsSelected}. Any selected: {anySelected}");
                    RelayCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// Handles device-level errors (disconnection).
        /// </summary>
        private void OnDeviceError(string deviceId, string message)
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                var device = Devices.FirstOrDefault(d => d.DeviceId == deviceId);
                if (device != null)
                {
                    device.HasError = true;
                    device.IsActive = false;
                    device.StatusText = $"Disconnected";
                    device.BufferHealth = 0;
                }

                ErrorMessage = $"⚠ Device disconnected: {device?.FriendlyName ?? deviceId}";

                // If all selected devices errored, update status
                if (Devices.Where(d => d.IsSelected).All(d => d.HasError))
                {
                    PlaybackStatus = "Error";
                    ErrorMessage = "⚠ All devices disconnected. Click Stop and try again.";
                }
            });
        }

        /// <summary>
        /// Handles device reconnection — re-adds device to output.
        /// </summary>
        private void OnDeviceReconnected(string deviceId)
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                var device = Devices.FirstOrDefault(d => d.DeviceId == deviceId);
                if (device != null && IsSyncing && device.IsSelected)
                {
                    device.HasError = false;
                    device.StatusText = "Reconnecting...";
                    ErrorMessage = $"Reconnecting: {device.FriendlyName}...";

                    _outputService.AddDevice(device);
                    Logger.Info($"Device reconnected and re-added: {device.FriendlyName}");
                }
            });
        }

        /// <summary>
        /// Saves current device profiles (volume + delay).
        /// </summary>
        private void SaveProfiles()
        {
            try
            {
                var profiles = Devices.Where(d => d.IsSelected)
                    .ToDictionary(d => d.DeviceId, d => new DeviceProfile
                    {
                        Delay = d.ManualDelay,
                        Volume = d.Volume
                    });
                DeviceProfileManager.Save(profiles);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save profiles", ex);
            }
        }

        private void UpdateStatusBrush()
        {
            StatusBrush = PlaybackStatus switch
            {
                "Syncing" => new SolidColorBrush(Color.FromRgb(0x5A, 0x9A, 0x5A)),
                "Error" => new SolidColorBrush(Color.FromRgb(0xB0, 0x50, 0x50)),
                "Starting..." => new SolidColorBrush(Color.FromRgb(0xA0, 0x80, 0x40)),
                _ => new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70))
            };
        }

        // ── INotifyPropertyChanged ────────────────────────────────
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public void Dispose()
        {
            _deviceRefreshTimer.Stop();
            _monitorTimer.Stop();
            _captureService.Dispose();
            _outputService.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
