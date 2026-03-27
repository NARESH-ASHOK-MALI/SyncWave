using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SyncWave.Utils
{
    /// <summary>
    /// Per-device profile containing user preferences.
    /// </summary>
    public class DeviceProfile
    {
        [JsonPropertyName("delay")]
        public double Delay { get; set; }

        [JsonPropertyName("volume")]
        public double Volume { get; set; } = 100;
    }

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
        /// Save a set of device profiles (device ID → profile with delay + volume).
        /// </summary>
        public static void Save(Dictionary<string, DeviceProfile> profiles)
        {
            try
            {
                var json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_profilePath, json);
                Logger.Info($"Saved device profile with {profiles.Count} devices.");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save device profile", ex);
            }
        }

        /// <summary>
        /// Load previously saved device profiles.
        /// Returns empty dictionary if no profile exists.
        /// Handles migration from old format (delay-only) gracefully.
        /// </summary>
        public static Dictionary<string, DeviceProfile> Load()
        {
            try
            {
                if (!File.Exists(_profilePath))
                    return new Dictionary<string, DeviceProfile>();

                var json = File.ReadAllText(_profilePath);

                // Try new format first
                try
                {
                    var result = JsonSerializer.Deserialize<Dictionary<string, DeviceProfile>>(json);
                    Logger.Info($"Loaded device profile (v2) with {result?.Count ?? 0} devices.");
                    return result ?? new Dictionary<string, DeviceProfile>();
                }
                catch
                {
                    // Fall back to old format (delay-only) for migration
                    try
                    {
                        var oldResult = JsonSerializer.Deserialize<Dictionary<string, double>>(json);
                        if (oldResult != null)
                        {
                            var migrated = new Dictionary<string, DeviceProfile>();
                            foreach (var kvp in oldResult)
                            {
                                migrated[kvp.Key] = new DeviceProfile { Delay = kvp.Value, Volume = 100 };
                            }
                            Logger.Info($"Migrated old profile format for {migrated.Count} devices.");
                            // Save in new format
                            Save(migrated);
                            return migrated;
                        }
                    }
                    catch { }
                }

                return new Dictionary<string, DeviceProfile>();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load device profile", ex);
                return new Dictionary<string, DeviceProfile>();
            }
        }
    }
}
