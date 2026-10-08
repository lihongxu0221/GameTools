# GameTools 需求规格说明书

## 1. 业务与运行环境
- **开发框架**：.NET 8 (`net8.0-windows`)。
- **目标操作系统**：Windows 7 SP1 及更高版本（Win7 / Win8 / Win8.1 / Win10 / Win11）。
- **运行环境前置条件**：
  - Windows 7 必须安装 SP1 补丁。
  - 必须安装 Microsoft Visual C++ 2015-2022 Redistributable (Universal C Runtime, api-ms-win-crt-*)。
  - 建议安装 KB2533623 或 KB3063858、SHA-2 补丁（KB4474419/KB4490628）。

## 2. 功能性需求

### 2.1 后台进程 (Background Process)
- **REQ-BG-01**：支持作为无界面后台守护进程常驻运行，支持可选系统托盘驻留模式。
- **REQ-BG-02**：具备 Win32 消息泵机制（Message Pump），能够持续稳定接收分发系统消息（如 WM_HOTKEY）。
- **REQ-BG-03**：支持跨进程单实例互斥（Single Instance），避免多开冲突。
- **REQ-BG-04**：支持优雅启动、运行中配置变更与平稳退出清理。

### 2.2 屏幕与窗口截图 (Screenshot)
- **REQ-CAP-01**：支持抓取全屏（包含多显示器虚拟桌面）与自定义矩形区域。
- **REQ-CAP-02**：支持抓取指定窗口句柄（HWND）画面。
- **REQ-CAP-03**：支持后台窗口截图（无需窗口前置激活）。针对 Windows 7（不支持 PW_RENDERFULLCONTENT 0x02），自动使用稳定兼容参数；针对 Windows 8.1+ 自动启用增强捕获并在失败时平滑降级。
- **REQ-CAP-04**：严格保证 GDI DC 与 Bitmap 句柄即时释放，长周期高频截图无资源泄漏。
- **REQ-CAP-05**：支持输出为 System.Drawing.Bitmap、字节数组（BGRA/PNG）及直接保存本地文件。

### 2.3 全局快捷键 (Global Hotkeys)
- **REQ-HK-01**：支持注册与注销任意系统级全局快捷键（Ctrl / Alt / Shift / Win 任意组合 + 键码）。
- **REQ-HK-02**：支持快捷键被系统或其他进程占用时的冲突检测，返回明确诊断异常。
- **REQ-HK-03**：触发快捷键时采用线程池异步派发事件，不得阻塞底层 Win32 消息派发线程。

### 2.4 模拟文本与按键输入 (Text Input & Keystroke Simulation)
- **REQ-INP-01 (前台模式)**：基于 `SendInput` 实现，支持直接投递 Unicode 字符串（包括中文、Emoji、特殊字符），不受输入法状态干扰。
- **REQ-INP-02 (前台按键)**：支持模拟常用控制键（Enter、Tab、Esc、Backspace、方向键）及修饰组合键。
- **REQ-INP-03 (输入节奏)**：支持配置字符间模拟延时与随机抖动，贴合人类输入节奏。
- **REQ-INP-04 (后台定向模式)**：支持针对目标非激活窗口直接投递 `WM_CHAR` / `WM_KEYDOWN` / `WM_KEYUP` 消息。
- **REQ-INP-05 (控件文本)**：支持针对标准 Windows 文本编辑控件发送 `WM_SETTEXT` 快速写入。

### 2.5 钩子监听进程消息 (Hooks & Process Monitoring)
- **REQ-HKP-01 (低级输入监听)**：基于 `WH_KEYBOARD_LL` 与 `WH_MOUSE_LL` 实现免注入的全局键盘与鼠标监听。
- **REQ-HKP-02 (进程过滤)**：支持根据前台活动窗口的所属进程 ID / 进程名动态过滤，仅监控针对目标进程的用户交互。
- **REQ-HKP-03 (防回收与零阻塞)**：回调委托必须显式通过强引用与 `GCHandle` 保护，杜绝 GC 回收；回调内必须通过高性能管道（Channel）解耦异步处理，杜绝 Windows 钩子超时卸载。
- **REQ-HKP-04 (进程窗口事件监听)**：基于 `SetWinEventHook` 纯托管实现对指定目标进程的窗口创建、激活、移动与销毁事件监听。

## 3. 非功能性需求
- **稳定性**：CPU 待机占用低于 0.1%，异常路径具备恢复与保护机制。
- **兼容性**：Win7 SP1 ~ Win11 行为一致或具备平滑回退。
- **安全性**：无敏感数据泄漏，支持非管理员账户安全运行。

## 变更记录
| 时间 | 变更摘要 |
| --- | --- |
| 2026-10-08 15:58:00 +08:00 | 创建需求规格说明书，明确五大能力具体功能性与非功能性要求。 |
