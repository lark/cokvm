# CoKVM BLE 自定义输入协议（VSS）

## 1. 概述

CoKVM Dongle 基于 nRF52840 Dongle。USB 侧作为复合设备接入被控主机（VID/PID = `0x1915`/`0x5211`），包含 CDC ACM 控制台、启动类键盘与绝对坐标鼠标；BLE 侧作为 GATT 服务端，与控制端（macOS / Windows）建立单条连接（`CONFIG_BT_MAX_CONN=1`）。

控制端通过 **Vendor-Specific Service (VSS)** 向 Dongle 下发固定 **22 字节** 的二进制帧。Dongle 校验帧头后，把键盘/鼠标帧转换为标准 USB HID 输入报文注入被控主机；LED 状态帧则用于把控制端显示的键盘 LED 状态同步到被控主机。

- 方向：控制端 → Dongle 单向下行（Write Without Response）；无业务上行通道。
- 本协议不承载视频数据，也不承载固件升级。

## 2. GATT 服务定义

| 项目 | 值 |
| :--- | :--- |
| 设备名 | `CoKVM-Dongle`（完整名，位于扫描响应） |
| 服务 UUID | `FF00`（16-bit，基址 `0000xxxx-0000-1000-8000-00805F9B34FB`） |
| DATA 特征 | `FF1D`，属性 **Write Without Response**，权限 Write Authenticated，C→D |
| INFO 特征 | `FF2C`，属性 **Read**，权限 Read Authenticated，D→C，返回 ASCII `"OK"`（2 字节） |
| 广播包 | Flags + 16-bit Service UUID `FF00` |
| 扫描响应 | 完整设备名、Appearance `0x0000`、DIS `0x180A`、BAS `0x180F` 的 16-bit UUID |

**MTU**：单帧 22 字节，要求 ATT MTU ≥ 25。固件在 `CONFIG_BT_SMP=y` 下 L2CAP TX MTU 默认为 65，满足要求。不存在 Nordic UART Service（NUS）通道。

**连接参数**（固件侧建议值）：连接间隔 7.5–15 ms，slave latency 0，supervision timeout 5 s。

## 3. 帧结构

所有数值均为小端（Little-Endian）。固定 22 字节，前 4 字节为头部：

| 偏移 | 字段 | 取值 |
| :--- | :--- | :--- |
| 0 | `magic_k` | 固定 `0x4B`（'K'） |
| 1 | `magic_m` | 固定 `0x4D`（'M'） |
| 2 | `version` | 固定 `0x00` |
| 3 | `type` | `0x01` 键盘；`0x02` 鼠标；`0x03` LED 状态 |
| 4–11 | `data` | 8 字节联合体，含义由 `type` 决定（见第 4 节） |
| 12–21 | `reserved` | 10 字节，必须填 `0` |

**校验规则**（任一不满足即拒绝）：

| 条件 | 返回 |
| :--- | :--- |
| 写入长度 < 22 | `ATT_ERR_INVALID_ATTRIBUTE_LEN` |
| `magic_k`/`magic_m` 不匹配 | `ATT_ERR_VALUE_NOT_ALLOWED` |
| `version != 0x00` | `ATT_ERR_VALUE_NOT_ALLOWED` |
| `type` 不在 1–3 | `ATT_ERR_VALUE_NOT_ALLOWED` |

长度大于 22 时，多出的字节被忽略（便于向后兼容扩展）。

## 4. 载荷定义

### 4.1 键盘数据（`type = 0x01`）

| `data` 偏移 | 字段 | 说明 |
| :--- | :--- | :--- |
| 0 | `modifiers` | 修饰键位掩码（见下表） |
| 1 | `delay` | **保留，控制端必须填 `0`**；Dongle 内部用于 LED 同步的发送延时（单位 10 ms） |
| 2–7 | `keycodes[6]` | 当前按下的 USB HID Usage ID 列表，`0` 表示空位 |

每次按键状态变化都发送一帧**完整状态**（当前修饰键 + 当前所有按下的键），按下与释放各一帧。最多同时 6 个非修饰键；默认报告描述符支持 Usage ID `0x00`–`0x65`。

修饰键掩码：

| 位 | 值 | 含义 |
| :--- | :--- | :--- |
| bit0 | `0x01` | Left Ctrl |
| bit1 | `0x02` | Left Shift |
| bit2 | `0x04` | Left Alt / Option |
| bit3 | `0x08` | Left GUI / Command |
| bit4–7 | — | 未使用，填 0 |

示例：左 Shift + `A`（Usage `0x04`）按下
`4B 4D 00 01 02 00 04 00 00 00 00 00-00 00 00 00 00 00 00 00 00 00`

### 4.2 鼠标数据（`type = 0x02`）

| `data` 偏移 | 字段 | 说明 |
| :--- | :--- | :--- |
| 0 | `buttons` | bit0 左键 `0x01`，bit1 右键 `0x02`，bit2 中键 `0x04`，bit3/bit4 为第 4/5 键；其余位填 0 |
| 1–2 | `x` | **绝对坐标**，`int16` 小端，范围 `0`（左）– `17280`（右） |
| 3–4 | `y` | **绝对坐标**，`int16` 小端，范围 `0`（上）– `17280`（下） |
| 5 | `wheel` | **相对增量**，`int8`，范围 `-127`–`127`；一格滚动为 ±120 |
| 6–7 | — | 保留，填 0 |

坐标映射到固定的 `17280 × 17280` 逻辑空间，与被控主机分辨率无关；非滚动帧 `wheel` 填 0。

> 注意：X/Y 是绝对坐标，每一帧都会把指针移动到所给位置。发送仅滚轮的帧时必须复用最近一次的绝对坐标与按键状态；填 `(0, 0)` 会把指针移到左上角并清除按键。

### 4.3 LED 状态（`type = 0x03`）

| `data` 偏移 | 字段 | 说明 |
| :--- | :--- | :--- |
| 0 | `led_status` | 目标 LED 状态：bit0 Num Lock `0x01`，bit1 Caps Lock `0x02`，其余位忽略 |
| 1–7 | — | 保留，填 0 |

该帧表示“控制端认为被控主机当前应处于的 LED 状态”。由于 BLE 侧没有 HID 输出通道，控制端需在其 LED 状态变化时主动下发；Dongle 收到后与已知状态比较，如不一致则向被控主机注入 Num Lock / Caps Lock 按键（按下后 10 ms 释放）来翻转 LED。

当前控制端仅使用 Caps Lock 位（bit1）；Num Lock 位（bit0）由固件支持，但客户端不下发。

## 5. LED 同步状态机

Dongle 以被控主机 USB HID 的 LED 输出报文为最终事实来源：

- `INITIALIZING`：尚未收到主机 LED 输出报文，收到的 BLE LED 状态仅记录，不动作。
- `SYNCED`：与主机一致。若收到不同的目标状态，计算差异位，进入 `SYNCING` 并注入按键。
- `SYNCING`：等待主机回报 LED。若回报与目标一致则回到 `SYNCED`；不一致且重试次数 < 3 时重新注入差异键；超过 3 次则接受主机状态并回到 `SYNCED`。
- 在 `SYNCED` 下若主机自行改变 LED（用户直接按了物理键盘），Dongle 忽略该漂移，直到控制端再次下发 LED 状态帧。

## 6. 连接、配对与安全

安全管理全部通过 USB CDC ACM 控制台（Zephyr shell）完成，不再使用 BLE 通道：

| 命令 | 作用 |
| :--- | :--- |
| `cokvm pair` | 打开 60 秒配对窗口（蓝色 LED 每 200 ms 闪烁） |
| `cokvm list` | 列出已绑定主机地址 |
| `cokvm clear` | 删除全部绑定并退出可绑定状态 |
| `cokvm test ...` | 仅在 `CONFIG_COKVM_TESTSUITE=y` 时提供，用于注入测试输入 |

- 配对窗口只在手动执行 `cokvm pair` 后开启，超时后自动关闭可绑定（bondable=false）。已绑定主机在窗口外仍可重连，未绑定主机的配对请求被拒绝。
- 绑定信息保存在 NVS，最多 5 个主机（`CONFIG_BT_MAX_PAIRED=5`），断开后 Dongle 继续广播并接受已绑定主机重连。
- `CONFIG_COKVM_PASSKEY_PAIRING=y`（`prj.conf` 默认）：使用 Passkey Entry，6 位配对码打印在控制台，IO 能力为 Display Only，强制 MITM（安全等级 L3），`FF1D`/`FF2C` 均要求已认证链路。
- `CONFIG_COKVM_PASSKEY_PAIRING=n`：使用 Just Works（安全等级 L2），仅加密不防 MITM。
- 连接建立后固件先请求 L2 加密。控制端在服务发现后应读取 `FF2C`：该读取会触发配对/安全升级，成功返回 `"OK"` 才表示链路已认证、可以发送数据；失败则 `FF1D` 写入会被拒绝。
- 固件升级不在此协议内，需通过有线 USB DFU（`west flash` 等）完成。

## 7. USB HID 映射

| 方向 | 报告 | 内容 |
| :--- | :--- | :--- |
| D→被控主机 | Report ID `0x01` | 修饰键 1B + 保留 1B + 6 个 keycode，共 8B（Boot 风格） |
| D→被控主机 | Report ID `0x02` | buttons 1B + X 2B（绝对，0–17280）+ Y 2B（绝对，0–17280）+ wheel 1B，共 6B |
| 被控主机→D | Report ID `0x01` Output | LED 位图 1B，bit0 Num Lock、bit1 Caps Lock |

固件使用 `CONFIG_USBD_HID_IN_BUF_COUNT=6`，队列满载时 D→主机方向的报文会排队等待发送。

## 8. 状态指示灯

| 状态 | 指示 |
| :--- | :--- |
| `NORMAL` | RGB 全灭 |
| `PAIRING` | 蓝色 LED 每 200 ms 翻转 |
| `CONNECTED` | 绿色常亮 |
| `ERROR` | 红色 LED 每 500 ms 翻转 |

## 9. 版本与兼容性

- 当前协议版本固定为 `0x00`，与帧头 `version` 字段对应；版本不匹配直接拒绝。
- 扩展只能使用 `reserved` 字节或追加在 22 字节之后的字段；接收端必须接受长度 ≥ 22 并忽略多余字节。
- 未定义的 `type` 一律拒绝，控制端不得依赖 Dongle 忽略未知类型。
