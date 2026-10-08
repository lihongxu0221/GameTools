# GameTools 架构与详细设计规范

## 1. 系统分层架构

```
+-----------------------------------------------------------+
|              GameTools.App (Demo / Host)                  |
|    - 控制台交互 / 后台托盘运行 / 综合功能集成展示           |
+-----------------------------------------------------------+
                              |
+-----------------------------------------------------------+
|           GameTools.Infrastructure (能力实现层)            |
|  - BackgroundMessagePump (STA 消息泵)                     |
|  - GdiScreenCapture (全屏 / 窗口截图 / Win7 降级适配)      |
|  - Win32HotkeyManager (全局热键注册与派发)                 |
|  - WindowsInputSimulator (SendInput / PostMessage 模拟)   |
|  - LowLevelHookManager (键盘鼠标低级钩子 + Channel 解耦)   |
|  - WinEventHookManager (进程窗口生命周期与焦点事件监听)    |
+-----------------------------------------------------------+
                              |
+-----------------------------------------------------------+
|               GameTools.Win32 (底层适配层)                |
|  - NativeMethods (User32, Gdi32, Kernel32, DwmApi)        |
|  - OSVersionHelper (Win7 ~ Win11 探测与特性支持矩阵)      |
|  - SafeGdiHandle / SafeGdiDcHandle (GDI 资源 RAII 封装)   |
|  - WindowHelper (窗口枚举、RECT 探测、DPI 感知适配)       |
+-----------------------------------------------------------+
                              |
+-----------------------------------------------------------+
|               GameTools.Core (抽象契约层)                 |
|  - 接口: IScreenCapture, IHotkeyManager, ITextInput...    |
|  - 实体: WindowInfo, CaptureResult, HotkeyDefinition...   |
|  - 事件: HotkeyTriggeredEventArgs, ProcessHookEventArgs...|
+-----------------------------------------------------------+
```

## 2. 核心模块详细设计

### 2.1 后台消息泵 (BackgroundMessagePump)
- **实现原理**：在专用 STA 线程中创建 `NativeWindow` 或通过 Win32 `CreateWindowEx` 创建 `HWND_MESSAGE`（仅消息窗口）。
- **生命周期**：
  - `Start()`：启动后台线程并等待窗口句柄有效（通过 `TaskCompletionSource` 信号通知）。
  - `WndProc`：捕获 `WM_HOTKEY`、自定义退出消息 `WM_USER + 1` 等。
  - `Stop()`：发送退出消息并安全终结消息循环。

### 2.2 屏幕与窗口截图 (GdiScreenCapture)
- **API 选取与分支策略**：
  - 全屏截图：`GetDC(IntPtr.Zero)` + `BitBlt`。
  - 窗口截图：调用 `PrintWindow(hWnd, hMemDC, flags)`。
  - **Win7 兼容判断**：
    - 若 `OSVersionHelper.IsWindows81OrGreater` 为 true，优先尝试 `flags = PW_RENDERFULLCONTENT (0x00000002)`。
    - 若调用返回 false、全黑位图，或在 Windows 7 下（`flags = 0` 或 `PW_CLIENTONLY = 1`），执行平滑降级。
    - 针对 Direct3D/硬件加速黑屏，提供 Desktop DC 裁剪策略回退。
- **资源安全**：
  - 使用 RAII 思想，在 `using (var dc = SafeGdiDcHandle.FromWindow(hWnd))` 和 `using (var bmp = SafeGdiBitmapHandle.Create(...))` 中确保无论发生何种异常，`DeleteDC`、`DeleteObject`、`ReleaseDC` 都能执行。

### 2.3 全局快捷键管理器 (Win32HotkeyManager)
- **实现机制**：
  - 利用后台消息窗口句柄，调用 `RegisterHotKey(hWnd, id, modifiers, vk)`。
  - 修饰键支持 `MOD_ALT (1)`, `MOD_CONTROL (2)`, `MOD_SHIFT (4)`, `MOD_WIN (8)`, `MOD_NOREPEAT (0x4000)`。
  - 内部维护 `Dictionary<int, HotkeyRegistration>`。
  - 收到 `WM_HOTKEY` 时，异步派发 `ThreadPool.QueueUserWorkItem`，避免阻塞消息线程。

### 2.4 文本与按键模拟 (WindowsInputSimulator)
- **双模输入**：
  - **前台注入 (`SendInput`)**：
    - Unicode 文本：设置 `KEYBDINPUT.dwFlags = KEYEVENTF_UNICODE (4)`，`wScan = ch`。无需关心操作系统当前输入法（拼音/五笔等），直接投递字符。
    - 按键与组合键：设置 `wVk` 虚拟键码，按顺序触发 KeyDown 和 KeyUp。
    - 延迟控制：支持配置 `TextDelayMilliseconds` 与随机 Jitter，防止过快输入导致宿主丢字。
  - **后台投递 (`PostMessage` / `SendMessage`)**：
    - `PostMessage(hWnd, WM_CHAR, (IntPtr)ch, IntPtr.Zero)`。
    - `SendMessage(hWnd, WM_SETTEXT, IntPtr.Zero, text)`。

### 2.5 钩子监听进程消息 (LowLevelHookManager & WinEventHookManager)
- **低级输入钩子 (Low-Level Hooks)**：
  - 调用 `SetWindowsHookEx(WH_KEYBOARD_LL, proc, hMod, 0)` 和 `SetWindowsHookEx(WH_MOUSE_LL, proc, hMod, 0)`。
  - **防 GC 机制**：将委托保存在私有静态引用中，并使用 `GCHandle.Alloc(proc, GCHandleType.Normal)` 固化，在 `Dispose()` 时显式 `Free()`。
  - **异步 Channel 队列**：回调内部仅构造轻量结构体，调用 `_channel.Writer.TryWrite(...)`，并立即调用 `CallNextHookEx` 返回。
  - **进程过滤**：独立的消费后台任务读取 Channel，调用 `GetForegroundWindow()` 获取前台窗口，并通过 `GetWindowThreadProcessId` 得到所属进程 ID。若配置了目标进程，则比对后过滤，触发 `ProcessInputMessageEvent`。
- **WinEventHook 进程事件监听**：
  - 调用 `SetWinEventHook(EVENT_MIN, EVENT_MAX, IntPtr.Zero, proc, targetPid, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS)`。
  - 纯托管监听目标进程窗口的产生、激活、移动与关闭。

## 变更记录
| 时间 | 变更摘要 |
| --- | --- |
| 2026-10-08 15:58:00 +08:00 | 创建详细设计规范，制定各个子模块架构契约与关键技术实现细节。 |
