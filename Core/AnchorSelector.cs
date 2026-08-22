using System;
using System.Collections.Generic;
using System.Linq;
using SyncWave.Models;

namespace SyncWave.Core
{
    public class AnchorSelector
    {
        /// <summary>Hysteresis margin: a new candidate must exceed the current
        /// anchor by this many ms to trigger a switch, preventing anchor
        /// thrashing from measurement noise or near-equal estimates.
        /// TODO: revisit this value after real hardware testing.</summary>
        public const double HysteresisMarginMs = 10.0;

        public AnchorChangeResult Recompute(List<AnchorDeviceProfile> devices)
        {
            if (devices.Count == 0)
                return AnchorChangeResult.Empty;

            // Pinned device wins if it exists in the *current* device list.
            var pinned = devices.FirstOrDefault(d => d.IsPinnedByUser);
            AnchorDeviceProfile anchor;

            if (pinned != null)
            {
                anchor = pinned;
            }
            else
            {
                var currentAnchor = devices.FirstOrDefault(d => d.IsAnchor);
                var candidate = devices.OrderByDescending(d => d.EffectiveLatencyMs)
                                       .ThenBy(d => d.Id) // deterministic tiebreaker
                                       .First();

                // Hysteresis: only switch if candidate exceeds current by margin
                if (currentAnchor != null
                    && candidate.Id != currentAnchor.Id
                    && candidate.EffectiveLatencyMs - currentAnchor.EffectiveLatencyMs <= HysteresisMarginMs)
                {
                    anchor = currentAnchor; // keep current, not enough to justify switch
                }
                else
                {
                    anchor = candidate;
                }
            }

            bool changed = devices.Any(d => d.IsAnchor != (d == anchor));

            foreach (var d in devices)
            {
                d.IsAnchor = (d == anchor);
                d.HeldBackDelayMs = Math.Max(0, anchor.EffectiveLatencyMs - d.EffectiveLatencyMs);
            }

            return new AnchorChangeResult
            {
                Anchor = anchor,
                AnchorChanged = changed,
                RequiresRecalibration = changed
            };
        }
    }
}
