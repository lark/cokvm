# CoKVM BLE Custom Input Protocol (VSS)

## 1. Overview

The CoKVM Dongle is based on the nRF52840 Dongle. On the USB side it enumerates on the controlled host as a composite device (VID/PID = `0x1915`/`0x5211`) exposing a CDC ACM console, a boot-style keyboard and an absolute-coordinate mouse. On the BLE side it acts as a GATT server with a single connection to the control host (macOS / Windows, `CONFIG_BT_MAX_CONN=1`).

The control host sends fixed **22-byte** binary frames over the **Vendor-Specific Service (VSS)**. After validating the frame header, the Dongle converts keyboard/mouse frames into standard USB HID input reports injected into the controlled host. LED status frames keep the keyboard LEDs of the controlled host in sync with the state shown by the control host.

- Direction: control host → Dongle only (Write Without Response); there is no business uplink channel.
- Video transport and firmware upgrade are outside the scope of this protocol.

## 2. GATT Service Definition

| Item | Value |
| :--- | :--- |
| Device name | `CoKVM-Dongle` (complete name, in scan response) |
| Service UUID | `FF00` (16-bit, base `0000xxxx-0000-1000-8000-00805F9B34FB`) |
| DATA characteristic | `FF1D`, **Write Without Response**, permission Write Authenticated, C→D |
| INFO characteristic | `FF2C`, **Read**, permission Read Authenticated, D→C, returns ASCII `"OK"` (2 bytes) |
| Advertising packet | Flags + 16-bit Service UUID `FF00` |
| Scan response | Complete device name, Appearance `0x0000`, 16-bit UUIDs of DIS `0x180A` and BAS `0x180F` |

**MTU**: a frame is 22 bytes, so the ATT MTU must be ≥ 25. With `CONFIG_BT_SMP=y` the firmware's L2CAP TX MTU defaults to 65, which is sufficient. There is no Nordic UART Service (NUS) channel.

**Connection parameters** (firmware-preferred values): connection interval 7.5–15 ms, slave latency 0, supervision timeout 5 s.

## 3. Frame Structure

All multi-byte values are little-endian. Every frame is exactly 22 bytes; the first 4 bytes form the header:

| Offset | Field | Value |
| :--- | :--- | :--- |
| 0 | `magic_k` | Fixed `0x4B` ('K') |
| 1 | `magic_m` | Fixed `0x4D` ('M') |
| 2 | `version` | Fixed `0x00` |
| 3 | `type` | `0x01` keyboard; `0x02` mouse; `0x03` LED status |
| 4–11 | `data` | 8-byte union, interpreted according to `type` (see Section 4) |
| 12–21 | `reserved` | 10 bytes, must be zero |

**Validation rules** (any failure rejects the frame):

| Condition | Response |
| :--- | :--- |
| Write length < 22 | `ATT_ERR_INVALID_ATTRIBUTE_LEN` |
| `magic_k` / `magic_m` mismatch | `ATT_ERR_VALUE_NOT_ALLOWED` |
| `version != 0x00` | `ATT_ERR_VALUE_NOT_ALLOWED` |
| `type` outside 1–3 | `ATT_ERR_VALUE_NOT_ALLOWED` |

Lengths greater than 22 are accepted; the extra bytes are ignored for forward compatibility.

## 4. Payload Definitions

### 4.1 Keyboard Data (`type = 0x01`)

| `data` offset | Field | Description |
| :--- | :--- | :--- |
| 0 | `modifiers` | Modifier bitmask (see table below) |
| 1 | `delay` | **Reserved, control host must send `0`**; used internally by the Dongle for LED-sync timing (10 ms units) |
| 2–7 | `keycodes[6]` | USB HID Usage IDs of currently held keys; `0` means empty slot |

Every key state change sends one frame with the **complete state** (current modifiers plus all currently held keys), one frame for press and one for release. At most 6 non-modifier keys at a time; the default report descriptor supports Usage IDs `0x00`–`0x65`.

Modifier bitmask:

| Bit | Value | Meaning |
| :--- | :--- | :--- |
| bit0 | `0x01` | Left Ctrl |
| bit1 | `0x02` | Left Shift |
| bit2 | `0x04` | Left Alt / Option |
| bit3 | `0x08` | Left GUI / Command |
| bit4–7 | — | Unused, set to 0 |

Example: Left Shift + `A` (Usage `0x04`) pressed
`4B 4D 00 01 02 00 04 00 00 00 00 00-00 00 00 00 00 00 00 00 00 00`

### 4.2 Mouse Data (`type = 0x02`)

| `data` offset | Field | Description |
| :--- | :--- | :--- |
| 0 | `buttons` | bit0 left `0x01`, bit1 right `0x02`, bit2 middle `0x04`, bit3/bit4 buttons 4/5; other bits zero |
| 1–2 | `x` | **Absolute** coordinate, `int16` little-endian, `0` (left) – `17280` (right) |
| 3–4 | `y` | **Absolute** coordinate, `int16` little-endian, `0` (top) – `17280` (bottom) |
| 5 | `wheel` | **Relative** delta, `int8`, `-127`–`127`; one notch is ±120 |
| 6–7 | — | Reserved, zero |

Coordinates map to a fixed `17280 × 17280` logical space independent of the controlled host's resolution; non-scroll frames set `wheel` to 0.

> Note: X/Y are absolute, so every frame moves the pointer to the given position. Scroll-only frames must repeat the last absolute position and button state; sending `(0, 0)` moves the pointer to the top-left corner and clears the buttons.

### 4.3 LED Status (`type = 0x03`)

| `data` offset | Field | Description |
| :--- | :--- | :--- |
| 0 | `led_status` | Target LED state: bit0 Num Lock `0x01`, bit1 Caps Lock `0x02`; other bits ignored |
| 1–7 | — | Reserved, zero |

This frame means "the LED state the controlled host should currently be in". Because there is no HID output channel over BLE, the control host must send it whenever its LED state changes. The Dongle compares it with the known state and, on mismatch, injects Num Lock / Caps Lock key presses (press followed by release after 10 ms) into the controlled host to flip the LEDs.

Clients currently use only the Caps Lock bit (bit1); the Num Lock bit (bit0) is supported by the firmware but is not sent by the clients.

## 5. LED Synchronization State Machine

The USB HID LED output report from the controlled host is the source of truth:

- `INITIALIZING`: no host LED output report yet; incoming BLE LED states are recorded but not acted upon.
- `SYNCED`: matches the host. On a differing target state, compute the differing bits, enter `SYNCING` and inject the key presses.
- `SYNCING`: wait for the host to report LEDs. Matching target → back to `SYNCED`. Mismatch with fewer than 3 retries → inject the diff again; after 3 retries accept the host state and return to `SYNCED`.
- While `SYNCED`, LED drift caused by the user on the physical keyboard is ignored until the control host sends another LED status frame.

## 6. Connection, Pairing and Security

All management is done through the USB CDC ACM console (Zephyr shell); no BLE management channel exists:

| Command | Purpose |
| :--- | :--- |
| `cokvm pair` | Open a 60-second pairing window (blue LED toggles every 200 ms) |
| `cokvm list` | List bonded host addresses |
| `cokvm clear` | Delete all bonds and leave bondable mode |
| `cokvm test ...` | Only with `CONFIG_COKVM_TESTSUITE=y`; inject test input |

- The pairing window opens only after a manual `cokvm pair`; it closes automatically on timeout (bondable=false). Bonded hosts may still reconnect outside the window; pairing attempts from unbonded hosts are rejected.
- Bonds are stored in NVS, up to 5 hosts (`CONFIG_BT_MAX_PAIRED=5`). After a disconnect the Dongle keeps advertising and accepts reconnection from bonded hosts.
- `CONFIG_COKVM_PASSKEY_PAIRING=y` (the `prj.conf` default): Passkey Entry, the 6-digit passkey is printed on the console, IO capability is Display Only, MITM is enforced (security level L3), and both `FF1D` and `FF2C` require an authenticated link.
- `CONFIG_COKVM_PASSKEY_PAIRING=n`: Just Works (security level L2), encrypted but without MITM protection.
- After connection the firmware first requests L2 encryption. The control host should read `FF2C` after service discovery: the read triggers pairing/security upgrade, and only a successful `"OK"` means the link is authenticated and data writes will be accepted; otherwise writes to `FF1D` are rejected.
- Firmware upgrade is out of scope and requires wired USB DFU (`west flash`, etc.).

## 7. USB HID Mapping

| Direction | Report | Content |
| :--- | :--- | :--- |
| D→host | Report ID `0x01` | modifiers 1B + reserved 1B + 6 keycodes, 8B total (boot-style) |
| D→host | Report ID `0x02` | buttons 1B + X 2B (absolute, 0–17280) + Y 2B (absolute, 0–17280) + wheel 1B, 6B total |
| host→D | Report ID `0x01` Output | LED bitmap 1B: bit0 Num Lock, bit1 Caps Lock |

The firmware uses `CONFIG_USBD_HID_IN_BUF_COUNT=6`; when the buffers are full, D→host reports queue until they can be sent.

## 8. Status LEDs

| State | Indication |
| :--- | :--- |
| `NORMAL` | All RGB LEDs off |
| `PAIRING` | Blue LED toggles every 200 ms |
| `CONNECTED` | Green LED solid |
| `ERROR` | Red LED toggles every 500 ms |

## 9. Versioning and Compatibility

- The protocol version is fixed at `0x00`, mirrored in the header `version` field; mismatches are rejected.
- Extensions may only use the `reserved` bytes or fields appended after byte 22; receivers must accept lengths ≥ 22 and ignore extra bytes.
- Undefined `type` values are always rejected; control hosts must not rely on the Dongle ignoring unknown types.
