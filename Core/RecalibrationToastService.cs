using System;
using System.Collections.Concurrent;
using SyncWave.Models;
using SyncWave.Utils;

namespace SyncWave.Core
{
    /// <summary>
    /// Monitors Bluetooth device reconnect events and shows a one-time,
    /// dismissible toast suggesting recalibration when a previously-calibrated
    /// device reconnects. Suppresses repeat toasts per device within a session.
    ///
    /// Design: no modal, no blocking, no repeated nagging — consistent with
    /// SyncWave's low-friction, "don't disturb" philosophy.
    /// </summary>
    public class RecalibrationToastService
    {
        // Track devices that have already been toasted this session
        private readonly ConcurrentDictionary<string, DateTime> _dismissedDevices = new();

        // Minimum time between toasts for the same device (prevents rapid-reconnect spam)
        private static readonly TimeSpan ToastCooldown = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Raised when a recalibration toast should be shown.
        /// Parameters: deviceId, deviceName, message.
        /// </summary>
        public event Action<string, string, string>? ToastRequested;

        /// <summary>
        /// Called when a BT device reconnects. Evaluates whether to show
        /// a recalibration toast based on calibration state and dismiss history.
        /// </summary>
        /// <param name="device">The reconnected device model.</param>
        public void OnDeviceReconnected(AudioDeviceModel device)
        {
            if (device == null) return;

            // Only toast for Bluetooth devices
            if (!device.DeviceType.Equals("Bluetooth", StringComparison.OrdinalIgnoreCase))
                return;

            // Only toast for devices that have been calibrated before
            if (device.CalibrationStatus == CalibrationStatus.NotCalibrated)
                return;

            // Check dismiss history — suppress within cooldown window
            if (_dismissedDevices.TryGetValue(device.DeviceId, out var lastDismissed))
            {
                if (DateTime.UtcNow - lastDismissed < ToastCooldown)
                {
                    Logger.Info($"RecalibrationToast: suppressed for {device.FriendlyName} (cooldown)");
                    return;
                }
            }

            // Record that we're toasting this device
            _dismissedDevices[device.DeviceId] = DateTime.UtcNow;

            string message = $"{device.FriendlyName} reconnected — delay may have shifted.";
            Logger.Info($"RecalibrationToast: showing for {device.FriendlyName}");

            ToastRequested?.Invoke(device.DeviceId, device.FriendlyName, message);
        }

        /// <summary>
        /// Called when the anchor device changes. Extends the existing
        /// reconnect-toast mechanism to also surface anchor changes.
        /// </summary>
        public void OnAnchorChanged(string newAnchorName)
        {
            string message = $"Anchor switched to {newAnchorName}";
            Logger.Info($"AnchorChange toast: {message}");
            ToastRequested?.Invoke("anchor-change", newAnchorName, message);
        }

        /// <summary>
        /// Dismisses the toast for a specific device, resetting the cooldown timer.
        /// </summary>
        public void Dismiss(string deviceId)
        {
            _dismissedDevices[deviceId] = DateTime.UtcNow;
        }

        /// <summary>
        /// Clears all dismiss history (e.g. on sync stop).
        /// </summary>
        public void ClearSession()
        {
            _dismissedDevices.Clear();
        }
    }
}
