using System;
using NAudio.CoreAudioApi;
using SyncWave.Utils;

namespace SyncWave.Utils
{
    /// <summary>
    /// Utility methods for managing Windows audio endpoint state.
    /// Used to ensure devices are audible before SyncWave's pipeline starts,
    /// without persistently overriding user intent.
    /// </summary>
    public static class AudioEndpointUtils
    {
        /// <summary>
        /// Ensures a device's OS-level endpoint is not muted and not at 0 volume,
        /// so the app's internal VolumeWaveProvider gain is actually audible.
        /// Runs once per pipeline (re)build — does not persistently override
        /// user changes made afterward via Windows.
        /// </summary>
        /// <param name="device">The MMDevice to normalize. Safe to call with null.</param>
        /// <returns>
        /// True if any correction was applied (unmuted or volume raised);
        /// false if the endpoint was already in an audible state or the check was skipped.
        /// </returns>
        public static bool EnsureEndpointIsAudible(MMDevice? device)
        {
            if (device?.AudioEndpointVolume == null)
                return false;

            bool corrected = false;

            try
            {
                var epVolume = device.AudioEndpointVolume;

                // Unmute if muted
                if (epVolume.Mute)
                {
                    epVolume.Mute = false;
                    corrected = true;
                    Logger.Info($"Unmuted endpoint for {device.FriendlyName}");
                }

                // Raise volume to 100% if it's at/near 0 (< 1%)
                // A near-zero endpoint silently blocks all app-level audio output
                if (epVolume.MasterVolumeLevelScalar < 0.01f)
                {
                    float previousLevel = epVolume.MasterVolumeLevelScalar;
                    epVolume.MasterVolumeLevelScalar = 1.0f;
                    corrected = true;
                    Logger.Info($"Raised endpoint volume from {previousLevel * 100:F1}% to 100% for {device.FriendlyName}");
                }
            }
            catch (Exception ex)
            {
                // Don't let a failed volume-normalization attempt crash pipeline init.
                // Worst case: the device stays silent, same as today without the fix.
                Logger.Warn($"Could not normalize endpoint volume for {device.FriendlyName}: {ex.Message}");
            }

            return corrected;
        }
    }
}
