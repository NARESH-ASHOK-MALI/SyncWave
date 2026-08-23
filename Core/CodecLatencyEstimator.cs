using System;
using System.Collections.Generic;

namespace SyncWave.Core
{
    /// <summary>
    /// Provides codec-based latency starting-point estimates for Bluetooth devices.
    /// Uses published typical latency ranges per BT codec to pre-fill the calibration
    /// slider near the right value. Clearly labeled as an estimate — the user
    /// fine-tunes from here using perceptual calibration.
    ///
    /// Latency values are midpoints of published typical ranges:
    ///   SBC:     150–250ms  → midpoint ~200ms
    ///   AAC:     120–200ms  → midpoint ~160ms
    ///   aptX:    40–80ms    → midpoint ~60ms
    ///   aptX LL: 20–50ms    → midpoint ~35ms
    ///   aptX HD: 80–150ms   → midpoint ~120ms
    ///   LDAC:    100–200ms  → midpoint ~150ms
    ///   Wired/USB:           → ~5ms (essentially zero)
    ///
    /// For unknown codecs, falls back to a conservative 150ms default.
    /// </summary>
    public static class CodecLatencyEstimator
    {
        /// <summary>Result of a codec estimation.</summary>
        public class EstimationResult
        {
            /// <summary>Estimated delay in milliseconds.</summary>
            public double EstimatedDelayMs { get; init; }

            /// <summary>Name of the detected codec, or "Unknown" if not identified.</summary>
            public string CodecName { get; init; } = "Unknown";

            /// <summary>Whether this is a measured/known codec or a fallback default.</summary>
            public bool IsKnownCodec { get; init; }

            /// <summary>Human-readable description for UI display.</summary>
            public string Description => IsKnownCodec
                ? $"~{EstimatedDelayMs:F0}ms ({CodecName} estimate)"
                : $"~{EstimatedDelayMs:F0}ms (default estimate)";
        }

        // Static lookup table: codec identifier → (midpoint delay, display name)
        private static readonly Dictionary<string, (double DelayMs, string Name)> _codecTable = new(StringComparer.OrdinalIgnoreCase)
        {
            // Standard Bluetooth codecs
            { "sbc",        (200, "SBC") },
            { "aac",        (160, "AAC") },
            { "aptx",       (60,  "aptX") },
            { "apt-x",      (60,  "aptX") },
            { "aptxll",     (35,  "aptX LL") },
            { "aptx-ll",    (35,  "aptX LL") },
            { "aptx ll",    (35,  "aptX LL") },
            { "aptxhd",     (120, "aptX HD") },
            { "aptx-hd",    (120, "aptX HD") },
            { "aptx hd",    (120, "aptX HD") },
            { "ldac",       (150, "LDAC") },
            { "lc3",        (30,  "LC3") },
            { "lc3plus",    (25,  "LC3plus") },

            // Wired/USB (near-zero baseline)
            { "wired",      (5,   "Wired") },
            { "usb",        (8,   "USB") },
            { "hdmi",       (20,  "HDMI") },
        };

        /// <summary>Default fallback delay for unrecognized Bluetooth codecs.</summary>
        private const double DefaultBtDelayMs = 150;

        /// <summary>Default delay for wired connections.</summary>
        private const double DefaultWiredDelayMs = 5;

        /// <summary>
        /// Estimates the latency for a device based on its transport type and
        /// optional codec information.
        /// </summary>
        /// <param name="deviceType">Device transport type ("Bluetooth", "USB", "Wired", "HDMI").</param>
        /// <param name="codecHint">Optional codec identifier string (e.g. "SBC", "aptX").</param>
        /// <returns>An estimation result with the delay and codec info.</returns>
        public static EstimationResult Estimate(string deviceType, string? codecHint = null)
        {
            // If we have an explicit codec hint, try the lookup table first
            if (!string.IsNullOrWhiteSpace(codecHint))
            {
                var normalized = codecHint.Trim();
                if (_codecTable.TryGetValue(normalized, out var codecInfo))
                {
                    return new EstimationResult
                    {
                        EstimatedDelayMs = codecInfo.DelayMs,
                        CodecName = codecInfo.Name,
                        IsKnownCodec = true
                    };
                }

                // Try partial match (e.g. "Qualcomm aptX" contains "aptx")
                foreach (var kvp in _codecTable)
                {
                    if (normalized.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        return new EstimationResult
                        {
                            EstimatedDelayMs = kvp.Value.DelayMs,
                            CodecName = kvp.Value.Name,
                            IsKnownCodec = true
                        };
                    }
                }
            }

            // Fall back to transport-type-based estimation
            var type = (deviceType ?? "").Trim().ToLowerInvariant();

            return type switch
            {
                "bluetooth" => new EstimationResult
                {
                    EstimatedDelayMs = DefaultBtDelayMs,
                    CodecName = "Bluetooth",
                    IsKnownCodec = false
                },
                "usb" => new EstimationResult
                {
                    EstimatedDelayMs = 8,
                    CodecName = "USB",
                    IsKnownCodec = true
                },
                "hdmi" => new EstimationResult
                {
                    EstimatedDelayMs = 20,
                    CodecName = "HDMI",
                    IsKnownCodec = true
                },
                _ => new EstimationResult
                {
                    EstimatedDelayMs = DefaultWiredDelayMs,
                    CodecName = "Wired",
                    IsKnownCodec = true
                }
            };
        }

        public static EstimationResult Estimate(SyncWave.Models.TransportType transport)
        {
            var deviceTypeString = transport switch
            {
                SyncWave.Models.TransportType.WiredOnboard => "Wired",
                SyncWave.Models.TransportType.Usb => "USB",
                SyncWave.Models.TransportType.Hdmi => "HDMI",
                _ when transport.ToString().StartsWith("Bluetooth") => "Bluetooth",
                _ => "Wired"
            };
            return Estimate(deviceTypeString);
        }
    }
}
