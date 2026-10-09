# GameTools 架构与详细设计规范

## 1. 系统分层架构

```
+-----------------------------------------------------------+
|              GameTools.App (WPF + Prism)                  |
|  - MainWindow / MainViewModel（MVVM，命令与绑定驱动）       |
|  - 宿主模式：交互窗口 / 仅托盘 / 无头                     |
|  - NLog 日志；服务层负责文件布局与线程切换                 |
+-----------------------------------------------------------+
                               |
+-----------------------------------------------------------+
|           GameTools.Infrastructure (能力实现层)            |
|  - BackgroundMessagePump (STA 消息泵，超时与取消)          |
|  - GdiScreenCapture (全屏/区域/窗口截图 + 全黑降级)        |
|  - CaptureFrameConverter (BGRA 帧 <-> GDI+ 位图)          |
|  - VirtualKeyConverter (VirtualKey <-> WinForms Keys)      |
|  - Win32HotkeyManager (全局热键注册与派发)                 |
|  - WindowsInputSimulator (SendInput / PostMessage 模拟)   |
|  - LowLevelHookManager (键鼠低级钩子 + 有界 Channel)        |
|  - WinEventHookManager (窗口生命周期事件 + 白名单订阅)     |
+-----------------------------------------------------------+
                               |
+-----------------------------------------------------------+
|               GameTools.Win32 (底层适配层)                |
|  - User32 / Gdi32 / Kernel32 / Ntdll / DwmApi             |
|  - OSVersionHelper (RtlGetVersion 真实版本探测)            |
|  - IOsVersionProvider (版本能力契约，可注入测试)            |
|  - WindowHelper (窗口边界与枚举、绘制尺寸区分)             |
|  - ProcessInfoCache (进程名缓存)                           |
|  - SafeGdiObjectHandle / SafeGdiDcHandle / SafeWindowDcHandle|
+-----------------------------------------------------------+
                               |
+-----------------------------------------------------------+
|          GameTools.Core (netstandard2.0 契约层)            |
|  - 接口: IScreenCapture, IHotkeyManager, ITextInput...     |
|  - 值对象: VirtualKey, CaptureBounds, CaptureFrame         |
|  - 实体: WindowInfo, CaptureResult, 事件参数               |
|  - 不依赖 WinForms / System.Drawing / Win32                |
+-----------------------------------------------------------+
```

依赖方向：`Core` → `Win32` → `Infrastructure` → `App`，`Tests` 依赖三者。禁止反向依赖与循环依赖。

**契约层平台无关化**：契约不得出现 `System.Windows.Forms.Keys`、`System.Drawing.Bitmap`、`System.Drawing.Rectangle` 等框架类型。WinForms 与 GDI+ 依赖收口到 `Infrastructure` 与 `App`，转换器位于边界处。

**目标框架矩阵**：

| 工程 | 目标框架 | 说明 |
| --- | --- | --- |
| `GameTools.Core` | `netstandard2.0` | 纯契约与值对象，不参与多目标构建 |
| `GameTools.Win32` | `net8.0-windows`;`net48` | Win32 P/Invoke 与原生结构 |
| `GameTools.Infrastructure` | `net8.0-windows`;`net48` | 平台能力实现，含 UI Automation 与 GDI+ |
| `GameTools.App` | `net8.0-windows`;`net48` | WPF 应用宿主 |
| `GameTools.Tests` | `net8.0-windows`;`net48` | 自动化测试 |

除 `Core` 为 `netstandard2.0` 外，其余四个工程均为多目标 `net8.0-windows;net48`：前者面向 Windows 10 及以上，后者面向 Windows 7 SP1 / 8 / 8.1。`Core` 刻意不参与多目标，以保证契约层不被任一 UI 框架或 .NET 版本绑定。

## 2. 核心模块详细设计

### 2.1 后台消息泵 (BackgroundMessagePump)
- **实现原理**：专用 STA 线程上创建 `HWND_MESSAGE` 消息窗口并运行 `Application.Run`。
- **线程亲和性**：全局热键注册（`RegisterHotKey`）、低级钩子安装（`SetWindowsHookEx`）与 `WinEventHook` 的创建与销毁**全部**经本泵调度。Win32 要求这些 API 在特定线程上调用，且在该线程读取 `LastError`。
- **生命周期**：
  - `Start()`：原子化保证并发调用只创建一个消息循环；启动异常经 `TaskCompletionSource` 回传调用方，而非仅写 Trace。
  - `WndProc`：处理 `WM_EXECUTE_ACTION`（队列驱动）与注册的其它消息过滤器。**过滤器异常不得中断消息循环**。
  - `Stop()`：先退出 `ApplicationContext` 使循环返回，再投递退出消息兜底；清空待执行队列，避免等待方永久挂起。
- **同步调度**：提供带超时与取消的 `InvokeFunc`。取消检查前置到入队之前；等待超时抛出 `TimeoutException` 而非无限等待。
- **设计权衡**：动作在泵线程内串行执行，耗时操作（截图、输入模拟）不得通过同步调度提交。

### 2.2 屏幕与窗口截图 (GdiScreenCapture)
- **API 选取与分支策略**：
  - 全屏与区域：`GetDC(IntPtr.Zero)` + `BitBlt`（含 `CAPTUREBLT` 以捕获分层窗口）。
  - 窗口：`PrintWindow` 按标志位序列尝试，任一步成功且内容非全黑即接受。
- **标志位序列**：Windows 8.1 及以上先试 `PW_RENDERFULLCONTENT`，随后 `PW_DEFAULT`，最后 `PW_CLIENTONLY`。
- **全黑检测**：`PrintWindow` 返回 true 但内容全黑时按失败处理并继续降级——硬件加速窗口在旧系统与部分驱动组合下会出现该现象。判定采用抽样（步长 4，阈值 8）以控制开销。
- **边界语义（易错点）**：
  - `GetWindowBounds` 返回**可见边界**：Windows 8.1+ 用 DWM 扩展帧边界（排除不可见调整边框）。
  - `GetPrintWindowSize` 返回**绘制尺寸**：一律按 `GetWindowRect` 完整外框。
  - 二者不可混用：`PrintWindow` 按完整外框绘制，若用 DWM 边界分配位图会导致右侧与底部被裁切。
- **版本探测**：统一经 `IOsVersionProvider`，net48 走 `ntdll!RtlGetVersion`。**不得使用 `Environment.OSVersion`**：net48 下缺少 `supportedOS` 清单声明时会被 shim 截断为 6.2，导致 `PW_RENDERFULLCONTENT` 在 Win10/11 上被错误禁用。
- **资源安全**：GDI 对象以安全句柄包裹，`SelectObject` 的原对象在 `finally` 中还原；中间位图用完立即释放。

### 2.3 全局快捷键管理器 (Win32HotkeyManager)
- **实现机制**：以消息泵窗口为 `hWnd` 调用 `RegisterHotKey`，注册与注销均经消息泵同步调度。
- **修饰键**：`MOD_ALT(1)`、`MOD_CONTROL(2)`、`MOD_SHIFT(4)`、`MOD_WIN(8)`、`MOD_NOREPEAT(0x4000)`。**`KeyModifiers` 枚举值与 `MOD_*` 位对齐是隐式契约**，须由测试保护。
- **冲突分类**：`ERROR_HOTKEY_ALREADY_REGISTERED(1409)` 区分「被系统或其他进程占用」；本进程内重复注册另行检测并给出可区分诊断。
- **注销顺序**：先原生注销、成功后再移除状态，避免注销失败时丢失可重试的信息。
- **自定义消息**：自定义消息置于 `WM_APP`(0x8000) 区间，避开第三方库常用的 `WM_USER`(0x0400~0x07FF)。

### 2.4 文本与按键模拟 (WindowsInputSimulator)
- **前台注入（`SendInput`）**：
  - Unicode 文本：`KEYEVENTF_UNICODE`，`wScan` 为 UTF-16 码元。
  - **代理对（易错点）**：非 BMP 字符须按「低代理项 Down、高代理项 Down、低代理项 Up、高代理项 Up」成组投递，且**组内不得插入字符间延迟**。逐码元独立 down/up 且中间有延迟时，多数目标应用会丢弃孤立代理项并输出替换字符。
  - 按键与组合键：修饰键按下失败时必须补偿已按下的键，避免修饰键粘连。
- **后台投递**：
  - `WM_KEYDOWN` / `WM_KEYUP` 的 `lParam` 须置重复次数 1、第 30 位标记按下；`WM_KEYUP` 另置第 31 位；扩展键另置第 24 位。
  - 后台按键须补发 `WM_CHAR`，否则多数控件不产生实际字符输入。
  - `WM_SETTEXT` 使用 `SendMessageTimeout`（2 秒），避免目标进程挂起导致调用方无限阻塞。
- **返回值检查**：`SendInput` / `PostMessage` 返回 0 表示被 UIPI 拒绝或数组被锁定，必须转化为可诊断错误而非静默失败。

### 2.5 钩子监听 (LowLevelHookManager & WinEventHookManager)
- **低级输入钩子**：
  - `SetWindowsHookEx(WH_KEYBOARD_LL/WH_MOUSE_LL, proc, hMod, 0)`，安装与卸载经消息泵执行。
  - **防 GC 机制**：委托由实例字段强引用持有并 `GCHandle.Alloc` 固化。字段强引用即已足够保活；`GCHandleType.Normal` 不提供固定，`design.md` 不应将其表述为「固化防 GC」。
  - **超时约束**：回调执行时间受 `LowLevelHooksTimeout` 限制（Windows 默认 3000ms，超时静默卸载钩子）。本实现设为 1000ms 以尽早暴露超时。
  - **回调路径零阻塞、零分配**：载荷为 `readonly record struct`，回调内仅做结构体读取与有界队列 `TryWrite`，随即 `CallNextHookEx` 返回。
  - **有界队列**：容量 4096、`FullMode = DropOldest`，丢弃计数可查询。健康状态丢弃率阈值纳入验收。
  - **过滤目标独立**：键盘与鼠标的目标进程相互独立，后启动者不覆盖先启动者。
  - **采样时机**：前台窗口与进程在事件入队时采样，不在消费时采样，避免积压或焦点切换导致归属错判。
- **WinEventHook**：
  - `SetWinEventHook(eventMin, eventMax, IntPtr.Zero, proc, targetPid, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS)`。
  - **白名单订阅**：按事件类型逐个注册（创建、销毁、显示、隐藏、重排、焦点、选中、改名、位置变化、前台切换、最小化起止），避免 `EVENT_MIN`~`EVENT_MAX` 全量订阅引入无关事件风暴。
  - **目标 PID 校验**：必须为正整数；`0` 在原生语义中表示监听整个桌面，与「指定进程」契约不符，必须拒绝。
  - **元数据回填**：销毁类事件的 HWND 可能已失效或为空，此时以配置的目标 PID 回填。
  - 卸载失败时保留委托根，防止系统回调到已回收委托。

### 2.6 进程信息缓存 (ProcessInfoCache)
`Process.GetProcessById` 每次调用都打开进程对象并读取路径，属重量级操作且在目标退出时抛异常。钩子与窗口枚举在高频场景下逐事件查询会显著消耗资源，因此按 PID 缓存结果，容量上限 1024，进程重启导致 PID 复用时需主动 `Clear()`。

## 3. 应用宿主设计 (GameTools.App)
- **技术栈**：WPF + Prism 9（MVVM 与 DryIoc 容器）+ NLog 5.4。
- **UI 框架隔离**：App 启用 `UseWPF` 并显式关闭 `UseWindowsForms`；`Infrastructure` 保留 WinForms（`NativeWindow`、`ApplicationContext`、`SystemInformation` 依赖）。全局 `UseWindowsForms=true` 会给 WPF 应用引入无关依赖并触发 WFAC010 诊断。
- **容器注册**：核心服务（消息泵、单实例锁、截图、热键、输入、钩子）注册为单例；`HotkeyService` 需 UI 调度器，在 Shell 创建时以工厂方式注册。
- **服务层职责**：包裹底层能力并处理线程与文件系统布局——截图结果落盘、热键回调切回 UI 线程、进程枚举去重。
- **宿主模式**：交互窗口、仅托盘（`--tray`）、无头（`--headless`）。多模式同时声明时 `--headless` 优先级最高。未知参数不得改变行为，避免参数拼写错误导致意外无头运行。
- **日志**：NLog 输出滚动文件（单文件 5MB、保留 10 个历史文件）；`TraceLog` 将库层 `Trace.WriteLine` 桥接至统一日志，避免日志框架下沉到库层。
- **事件流上限**：钩子事件与日志列表设上限（500 / 800 条），超出后丢弃最旧条目，避免长时间运行内存无界增长。事件入队经 UI 调度器切回界面线程。

## 变更记录

| 时间 | 变更摘要 |
| --- | --- |
| 2026-10-08 19:10:00 +08:00 | 依据 S1–S8 实现重写设计文档：明确契约层平台无关化与转换器位置；补充钩子消息线程亲和性、`RtlGetVersion` 版本探测、窗口边界与绘制尺寸区分、全黑降级、代理对投递规则、键消息 Win32 约定、有界队列与采样时机、白名单订阅与 PID 校验等实现级约束；新增第 3 节应用宿主设计。 |
| 2026-10-08 15:58:00 +08:00 | 创建详细设计规范，制定各个子模块架构契约与关键技术实现细节。 |