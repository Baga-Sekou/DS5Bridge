# DualSense (DS5) 协议参考

来源：`ds.evua.cc` 前端 bundle（WebHID 解析器 `q8`）反推 + 本机 hidapi 实测校验。
**重要换算**：该网站用 WebHID，`data` 不含 reportId；hidapi 读到的含 reportId。
因此 **hidapi 字节号 = 网站索引 + 1**。

## USB 输入报告（手柄→PC）

- 长度 **64 字节**，report ID `0x01`（byte 0）。

| 偏移 | 内容 | 编码 |
|---|---|---|
| 0 | Report ID | `0x01` |
| 1 | 左摇杆 X | 0–255，中心≈128 |
| 2 | 左摇杆 Y | 0–255 |
| 3 | 右摇杆 X | 0–255 |
| 4 | 右摇杆 Y | 0–255 |
| 5 | L2 扳机（模拟量） | 0–255 |
| 6 | R2 扳机（模拟量） | 0–255 |
| 7 | 序列计数器 | 0→255 循环 |
| 8 | 数字键组1 | 低4位=十字键；bit4=□ bit5=✕ bit6=○ bit7=△ |
| 9 | 数字键组2 | bit0=L1 bit1=R1 bit2=L2 bit3=R2 bit4=Create bit5=Options bit6=L3 bit7=R3 |
| 10 | 数字键组3 | bit0=PS bit1=触控板点击 bit2=静音 |
| 11–15 | 保留 | |
| 16–17 | 陀螺仪 X | int16 LE |
| 18–19 | 陀螺仪 Y | int16 LE |
| 20–21 | 陀螺仪 Z | int16 LE |
| 22–23 | 加速度计 X | int16 LE |
| 24–25 | 加速度计 Y | int16 LE |
| 26–27 | 加速度计 Z | int16 LE |
| 28–33 | 传感器时间戳 | uint32 |
| 34–40 | 触控点 1 | |
| 41–47 | 触控点 2 | |
| 53 | 电池 | |
| 54 | 耳机连接 | |

**十字键低4位编码**（byte8 & 0x0F）：`0=上 1=右上 2=右 3=右下 4=下 5=左下 6=左 7=左上 8=中位`

## 蓝牙输入报告

- 长度 **78 字节**，report ID `0x31`（byte 0）。所有字段相对 USB 后移 1
  （摇杆在 byte 2–5，按钮在 byte 9–11，陀螺仪在 byte 17–28）。

## USB 输出报告（PC→手柄，游戏下发的震动/触觉）

- report ID `0x02`，**48 字节**（`HMOutputPacket.Data`，无 reportId 字节）。

| 偏移 | 内容 |
|---|---|
| 0 | `0xFF`（magic） |
| 1 | `0xF7`（magic） |
| 2 | **右马达 motorRight** 0–255 |
| 3 | **左马达 motorLeft** 0–255 |
| 4 | 耳机音量 |
| 5 | 扬声器音量 |
| 6–7 | 音频控制 |
| 8–9 | 静音灯 |
| 10–37 | 自适应扳机数据（左/右各一段） |
| 38–43 | 灯条设置 / 玩家指示灯 |
| 44 | 灯条 R |
| 45 | 灯条 G |
| 46 | 灯条 B |

## 蓝牙输出报告

- report ID `0x31`，**547 字节**（含触觉音频流）。

## 注意事项

- 普通 DS5 与 **DualSense Edge** 面键布局不同：Edge 的面键是模拟量（位于 byte 3），
  解析函数不同（网站里普通版是 `q8`，Edge 是 `j8`）。本项目针对普通 DS5。
- 在 HIDMaestro 里这些偏移都已由 `dualsense` profile 的 `extendedOutputReport` 描述，
  `OutputDecoded.Fields` 直接给出 `leftMotor` / `rightMotor` / `lightbar` 等具名字段，
  **实际代码无需手工解析**——本文件仅用于调试与单元测试。
