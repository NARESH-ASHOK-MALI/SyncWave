namespace SyncWave.Models
{
    /// <summary>
    /// Device transport classification, ordered roughly by typical latency.
    /// Used by AnchorSelector's heuristic — not a codec-level classification.
    ///
    /// KNOWN LIMITATION: Windows does not reliably expose the negotiated
    /// Bluetooth codec (SBC, AAC, aptX, etc.) at the OS level. The BT variants
    /// here are coarse guesses based on enumerator strings and device names,
    /// NOT measured codec identification. Do not treat the estimate as exact.
    /// </summary>
    public enum TransportType
    {
        WiredOnboard,           // HDAUDIO enumerator — ~5-10ms
        Usb,                    // USB enumerator — ~20ms
        Hdmi,                   // HDMI/DisplayPort — ~20ms
        BluetoothAptxLL,        // aptX LL hint — ~35ms (rare to detect)
        BluetoothAptx,          // aptX hint — ~60ms
        BluetoothAac,           // AAC hint — ~160ms
        BluetoothSbc,           // SBC / older — ~200ms
        BluetoothUnknownCodec,  // BT detected, codec unknown — ~170ms (conservative)
        Unknown                 // Fallback — treated as wired (~10ms)
    }
}
