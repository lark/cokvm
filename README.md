# CoKVM

CoKVM (Collective KVM) turns your laptop or desktop into the keyboard, monitor and mouse of another machine, using an inexpensive USB video capture device (typically MS2130 / MS2130S) and an nRF52840 Dongle.

## Overview

CoKVM is built for field work, data center maintenance and home machines (Linux boxes, for example):

- The controlled host is attached to the capture device and the Dongle. The Dongle enumerates on that host as a standard USB keyboard, an absolute-coordinate mouse and a USB serial console, so it works even in BIOS/UEFI.
- The control app (macOS or Windows) shows the captured video in a local window and sends keyboard, mouse and keyboard-LED state to the Dongle over BLE.
- The Dongle converts each 22-byte VSS frame into USB HID reports for the controlled host. See [protocols.en.md](docs/protocols.en.md) for the wire format.

This project includes firmware and client applications
* `firmware/`, nRF52840 Dongle firmware (Zephyr): USB composite device plus BLE VSS service
* `control/macos/`, macOS control app (Swift / AppKit, SwiftPM)
* `control/win/`, Windows control app (C# / .NET 8 / WinUI 3)

### Why CoKVM

- **Portable** — just a capture device and a small dongle.
- **Open** — BLE GATT, USB HID and standard HID usage IDs, with a documented protocol.
- **No network setup** — network is not needed.
- **Works before the OS boots** — usable in BIOS/UEFI on modern hardware (2-4s reconnect delay expected).
- **Inexpensive** — the capture device and the dongle cost around $11 on China e-commerce platform.

## Building and Packaging

### Firmware (nRF52840 Dongle)

Requirements:

- Zephyr 4.4+ with the matching Zephyr SDK, and `west` on your `PATH`.
- Flash `zephyr.hex` with nRF Connect Programmer.
- The board is fixed to `nrf52840dongle` in `firmware/CMakeLists.txt`.

Build:

```bash
cd firmware
west build -b nrf52840dongle
```

For a development build (`prj_dev.conf` also enables the shell, BT shell, USBD shell and `CONFIG_EXCEPTION_DEBUG`):

```bash
cd firmware
FILE_SUFFIX=dev west build -b nrf52840dongle -p always
```

Use `-p always` when switching between the default and dev configurations. The output is `firmware/build/zephyr/zephyr.hex` (along with `zephyr.elf` and `zephyr.bin`).

The Dongle has no on-board debugger, so enter USB DFU bootloader mode first (hold RESET while plugging it in; the red LED blinks), then flash `zephyr.hex` with nRF Connect Programmer. For distribution, ship the hex file or a DFU package.

Key Kconfig options:

| Option | Default | Description |
| :--- | :--- | :--- |
| `CONFIG_COKVM_PASSKEY_PAIRING` | `y` | Passkey Entry pairing (MITM, security level 3); disable for Just Works (level 2) |
| `CONFIG_COKVM_TESTSUITE` | `n` | Enables the `cokvm test ...` input injection commands |

Pairing and bonds are managed from the USB serial console:

```
cokvm pair    # open a 60-second pairing window
cokvm list    # list bonded hosts
cokvm clear   # erase all bonds
```

### macOS app

Requires macOS 13 or later and Swift 5.10 (Xcode or Command Line Tools).

```bash
cd control/macos
./build.sh            # debug
./build.sh release    # release
```

The script builds the SwiftPM package, assembles `CoKVM.app` and ad-hoc signs it. The result is `control/macos/CoKVM.app`. On first launch, grant Bluetooth and camera access in System Settings → Privacy & Security.

### Windows app

Requires Windows 10 1809 or later (x64), Visual Studio 2022 with the "Windows application development" workload (including the Windows App SDK targets), and the .NET 8 SDK.

Build an unpackaged, self-contained executable (no certificate needed) from the command line:

```bat
cd control\win\cokvm
build.bat            :: Debug
build.bat release    :: Release
run.bat              :: run the built executable
```

Output: `control\win\cokvm\bin\x64\<Config>\net8.0-windows10.0.19041.0\win-x64\CoKVM.exe`.

To produce an MSIX package, open `CoKVM.csproj` in Visual Studio 2022, select x64 and choose "Publish → Create App Packages"; Visual Studio creates a development certificate and signs the package.

## How to Use

Pair the dongle over USB first, then move it to the controlled host.

1. Flash the firmware as described above.
2. Plug the dongle into a computer where you can open a serial console — the control computer itself is fine for this step.
3. Open the dongle's CDC ACM port (`/dev/ttyACM*` on Linux/macOS, or the matching COM port on Windows) at 115200 8N1.
4. At the `cokvm>` prompt, run `cokvm pair`. The dongle opens a 60-second pairing window and blinks the blue LED every 200 ms.
5. Start the control app and select `CoKVM-Dongle` once it appears. macOS connects and triggers pairing automatically; on Windows, select the device and click the Bluetooth button to initiate pairing.
6. If Passkey Entry is enabled (the default), enter the 6-digit code printed on the console into the pairing dialog.
7. Once paired, unplug the dongle and connect it — together with the capture device — to the controlled host. Later reconnects are automatic; open a new pairing window only for a new control host or after `cokvm clear`.

In the app, select the capture device to start the video. Any camera, including the built-in one, works as a test video source when no capture device is available. Keyboard and mouse input is forwarded only while video is running.

## Limitations

**Hardware and connectivity**

- Only the nRF52840 Dongle is supported (tested on E-Byte E104-BT5040U). There is no prebuilt firmware or finished hardware yet, so you have to build and flash it yourself.
- The Dongle accepts a single BLE connection at a time (`CONFIG_BT_MAX_CONN=1`), i.e. one control host.
- The pairing window can only be opened with `cokvm pair` on the USB serial console (60 seconds); there is no button-based alternative; plug the dongle into a computer and use a console app (minicom or something like) to access the dongle's console
- There is no OTA/DFU over BLE.
- Passkey Entry is enabled by default, so pairing requires typing the 6-digit code shown on the console into the control app. Disabling it falls back to Just Works (encrypted, but no MITM protection).
- Range and interference are limited by BLE.

**Input and protocol**

- The keyboard is 6KRO: at most six non-modifier keys per frame. The default report descriptor supports HID usages `0x00`–`0x65` only, with no media keys or NKRO.
- The mouse uses absolute coordinates on a fixed `17280 × 17280` logical grid, with a relative wheel limited to `-127`–`127` per frame. The clients currently use only the left, right and middle buttons.
- BLE writes use Write Without Response, so there is no retransmission or application-level flow control. Mouse motion can be dropped on a weak link or a full queue (the firmware queue holds 64 items, and mouse input is throttled for 3 seconds after a failed USB submission).
- Only the Caps Lock LED is synchronized. Num Lock is not synced from the clients; the Windows app forwards it as a normal key so the controlled host toggles it itself. The macOS app also reports Caps Lock only.
- Input is forwarded only while video capture is running, to avoid stray keystrokes without a live picture. Select the capture device first.
- The video area is laid out for 16:9 content.

**Platform support**

- Only macOS and Windows control apps; no Linux support yet. Contribution is welcome.
- The macOS build script copies the binary from the Apple Silicon path (`.build/arm64-apple-macosx`), so Intel Macs need to adjust the script.
- The Windows app is an x64 WinUI 3 application for Windows 10 1809 or later.

## License

- `firmware/` is licensed under the [Apache License 2.0](firmware/LICENSE).
- `control/macos/` and `control/win/` are licensed under the [GNU General Public License v2.0](control/macos/LICENSE); see also [control/win/LICENSE](control/win/LICENSE).
