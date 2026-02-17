using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SyncWave.Utils
{
    /// <summary>
    /// Persists and restores user device selection profiles as JSON.
    /// Stored in %LocalAppData%/SyncWave/profiles.json.
    /// </summary>
    public static class DeviceProfileManager
    {
        private static readonly string _profilePath;

        static DeviceProfileManager()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SyncWave");
            Directory.CreateDirectory(dir);
            _profilePath = Path.Combine(dir, "profiles.json");
        }

        /// <summary>
        /// Save a set of device profiles (device ID → manual delay).
        /// </summary>
        public static void Save(Dictionary<string, double> deviceDelays)
        {
            try
            {
                var json = JsonSerializer.Serialize(deviceDelays, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_profilePath, json);
                Logger.Info($"Saved device profile with {deviceDelays.Count} devices.");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save device profile", ex);
            }
        }

        /// <summary>
        /// Load previously saved device profiles.
        /// Returns empty dictionary if no profile exists.
        /// </summary>
        public static Dictionary<string, double> Load()
        {
            try
            {
                if (!File.Exists(_profilePath))
                    return new Dictionary<string, double>();

                var json = File.ReadAllText(_profilePath);
                var result = JsonSerializer.Deserialize<Dictionary<string, double>>(json);
                Logger.Info($"Loaded device profile with {result?.Count ?? 0} devices.");
                return result ?? new Dictionary<string, double>();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load device profile", ex);
                return new Dictionary<string, double>();
            }
        }
    }
}
