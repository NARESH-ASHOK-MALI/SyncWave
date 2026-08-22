namespace SyncWave.Models
{
    public class AnchorDeviceProfile
    {
        public string Id { get; set; } = string.Empty;
        public string FriendlyName { get; set; } = string.Empty;
        public TransportType Transport { get; set; }
        public double EstimatedLatencyMs { get; set; }
        public double? MeasuredLatencyMs { get; set; }
        public double EffectiveLatencyMs => MeasuredLatencyMs ?? EstimatedLatencyMs;
        public bool IsAnchor { get; set; }
        public bool IsPinnedByUser { get; set; }
        public double HeldBackDelayMs { get; set; }
    }
}
