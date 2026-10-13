# DS5Bridge

把 **GameSir 九尾狐（音圈马达）手柄**在系统里伪装成 **Sony DualSense（DS5）**，
让游戏把针对 DS5 调校的震动数据下发给它，再把震动转回九尾狐的马达——用软件弥补
"九尾狐走 XInput、只能被当转子马达驱动"带来的震感损失。

> Turn a GameSir 九尾狐 pad into a virtual DualSense so games send it
> DualSense-tuned haptics, which are then routed to the pad's voice-coil motors.

## 功能

- **虚拟 DualSense**：游戏看到的是一个真实的 DS5（VID 054C / PID 0CE6），
  支持 USB 输入报告、震动/灯条/自适应扳机输出报告，以及 **USB 音频端点**。
- **输入转发**：九尾狐（有线 `PID 1050` / 无线 dongle `PID 103E`，自动识别）→ 虚拟 DS5。
- **马达震动回写**：游戏的 `leftMotor/rightMotor` → 九尾狐 XInput 马达。
- **波形合成**：把游戏的低频震动包络调制到自设计的载波上，做出脉冲/波浪质感。
- **音频触觉**：接管 DS5 的 4 声道音频端点，取声道 3/4（音圈触觉）直接驱动九尾狐
  ——救《潜水员戴夫》这类"音频触觉"游戏。
- **隐藏物理手柄**：用 HidHide 把物理九尾狐对游戏隐藏，避免游戏看到两个手柄。
- **托盘常驻 / 开机自启 / 关闭到托盘**。

## 依赖

- .NET 10（Windows）
- [HIDMaestro](https://github.com/hifihedgehog/HIDMaestro)（MIT）— 虚拟控制器引擎。
  `lib/` 内已附其预编译 `HIDMaestro.Core.dll` 及 WinRT 依赖（走 Git LFS）。
- （可选）[HidHide](https://github.com/nefarius/HidHide) — 用于隐藏物理手柄。

## 构建运行

```bash
dotnet build
# 以管理员运行（创建虚拟设备需要提权）
bin/Debug/net10.0-windows10.0.26100.0/DS5Bridge.exe
```

程序常驻托盘。设置里有：启用桥接、开机自启、关闭到托盘、合成震动、震动预设/强度、
隐藏物理手柄。

## 用法要点

1. **先连接好手柄（有线或无线），再打开游戏**。稳态下物理手柄会被完全隐藏，
   游戏只看到虚拟 DS5。
2. 不要在游戏运行中切换连接方式：切换瞬间物理手柄的 **XInput 接口**会在
   Windows XInput 子系统中短暂出现 1.5~2.5 秒（系统固有延迟，见下），可能被游戏抓到。
3. **退出请走托盘菜单，不要用任务管理器强杀**——强杀会留下损坏的虚拟设备节点。

## 已知限制

- **切换连接方式的瞬间**：物理手柄的 XInput 接口会有约 **1.5~2.5 秒**的暴露窗口。
  实测即使隐藏 `HID` 层（~120 ms 生效），**Windows XInput 子系统也要 1.5~2.5 秒
  才移除隐藏的设备**，这是系统缓存行为，HidHide 无法突破。稳态没有此问题。
- **HidHide 是全局 cloak 开关**，且会锁住未列入白名单的进程；本程序做了预检和
  热插拔重隐藏，但请勿在启用隐藏时强杀进程。
- **反作弊**：虚拟设备可被内核级反作弊识别，仅用于无反作弊的游戏。
- 虚拟 DS5 无法模拟真机的认证芯片，PS5 联机等场景不适用。

## 目录

```
Core/     DeviceWatcher / DeviceChangeWatcher / InputPump / VirtualDS5 /
          HapticEngine / HidHide / XInput
App/      BridgeService / TrayApp / Settings / SettingsForm / StartupManager
assets/   图标（make_icon.py 可重新生成）
docs/     DS5 协议字节布局参考
lib/      HIDMaestro.Core.dll 及依赖（Git LFS）+ LICENSE-HIDMaestro.txt（许可全文）
```

## 致谢

- [HIDMaestro](https://github.com/hifihedgehog/HIDMaestro)（MIT）— 纯用户态虚拟控制器引擎，
  `lib/HIDMaestro.Core.dll` 来自其 release，按 MIT 要求随附许可证全文见
  [`lib/LICENSE-HIDMaestro.txt`](lib/LICENSE-HIDMaestro.txt)。
- [HidHide](https://github.com/nefarius/HidHide)（MIT）— 隐藏物理设备的过滤驱动。
- DS5 报告字节布局参考自社区（含 `ds.evua.cc` 的 WebHID 解析器），见 `docs/ds5_protocol.md`。
