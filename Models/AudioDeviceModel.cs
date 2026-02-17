using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SyncWave.Models
{
    /// <summary>
    /// Represents an audio output device with its selection state,
    /// latency metrics, and synchronization parameters.
    /// </summary>
    public class AudioDeviceModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        private double _manualDelay;
        private double _measuredLatency;
        private double _bufferHealth;
        private bool _isActive;
        private string _statusText = "Ready";
        private bool _hasError;
        private double _volume = 100;
        private bool _isDefaultDevice;

        /// <summary>Unique device identifier from Windows audio subsystem.</summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>Human-readable device name shown in the UI.</summary>
        public string FriendlyName { get; set; } = string.Empty;

        /// <summary>Device connection type (Bluetooth, USB, Wired, HDMI).</summary>
        public string DeviceType { get; set; } = "Unknown";

        /// <summary>Whether the user has selected this device for sync output.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        /// <summary>User-adjustable delay offset in milliseconds (0–500).</summary>
        public double ManualDelay
        {
            get => _manualDelay;
            set { _manualDelay = Math.Clamp(value, 0, 500); OnPropertyChanged(); }
        }

        /// <summary>Measured output latency in milliseconds.</summary>
        public double MeasuredLatency
        {
            get => _measuredLatency;
            set { _measuredLatency = value; OnPropertyChanged(); }
        }

        /// <summary>Buffer health percentage (0–100). Below 20 indicates risk of underrun.</summary>
        public double BufferHealth
        {
            get => _bufferHealth;
            set { _bufferHealth = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
        }

        /// <summary>Whether the device is currently actively streaming.</summary>
        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; OnPropertyChanged(); }
        }

        /// <summary>Current status text for the device.</summary>
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        /// <summary>Whether the device is in an error state (e.g. disconnected).</summary>
        public bool HasError
        {
            get => _hasError;
            set { _hasError = value; OnPropertyChanged(); }
        }

        /// <summary>Per-device volume level (0–200%). Over 100% boosts quieter devices.</summary>
        public double Volume
        {
            get => _volume;
            set { _volume = Math.Clamp(value, 0, 200); OnPropertyChanged(); }
        }

        /// <summary>Whether this is the default render device (loopback source). Audio already plays here natively.</summary>
        public bool IsDefaultDevice
        {
            get => _isDefaultDevice;
            set { _isDefaultDevice = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
