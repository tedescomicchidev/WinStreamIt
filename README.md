# SonosStream

Stream all Windows system audio to Sonos speakers in real-time over your local network.

SonosStream captures the Windows audio mix (everything you hear — browser tabs, apps, games, calls) via WASAPI loopback and serves it as a continuous HTTP WAV stream. Sonos speakers connect to that stream using the standard UPnP AVTransport protocol, with no Sonos account, no cloud dependency, and no third-party drivers required.

[![Build](../../actions/workflows/build.yml/badge.svg)](../../actions/workflows/build.yml)

## Features

- **Zero-config discovery** — SSDP scan finds every Sonos speaker on your LAN automatically
- **Multi-speaker streaming** — play to individual speakers or group them all in one click
- **Per-device volume control** — debounced slider sends SOAP RenderingControl commands in real-time
- **Auto-reconnect** — health-check timer re-initiates streaming if a speaker unexpectedly stops
- **Silence injection** — keeps the HTTP connection alive when no audio is playing, preventing Sonos from dropping the stream
- **Settings persistence** — last-used devices are remembered and reconnected on next launch
- **No external UPnP library** — raw SSDP and SOAP over UDP/HTTP, no heavyweight dependency

## Requirements

| Requirement | Details |
|---|---|
| OS | Windows 10 (1809+) or Windows 11 |
| Runtime | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Windows App SDK | 1.5+ (bundled as self-contained in published builds) |
| Speakers | Any Sonos speaker that supports UPnP AVTransport (Sonos One Gen 1/2 tested) |
| Network | PC and speakers must be on the same LAN subnet |

### Build requirements

| Tool | Version |
|---|---|
| Visual Studio 2022 | 17.8+ with **Windows application development** workload |
| .NET 8 SDK | 8.0+ |
| Windows App SDK | Installed via VS workload |

## Getting started

### 1. Clone and build

```bash
git clone https://github.com/tedescomicchidev/WinStreamIt.git
cd WinStreamIt
dotnet build SonosStream.sln -c Release -p:Platform=x64
```

### 2. Firewall rule

The embedded HTTP server listens on **TCP port 5901**. Add an inbound rule so Sonos speakers can connect:

```powershell
# Run once as Administrator
netsh advfirewall firewall add rule `
  name="SonosStream" dir=in action=allow protocol=TCP localport=5901
```

Or let the app attempt to add it automatically on first launch (requires admin rights).

### 3. Run

```bash
dotnet run --project SonosStream/SonosStream.csproj
```

Or press **F5** in Visual Studio (select the **x64** platform target).

### 4. Stream

1. The app scans for speakers on startup — discovered speakers appear in the **Speakers** page.
2. Click **Play** next to a speaker to start streaming.
3. Click **Stream to All** to send audio to every discovered speaker simultaneously.
4. Adjust volume with the per-device slider.
5. Use the **Settings** page to change port, buffer size, or auto-reconnect behaviour.

## Architecture

```
[Windows Audio Engine]
        │  WASAPI loopback
        ▼
[WasapiLoopbackCapture]  ──resampled to──▶  [CircularAudioBuffer]
 32-bit float / 48 kHz    16-bit / 44100 Hz   (lock-free ring buffer)
                                                       │
                                                       ▼
                                       [Kestrel HTTP :5901]
                                        GET /stream/swyh.wav
                                         WAV header (44 bytes)
                                         + infinite PCM stream
                                                │
                                   ┌────────────┴────────────┐
                                   ▼                         ▼
                            [Sonos One #1]           [Sonos One #2]
                            (HTTP GET pull)          (grouped via x-rincon)
```

### Threading model

| Thread | Responsibility |
|---|---|
| Audio capture (NAudio) | Writes converted PCM into `CircularAudioBuffer` |
| Kestrel thread pool | One streaming task per connected Sonos speaker reads from the buffer |
| SSDP background task | UDP multicast scan, periodic re-scan every 30 s |
| WinUI 3 dispatcher | UI updates, health-check timer, ViewModel bindings |

## Project structure

```
SonosStream/
├── SonosStream.sln
└── SonosStream/
    ├── Program.cs                      # Unpackaged WinUI 3 bootstrap
    ├── App.xaml / App.xaml.cs          # DI container setup
    ├── MainWindow.xaml / .cs           # NavigationView shell
    │
    ├── Models/
    │   ├── SonosDevice.cs              # Discovered speaker (ObservableObject)
    │   ├── StreamingSession.cs         # Active session state
    │   └── AppSettings.cs             # Persisted configuration
    │
    ├── Services/
    │   ├── AudioCaptureService.cs      # WASAPI loopback + resampling
    │   ├── AudioStreamServer.cs        # Kestrel HTTP streaming endpoint
    │   ├── SonosDiscoveryService.cs    # SSDP M-SEARCH + XML parse
    │   ├── SonosControlService.cs      # SOAP AVTransport / RenderingControl
    │   └── SettingsService.cs          # JSON settings in LocalApplicationData
    │
    ├── ViewModels/
    │   ├── MainViewModel.cs            # Discovery, streaming, health-check
    │   └── DeviceViewModel.cs          # Per-speaker state and commands
    │
    ├── Views/
    │   ├── DeviceListPage.xaml / .cs   # Speaker list with play/volume controls
    │   └── SettingsPage.xaml / .cs     # Configuration UI
    │
    └── Helpers/
        ├── CircularAudioBuffer.cs      # Lock-free ring buffer (single-write, multi-read)
        ├── WavHeaderWriter.cs          # 44-byte RIFF header (0x7FFFFFFF = infinite)
        ├── NetworkHelper.cs            # LAN IP detection
        └── BoolNegationConverter.cs    # XAML value converter
```

## Key implementation notes

### Audio pipeline

NAudio captures in the device's native format (typically 32-bit float / 48 kHz stereo). A `MediaFoundationResampler` converts it to **16-bit signed PCM / 44100 Hz / stereo** — the format Sonos expects for LPCM streaming.

### WAV streaming quirks

Sonos determines the audio format from the **URL file extension**, not the `Content-Type` header. The stream endpoint is always `/stream/swyh.wav`. The WAV header uses `0x7FFFFFFF` for both the RIFF chunk size and the data chunk size, signalling an infinite stream.

### Silence injection

When nothing is playing on the PC, WASAPI may stop firing `DataAvailable` events. Without data, Sonos will time out and drop the connection. The capture service injects a 100 ms block of silence every 80 ms whenever no real audio has arrived for more than 150 ms.

### Grouping speakers

Sonos groups are formed by sending the coordinator's device ID as an `x-rincon:RINCON_...` URI to each slave via `SetAVTransportURI`. Only the coordinator receives the actual HTTP stream URL.

### Expected latency

UPnP/DLNA streaming to Sonos has an inherent **1–3 second delay** due to the speaker's internal jitter buffer. This is firmware behaviour and cannot be eliminated. LPCM gives the lowest latency; compressed formats add more.

## Settings

Stored at `%LocalAppData%\SonosStream\settings.json`:

| Key | Default | Description |
|---|---|---|
| `serverPort` | `5901` | TCP port for the Kestrel HTTP server |
| `bufferSizeSeconds` | `5` | Ring-buffer capacity in seconds of audio |
| `autoReconnect` | `true` | Re-initiate streaming if a speaker stops unexpectedly |
| `autoReconnectIntervalSeconds` | `10` | Health-check polling interval |
| `silenceInjection` | `true` | Inject silence to keep the connection alive |
| `lastUsedDevices` | `[]` | Device IDs reconnected automatically on next launch |
| `audioDeviceId` | `null` | Capture device override (null = system default) |

## CI

GitHub Actions builds the solution on every push and pull request targeting `main`/`master`. Release builds are published as self-contained single-file executables and uploaded as workflow artifacts.

The workflow (`build.yml`) runs on `windows-latest` and exercises both Debug and Release configurations for the x64 platform. Release artifacts are retained for 30 days.

## License

MIT
