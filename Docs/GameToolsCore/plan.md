# GameTools 核心系统实施计划

## 1. 计划概述
本项目为运行于 .NET 8 与 Windows 7 及以上（Windows 7 SP1、Windows 8/8.1、Windows 10、Windows 11）平台的底层交互系统，涵盖后台常驻进程、屏幕/窗口截图、全局快捷键、前后台文本输入模拟、全局及进程消息钩子监听五大核心能力。

## 2. 实施目标与技术选型
- **目标框架**：`.NET 8` (`net8.0-windows`)。
- **兼容策略**：
  - 核心底层 P/Invoke 调用针对 Windows 7 做特性探测与优雅降级（如 PrintWindow 标志位区分、Per-Monitor DPI 与系统 DPI 自适应）。
  - 后台消息循环采用独立单线程单元（STA）线程的消息泵机制（`BackgroundMessagePump`），保证热键与钩子调度可靠。
  - GDI 资源全面采用安全句柄封装（`SafeGdiHandle`），避免内存与 GDI 资源泄漏。
  - 全局钩子采用 `GCHandle` 显式锁存防 GC 回收，并通过 `System.Threading.Channels` 异步解耦，杜绝触发 Windows 钩子超时卸载。

## 3. 阶段规划
1. **阶段一：文档与工程基础**：建立规范文档与 .NET 8 分层解决方案（Core, Win32, Infrastructure, App, Tests）。
2. **阶段二：Win32 抽象与系统适配**：封装原生 API、系统版本探测器与安全句柄。
3. **阶段三：后台宿主与单实例控制**：建立单实例 Mutex 与 STA 消息泵。
4. **阶段四：截图模块**：全屏截取、区域截取、后台窗口 PrintWindow 截取（Win7 降级自适应）。
5. **阶段五：全局快捷键**：RegisterHotKey 封装、冲突检测、异步事件派发。
6. **阶段六：文本与按键模拟**：前台 Unicode SendInput 输入与后台非激活窗口 PostMessage/SendMessage 投递。
7. **阶段七：消息钩子与进程监听**：WH_KEYBOARD_LL/WH_MOUSE_LL 低级钩子 + 前台进程过滤 + WinEventHook 进程事件监听。
8. **阶段八：应用宿主与测试验证**：提供命令行/托盘演示应用，运行全套测试套件。

## 变更记录
| 时间 | 变更摘要 |
| --- | --- |
| 2026-10-08 15:58:00 +08:00 | 创建计划文档，明确 .NET 8 与 Win7+ 平台架构目标与分阶段计划。 |
