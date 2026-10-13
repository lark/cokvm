# CoKVM (Windows / WinUI 3)

Native Windows port of the macOS CoKVM application, built with **WinUI 3** (Windows
App SDK) and C#/.NET 8. It mirrors the macOS app's functionality: captures video
from a USB capture device (e.g. MS2130/MS2130S), forwards keyboard/mouse input over
BLE to the nRF52840 "CoKVM-Dongle" using the 22-byte VSS protocol, and syncs the
Caps Lock LED state.

## Architecture

| macOS file | Windows file | Responsibility |
|---|---|---|
| `VssPayload.swift` | `Protocol/VssPayload.cs` | 22-byte VSS payload (keyboard/mouse/LED) |
| `BluetoothClient.swift` | `Services/BluetoothService.cs` | BLE GATT (FF00/FF1D/FF2C), auto-reconnect, pairing |
| `VideoCapture.swift` | `Services/VideoCaptureService.cs` | MediaCapture, format selection, HW-accelerated preview |
| `KeyboardManager.swift` | `Input/KeyboardManager.cs` | Modifier tracking, 6-key array, combo parsing |
| `KeyCodeMap.swift` | `Input/WindowsKeyMap.cs` | VirtualKey → HID scancode |
| `MouseManager.swift` | `Input/MouseManager.cs` | Coordinate conversion (0–17280, absolute) |
| `VideoView.swift` | `UI/Controls/VideoView.xaml(.cs)` | GPU preview, mouse capture, combo panel |
| `StatusBarView.swift` | `UI/Controls/StatusBarView.xaml(.cs)` | Status labels + buttons |
| `ComboKeyPanel` | `UI/Controls/ComboKeyPanel.xaml(.cs)` | F-keys + special combos popup |
| `AppDelegate.swift` | `MainWindow.xaml(.cs)` | Coordinator, LED sync, event wiring |

## Protocol (VSS, 22 bytes)

```
[0x4B][0x4D][0x00][Type][8-byte data][10-byte padding]
```
- Type 0x01 Keyboard: `[modifiers][0x00][6 keycodes][pad]`
- Type 0x02 Mouse: `[buttons][x:i16le][y:i16le][wheel:i8][pad]` (absolute, 0–17280)
- Type 0x03 LED: `[led mask][pad]` (0x02 CapsLock)

BLE writes go directly to characteristic `FF1D` (no report-id prefix).

## Windows-specific behavior

- **Coordinate system**: Windows Y-axis is top-to-bottom, so no Y inversion
  (unlike macOS which does `height - y`).
- **Caps Lock LED**: keydown is dropped; only the final Caps Lock state is synced
  via the LED payload. Num Lock is forwarded as a normal key, so the controlled
  host toggles it itself.
- **Resizing**: X-axis only; height is derived as `width * 9/16 + 30` to keep the
  16:9 video + 30px status bar.
- **Video**: rendered via `CaptureElement` (Media Foundation hardware decode) to
  keep CPU usage low (< 10%).

## Build & run

Requirements: **Visual Studio 2022** with the _Windows application development_
workload (includes MSBuild + Windows App SDK targets). .NET 8 SDK is pulled in
by VS; a .NET 9 SDK also works (the project targets `net8.0-windows`).

### Option A — command line (recommended for quick runs)

Helper scripts in the project root (double-click in Explorer, or run from a
terminal):

| Script | What it does |
|---|---|
| `build.bat`          | Restore + build the **unpackaged, self-contained** x64 exe (Debug by default). Pass `release` for a Release build. |
| `run.bat`            | Launch the built exe. Builds first if missing. Pass `debug` or `release`. |
| `clean.bat`          | Delete `bin\` and `obj\`. |

Examples:
```
build.bat            :: Debug build
build.bat release    :: Release build
run.bat              :: Run the Debug build
run.bat release      :: Run the Release build
```

The unpackaged build runs directly as `bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\CoKVM.exe`
— no MSIX install or certificate needed. This is the mode that has been tested
end-to-end with the MS2130 capture card and CoKVM BLE dongle.

### Option B — Visual Studio 2022

1. Open `CoKVM.sln`.
2. Build (Ctrl+Shift+B).
3. Run with F5 using the **CoKVM** (MsixPackage) profile, or the
   **CoKVM (Unpackaged)** profile. VS auto-generates a dev certificate for the
   MSIX profile.

### Manual one-off build (no scripts)

From a "Developer Command Prompt for VS 2022":
```
msbuild CoKVM.csproj -t:Restore -p:Configuration=Debug -p:Platform=x64 ^
  -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:RuntimeIdentifier=win-x64
msbuild CoKVM.csproj -p:Configuration=Debug -p:Platform=x64 ^
  -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:RuntimeIdentifier=win-x64
```

Capabilities (`Package.appxmanifest`, used by the MSIX profile): `bluetooth`
(GATT) + `webcam`. The unpackaged profile accesses these as a desktop app
(webcam consent + BLE pairing are handled at runtime).

### Diagnostics

The unpackaged build writes a Bluetooth trace to
`%LOCALAPPDATA%\CoKVM\bt.log` and stores the bonded dongle id in
`%LOCALAPPDATA%\CoKVM\device.txt` (for auto-reconnect). Delete these to reset.
