# GameTools

Windows 桌面底层交互工具库与宿主应用：后台常驻与消息泵、屏幕与窗口截图、全局快捷键、前后台文本与按键模拟、全局输入钩子与跨进程窗口事件监听。

纯 .NET 实现，通过 P/Invoke 调用 Win32 API，**不依赖驱动、不注入其他进程**。

| 项目 | 值 |
| --- | --- |
| 支持系统 | Windows 7 SP1 / 8 / 8.1 / 10 / 11 |
| 目标框架 | `net8.0-windows`（Win10/11）、`net48`（旧系统） |
| 最低工具链 | Visual Studio 2022 17.8、.NET SDK 8.0.100、NuGet 6.2 |
| 应用宿主 | WPF + Prism 9 + NLog 5.4 |
| 语言版本 | C# 12，启用 Nullable |
| 许可 | 见仓库根目录许可声明 |

> **旧系统验证状态**：Windows 7 / 8 / 8.1 尚未在真机验证。兼容性结论基于官方支持矩阵与特性探测设计，不构成实机验证结论。已验证范围为 Windows 10 及以上的双目标编译与自动化测试。

---

## 快速开始

### 构建与测试

```bash
dotnet build GameTools.sln -c Release
dotnet test GameTools.sln -f net8.0-windows
dotnet test GameTools.sln -f net48
```

### 运行

```bash
# 交互模式（默认）：主窗口 + 托盘
GameTools.App.exe

# 仅驻留托盘
GameTools.App.exe --tray

# 无界面后台（按 Ctrl+C 退出）
GameTools.App.exe --headless
```

未知参数不会改变行为，因此参数拼写错误不会导致意外进入后台模式。

---

## 能力清单

| 能力 | 说明 |
| --- | --- |
| 后台消息泵 | 独立 STA 线程；全局热键、低级钩子、窗口事件的安装与卸载均在此线程完成 |
| 屏幕与窗口截图 | 全屏（支持多显示器虚拟桌面）、区域、后台窗口截图；PNG 与 BGRA 输出 |
| 全局快捷键 | Ctrl/Alt/Shift/Win 任意组合；占用诊断区分「本进程重复注册」与「被外部占用」 |
| 文本与按键模拟 | 前台 `SendInput` Unicode 输入（含 Emoji 代理对）、后台消息投递、`WM_SETTEXT` 写入 |
| 输入钩子 | `WH_KEYBOARD_LL` / `WH_MOUSE_LL` 免注入全局监听；键鼠目标独立；有界事件流 |
| 窗口事件 | `SetWinEventHook` 白名单订阅；纯托管，无需注入目标进程 |

---

## 目录结构

```
GameTools/
├─ GameTools.sln                     解决方案（经典格式，兼容 VS 2022 17.8）
├─ Directory.Build.props             统一构建设置（TFM、语言版本、分析器、DPI）
├─ Directory.Packages.props          中央包版本管理
├─ global.json                       SDK 版本固定
├─ .editorconfig                     编码与风格规范
├─ build/                            net48 编译垫片与全局 using
├─ src/
│  ├─ GameTools.Core/                契约与值对象（netstandard2.0，无平台依赖）
│  ├─ GameTools.Win32/               P/Invoke、版本探测、窗口与进程辅助
│  ├─ GameTools.Infrastructure/      五大能力实现
│  └─ GameTools.App/                 WPF 宿主（Prism + NLog）
├─ tests/
│  └─ GameTools.Tests/               自动化测试（96 项）
└─ Docs/
   ├─ GameTools_使用与运维手册.html    用户手册
   └─ Plans/Features/GameTools/GameToolsCore/   五段式文档（plan/requirements/design/implementation/tasks）
```

依赖方向：`Core` → `Win32` → `Infrastructure` → `App`，`Tests` 依赖三者。无反向依赖与循环依赖。

---

## 架构要点

### 契约层平台无关

`GameTools.Core` 目标框架为 `netstandard2.0`，不引用 WinForms 与 System.Drawing。平台相关类型以自有值对象表达：

| 值对象 | 替代 |
| --- | --- |
| `VirtualKey` | `System.Windows.Forms.Keys` |
| `CaptureBounds` | `System.Drawing.Rectangle` |
| `CaptureFrame` | `System.Drawing.Bitmap`（BGRA 像素） |

转换器（`VirtualKeyConverter`、`CaptureFrameConverter`）位于 `Infrastructure`，保持依赖方向单向。

### 版本探测不走 `Environment.OSVersion`

.NET Framework 目标缺少应用清单 `supportedOS` 声明时，`Environment.OSVersion` 在 Windows 8.1 及以上会被 shim 截断为 6.2，导致 `PrintWindow` 的 `PW_RENDERFULLCONTENT` 在 Win10/11 上被错误禁用。

版本能力统一经 `IOsVersionProvider`，底层调用 `ntdll!RtlGetVersion`。该接口可注入固定版本，使 Win7/8/8.1/10/11 全部分支可在单元测试中覆盖。

### 线程亲和性

Win32 要求特定 API 在特定线程调用：

- `RegisterHotKey` —— 必须在创建 HWND 的线程注册，并在该线程读取 `LastError`
- `SetWindowsHookEx`（低级钩子）—— 安装线程须持续运行消息循环
- `SetWinEventHook` / `UnhookWinEvent` —— 跨线程卸载会失败

因此上述 API 的调用全部经 `BackgroundMessagePump` 调度。

### 稳定性约束

| 约束 | 取值 |
| --- | --- |
| 同步调度超时 | 默认 5000ms，超时抛 `TimeoutException`，不允许无限等待 |
| 低级钩子超时 | `LowLevelHooksTimeout` = 1000ms（Windows 默认 3000ms 会静默卸载钩子） |
| 钩子事件队列 | 有界 4096，`DropOldest`，丢弃计数可查询 |
| 窗口事件队列 | 有界 2048，同上 |
| 钩子回调分配 | `readonly record struct`，回调路径零堆分配 |
| 日志 | 单文件 5MB，保留 10 个历史文件 |

---

## 二次开发

引用 `GameTools.Core`、`GameTools.Win32`、`GameTools.Infrastructure` 三个工程即可使用全部能力，无需引入宿主应用。

```csharp
var pump = new BackgroundMessagePump();
pump.Start();

var hotkeys = new Win32HotkeyManager(pump);
int id = hotkeys.RegisterHotkey(VirtualKey.F8, KeyModifiers.Control | KeyModifiers.Shift, OnTrigger);

var capture = new GdiScreenCapture();
using CaptureResult result = capture.CaptureFullScreen(allMonitors: true);
// result.Frame 为平台无关的 BGRA 像素帧

hotkeys.Dispose();
pump.Dispose();
```

注意：需先启动消息泵；单实例互斥由宿主负责；截图与输入模拟等耗时操作不应通过消息泵同步调度提交。

---

## 已知边界

**不支持**：进程注入、驱动级输入、任意窗口消息拦截、图像识别与 OCR、跨平台。

**权限限制**：输入注入受 Windows UIPI 约束。本项目以标准用户权限运行（不请求管理员），无法操作以管理员身份运行的窗口。

**未验证项**：Win7/8/8.1 真机运行、真实 Unicode 输入端到端、硬件加速窗口截图、GDI 句柄耐久曲线、待机 CPU 占用、非管理员与 UIPI 受限场景。完整缺口清单见 `Docs/Plans/Features/GameTools/GameToolsCore/requirements.md` 第 3 节。

---

## 文档

- 使用与运维手册：[`Docs/GameTools_使用与运维手册.html`](Docs/GameTools_使用与运维手册.html)
- 工具链兼容基线：`Docs/Plans/Features/GameTools/GameToolsCore/toolchain-compatibility.md`
- 五段式文档：`plan.md`、`requirements.md`、`design.md`、`implementation.md`、`tasks.md`

文档与代码同源维护，如发现不一致以 `Docs/Plans/Features/GameTools/GameToolsCore/` 下的文档为准。
