using SyncWave.Models;

namespace SyncWave.Core
{
    public class AnchorChangeResult
    {
        public AnchorDeviceProfile? Anchor { get; init; }
        public bool AnchorChanged { get; init; }
        public bool RequiresRecalibration { get; init; }

        public static AnchorChangeResult Empty => new()
        {
            Anchor = null,
            AnchorChanged = false,
            RequiresRecalibration = false
        };
    }
}
