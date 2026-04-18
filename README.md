<p align="center">
  <img src="Assets/logo.png" alt="SyncWave Logo" width="120" />
</p>

<h1 align="center">SyncWave</h1>

<p align="center">
  <strong>One Sound. Many Devices.</strong><br/>
  Stream system audio to multiple Bluetooth, USB, and wired devices simultaneously — with per-device volume & latency control.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet" alt=".NET 8" />
  <img src="https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D4?logo=windows" alt="Windows" />
  <img src="https://img.shields.io/badge/Audio-WASAPI-FF6B00" alt="WASAPI" />
  <img src="https://img.shields.io/badge/License-MIT-green" alt="MIT License" />
</p>

---

## 📥 Download

<p align="center">
  <a href="https://github.com/NARESH-ASHOK-MALI/SyncWave/releases/latest">
    <img src="https://img.shields.io/badge/⬇_Download-SyncWave.exe-28A745?style=for-the-badge&logo=windows" alt="Download SyncWave" />
  </a>
</p>

> **Quick Start:** Download the latest `SyncWave.exe` from the [Releases](https://github.com/NARESH-ASHOK-MALI/SyncWave/releases/latest) page — no installation required, just run it.
>
> - **Self-contained build** (~65 MB) — runs on any Windows 10/11 machine, no .NET runtime needed.

---

## ✨ Features

| Feature | Description |
|---|---|
| 🔊 **Multi-Device Output** | Stream to unlimited audio devices in parallel |
| 🎯 **Latency Compensation** | Auto-syncs faster devices to the slowest; per-device manual delay (0–500ms) |
| 🔈 **Real-Time Volume** | Per-device volume (0–200%) with instant adjustment — no buffer delay |
| 📊 **Live Monitoring** | Waveform visualization, buffer health bars, latency readouts |
| 🔄 **Auto-Reconnect** | Graceful Bluetooth disconnect handling with reconnection polling |
| 🚫 **Echo Prevention** | Auto-detects source device and skips it to prevent audio doubling |
| 💾 **Profile Persistence** | Auto-saves device selection, volume, and delay settings |
| 🎨 **Dark Theme UI** | Modern WPF interface with cyan-purple gradient accents |

## 🏗 Tech Stack

| Component | Technology |
|---|---|
| Runtime | .NET 8 (C#) |
| UI Framework | WPF (XAML) |
| Audio Library | NAudio 2.2.1 |
| Audio API | Windows Core Audio (WASAPI) |
| Architecture | MVVM |

## 📋 Prerequisites

- **Windows 10/11** (64-bit)
- **.NET 8 SDK** — [Download](https://dotnet.microsoft.com/download/dotnet/8.0)

## 🚀 Build & Run

```powershell
# Clone the repo
git clone https://github.com/NARESH-ASHOK-MALI/SyncWave.git
cd SyncWave

# Build and run
dotnet build
dotnet run
```

### Create Standalone EXE

```powershell
# Framework-dependent (~1 MB, requires .NET 8 runtime)
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

# Self-contained (~65 MB, runs anywhere)
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Output: `bin/Release/net8.0-windows/win-x64/publish/SyncWave.exe`

## 📁 Project Structure

```
SyncWave/
├── Assets/
│   └── logo.png                  # App icon
├── Core/
│   ├── AudioCaptureService.cs    # WASAPI loopback capture engine
│   ├── AudioOutputService.cs     # Multi-device output manager
│   ├── LatencyManager.cs         # Circular delay compensation buffers
│   └── VolumeWaveProvider.cs     # Real-time volume scaling (read-time)
├── ViewModels/
│   └── MainViewModel.cs          # MVVM ViewModel — pipeline orchestration
├── Views/
│   ├── MainWindow.xaml           # Dark-themed UI layout
│   └── MainWindow.xaml.cs        # Waveform canvas rendering
├── Models/
│   └── AudioDeviceModel.cs       # Observable device model
├── Utils/
│   ├── Logger.cs                 # Thread-safe file + debug logger
│   ├── RelayCommand.cs           # ICommand implementation
│   ├── Converters.cs             # WPF value converters
│   └── DeviceProfileManager.cs   # JSON profile persistence
├── App.xaml / App.xaml.cs        # App entry + global resource styles
├── app.manifest                  # Runs without admin privileges
├── SyncWave.csproj               # Project definition
└── LICENSE                       # MIT License
```

## 🔧 How It Works

### Audio Pipeline
```
System Audio → WASAPI Loopback → Capture Buffer → Latency Delay → Volume Scale → WasapiOut → Device
                                                     ↕ per-device      ↕ real-time
                                                   (0–500ms)        (0–200%)
```

### Latency Compensation Algorithm
1. On sync start, each device's hardware latency is measured via `AudioClient.StreamLatency`
2. The **maximum latency** across all devices is determined
3. For each device: `compensationDelay = maxLatency − deviceLatency + manualOffset`
4. High-performance circular delay buffers (using `Buffer.BlockCopy`) insert the required delay
5. Manual sliders adjust the offset in real-time; the buffer resizes dynamically
6. The master delay slider applies a global offset across all devices

### Echo Prevention
The default render device (where system audio natively plays) is auto-detected and labeled **"🔈 Source"**. When syncing, this device is automatically skipped — preventing the feedback loop that causes echo/doubling.

## 🎮 Usage

1. **Launch** SyncWave
2. **Select** output devices using the checkboxes (the Source device is auto-skipped)
3. **Adjust** per-device volume (0–200%) to match loudness between devices
4. **Set** per-device delay if needed for lip-sync or spatial alignment
5. Click **▶ Start Sync** — system audio streams to all selected devices
6. The waveform shows the live audio signal; buffer health bars show stream stability
7. Click **⏹ Stop Sync** to end playback (settings auto-saved)

## ⚡ Performance

- **CPU**: Typically under 10%
- **Latency**: ~100ms pipeline delay (WasapiOut timer mode)
- **Buffer**: 5-second per-device buffer prevents dropout
- **GC-friendly**: Bulk `Buffer.BlockCopy` transfers, reusable output buffers
- **Thread-safe**: Concurrent buffer writes with lock-free volume control

## 📝 License

[MIT](LICENSE)

---

<p align="center">
  Made with ❤️ for multi-device audio enthusiasts
</p>
