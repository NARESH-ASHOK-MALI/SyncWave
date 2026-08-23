using SyncWave.Models;

namespace SyncWave.Utils
{
    public static class TransportDetector
    {
        public static TransportType ToTransportType(string deviceType) => deviceType switch
        {
            "Bluetooth" => TransportType.BluetoothUnknownCodec,
            "USB" => TransportType.Usb,
            "HDMI" => TransportType.Hdmi,
            "Wired" => TransportType.WiredOnboard,
            _ => TransportType.Unknown
        };
    }
}
