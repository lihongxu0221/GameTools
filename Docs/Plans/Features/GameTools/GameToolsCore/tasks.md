# GameTools 任务分解与执行清单

## 1. 初始任务看板（阶段一至阶段八）

- [x] **TASK-01**：编写规范文档（plan.md, requirements.md, design.md, tasks.md, implementation.md）。
- [x] **TASK-02**：建立分层解决方案与各子工程文件（GameTools.slnx, GameTools.Core, Win32, Infrastructure, App, Tests）。
- [x] **TASK-03**：实现 Win32 底层 NativeMethods、OSVersionHelper、SafeHandle 等底层设施。
- [x] **TASK-04**：实现后台消息泵 BackgroundMessagePump 与单实例互斥锁 SingleInstanceLock。
- [x] **TASK-05**：实现屏幕与窗口截图 GdiScreenCapture（多显、区域、窗口后台 PrintWindow）。
- [x] **TASK-06**：实现全局快捷键管理器 Win32HotkeyManager（注册/注销、冲突检测、异步回调）。
- [x] **TASK-07**：实现前后台文本输入模拟 WindowsInputSimulator（SendInput Unicode 与 PostMessage WM_CHAR）。
- [x] **TASK-08**：实现钩子监听模块 LowLevelHookManager（防 GC 固化、Channel 异步管道、进程过滤）与 WinEventHookManager。
- [x] **TASK-09**：实现综合演示宿主 GameTools.App 与自动化单元测试 GameTools.Tests（13 项冒烟测试）。
- [ ] **TASK-10**：全量构建、测试运行、结果验证与文档更新。**进行中**：构建与既有测试通过，但复核确认验证覆盖不足以支撑「全功能验证完成」声明，剩余验收并入 TASK-16。

## 2. 改进任务看板（S0–S7）

> 依据 2026-10-08 两份复核报告（`Outputs/GameToolsCore/` 下审查记录）生成。每项任务仅在验收通过后勾选；涉及验证的任务须同时更新 `implementation.md` 验收日志。

### S0 工程治理与多目标框架（TASK-11）

- [x] **TASK-11.1**：新增 `Directory.Build.props`（统一 `TargetFrameworks=net8.0-windows;net48`、LangVersion、Nullable、版本号）。
- [x] **TASK-11.2**：新增 `Directory.Packages.props` 集中管理包版本，移除各 csproj 内散落版本。
- [x] **TASK-11.3**：新增 `global.json` 固定 SDK 基线为 8.0.100（`rollForward: latestMajor`），保证 VS 2022 17.8 内置 SDK 可构建且允许使用更高版本。
- [x] **TASK-11.4**：新增 `.editorconfig`（`charset = utf-8-bom`、命名与诊断规则），并将现有源文件统一为 UTF-8 BOM。
- [x] **TASK-11.5**：新增 net48 语言垫片（`IsExternalInit`、`RequiredMemberAttribute`、`CompilerFeatureRequiredAttribute`、`SetsRequiredMembersAttribute`）与手写 `GlobalUsings.cs`。
- [x] **TASK-11.6**：替换 6 处 net48 缺失 BCL 调用（`ObjectDisposedException.ThrowIf` x3、`ArgumentNullException.ThrowIfNull` x2、`string.Contains` 双参重载 x1）。
- [x] **TASK-11.7**：引入 `System.Threading.Channels` 与 `System.Runtime.InteropServices.RuntimeInformation` 包。
- [x] **TASK-11.8**：验证双目标构建 0 警告 0 错误且既有测试双目标通过。
- [x] **TASK-11.9**：解决方案改为经典 `GameTools.sln`（移除 `.slnx`），修正项目类型 GUID 与 tests 解决方案文件夹嵌套。
- [x] **TASK-11.10**：新增 `toolchain-compatibility.md`，记录 VS/SDK/MSBuild/NuGet 最低版本矩阵与新特性引入检查清单。
- [x] **TASK-11.11**：用 SDK 8.0.420 与 10.0.401 双版本验证经典 .sln 的构建与双目标测试。

### S1 平台一致性（TASK-12）

- [ ] **TASK-12.1**：新增 `IOsVersionProvider` 抽象，net48 走 `RtlGetVersion` 并以注册表 `CurrentBuildNumber` 兜底，net8 走 `Environment.OSVersion`。
- [ ] **TASK-12.2**：新增 `app.manifest`（supportedOS windows7 至 windows11、DPI awareness），并在 csproj 声明 `ApplicationManifest`。
- [ ] **TASK-12.3**：net8 侧设置 `ApplicationHighDpiMode` 与 net48 侧 manifest 语义对齐。
- [ ] **TASK-12.4**：统一窗口边界来源（DWM 扩展边界与 `GetWindowRect` 的 DPI 语义），修正注释与实现不一致。
- [ ] **TASK-12.5**：新增版本分支单测，覆盖 Win7/8/8.1/10/11 全部分支与降级策略。

### S2 钩子宿主线程统一（TASK-13）

- [ ] **TASK-13.1**：`LowLevelHookManager` 注入 `IBackgroundMessagePump`，安装与卸载全部在消息线程执行。
- [ ] **TASK-13.2**：`WinEventHookManager` 同上，并检查 `UnhookWinEvent` 返回值，卸载失败不得提前释放委托根。
- [ ] **TASK-13.3**：新增回调真实到达与线程归属断言测试（替代仅断言 Start 不抛异常）。

### S3 稳定性、资源与退出（TASK-14）

- [ ] **TASK-14.1**：消息泵 `InvokeFunc` 增加超时与取消；启动异常经 `TaskCompletionSource` 回传；`Start`/`Stop` 幂等与并发保护；`PostMessage` 结果检查；句柄跨线程内存屏障。
- [ ] **TASK-14.2**：`Main` 增加 try/finally；`ReadKey` 可被退出信号唤醒；托盘初始化等待完成；初始化失败回滚已装钩子。
- [ ] **TASK-14.3**：事件队列改为有界（容量 4096、FullMode、丢弃计数可观测）；`RawInputEvent` 与 `RawWinEvent` 改为 `readonly record struct`；进程名按 PID 缓存。
- [ ] **TASK-14.4**：停止完成屏障与幂等；停止后不再派发旧事件；订阅者内 `Dispose` 不自锁；消费循环检查取消。
- [ ] **TASK-14.5**：`RegisterMessageFilter` 改多订阅者；热键注销改为先原生后删状态；`Dispose` 增加完成屏障。
- [ ] **TASK-14.6**：输入投递检查 `SendInput`/`PostMessage` 返回值并产出可诊断结果（区分 UIPI 受限）；`WM_KEYUP` lParam 合规；扩展键标志；组合键异常补偿。
- [ ] **TASK-14.7**：修复 `SingleInstanceLock` 的 `AbandonedMutexException` 分支（改 `WaitOne(0)`），明确 `Local\` 会话语义。
- [ ] **TASK-14.8**：`SetWindowText` 改用 `SendMessageTimeout`；明确 `PrintWindow` 同步调用的隔离与生命周期策略。

### S4 功能缺口与契约修正（TASK-15）

- [ ] **TASK-15.1**：`GameTools.App` 技术栈替换为 WPF + Prism + NLog；可选宿主模式（交互窗口、仅托盘、无头）与 `args` 解析；运行中配置热更新管道。
- [ ] **TASK-15.2**：截图黑屏判定与降级原因回传（区分合法全黑画面与故障）；新增 BGRA 像素导出并定义 stride、alpha、行方向约定。
- [ ] **TASK-15.3**：输入节奏增加随机抖动配置与参数合法性校验；Unicode 文本按代理对成组投递且组内不插入延迟。
- [ ] **TASK-15.4**：钩子过滤支持进程名与在线更新；键鼠目标 PID 拆分；前台 PID 在事件发生时采样。
- [ ] **TASK-15.5**：WinEvent 目标 PID 校验（拒绝 0 与负数）；销毁事件用配置 PID 回填元数据；事件名映射补全（含位置变化事件）。
- [ ] **TASK-15.6**：修正消息循环 P/Invoke 契约（定义原生 `MSG`，`GetMessage` 返回 `BOOL`）。
- [ ] **TASK-15.7**：常量归位（自定义消息移入 `WM_APP` 区间、错误码入常量）；`KeyModifiers` 与 Win32 修饰键改显式映射表并加测试。
- [ ] **TASK-15.8**：清理误导性契约（`Handled` 语义澄清或移除）；补齐 `HotkeyDefinition` 或修正设计文档表述。

### S5 验证体系补强（TASK-16）

- [ ] **TASK-16.1**：自建测试宿主窗口、文本控件与辅助进程，替代读取真实桌面与监听真实输入。
- [ ] **TASK-16.2**：失败路径测试（无效 HWND、目标进程退出、UIPI 拒绝、队列溢出、泵线程崩溃、卸载失败）。
- [ ] **TASK-16.3**：并发竞态测试（重复启停、Dispose 与事件并发、订阅者内 Dispose、锁内阻塞）。
- [ ] **TASK-16.4**：资源耐久测试（连续截图 GDI 句柄增量、钩子长跑内存增长、队列积压有界）。
- [ ] **TASK-16.5**：双目标差异测试（版本探测、PNG 编码、热键与输入行为一致性）。
- [ ] **TASK-16.6**：补齐 `plan.md` 第 7.2 节列出的待确认阈值测量条件，或记录缺口并与需求方确认。

### S6 Core 去平台依赖（TASK-17）

- [ ] **TASK-17.1**：新增平台无关值对象 `CaptureBounds`、`InputPoint`、`VirtualKey`、`CapturePixelFormat`、`CaptureFrame`。
- [ ] **TASK-17.2**：`GameTools.Core` 目标框架改为 `netstandard2.0`，移除 `UseWindowsForms` 与 System.Drawing 依赖。
- [ ] **TASK-17.3**：调整 Core 接口与事件签名（截图矩形、像素帧、按键枚举与坐标）。
- [ ] **TASK-17.4**：Infrastructure 与 App 侧提供 `VirtualKey` 与按键映射、帧到 `Bitmap` 的转换适配。
- [ ] **TASK-17.5**：更新测试并执行全量回归；确认该提交可独立回退。

### S7 文档同步（TASK-18）

- [ ] **TASK-18.1**：`requirements.md` 技术前提改为多目标，补验收阈值与「旧系统未实机验证」声明。
- [ ] **TASK-18.2**：`design.md` 修正 GCHandle 表述，补钩子消息线程归属、`PrintWindow` 降级矩阵、依赖方向箭头。
- [ ] **TASK-18.3**：`implementation.md` 记录基线 SHA `20d5a4c`、功能分支与各阶段验收日志。
- [ ] **TASK-18.4**：`tasks.md` 按实际验证结果勾选，并撤回无证据的完成声明。
- [ ] **TASK-18.5**：用户手册 HTML 与修复后行为对齐（多目标说明、Unicode 与代理对、截图降级）。
### S8 应用宿主重写与交付文档（TASK-19）

- [ ] **TASK-19.1**：`Directory.Build.props` 改为按项目条件设置 UI 框架（App 启用 WPF 并关闭 WinForms；Infrastructure 保留 WinForms 以支持 `SystemInformation`、`NotifyIcon` 依赖的兼容路径）。
- [ ] **TASK-19.2**：新增 `Prism.DryIoc`、`NLog`、`NLog.Extensions.Logging` 依赖（版本写入 `Directory.Packages.props`，须同时支持 net48 与 net8.0-windows）。
- [ ] **TASK-19.3**：建立 Prism 应用骨架：`App.xaml`/`App.xaml.cs`（容器注册与模块装配）、`MainWindow`、ViewModel 基类约定。
- [ ] **TASK-19.4**：以 Prism 容器注册核心服务（消息泵、单实例锁、截图、热键、输入模拟、钩子管理器），替换原手工 `new`。
- [ ] **TASK-19.5**：接入 NLog，配置文件轮转与容量上限；替换现有 `Trace.WriteLine` 与 `Console.WriteLine` 诊断输出。
- [ ] **TASK-19.6**：实现可选宿主模式：交互窗口、仅托盘（`--tray`）、无头（`--headless`）。
- [ ] **TASK-19.7**：按 S4 修复项实现 UI 交互：截图预览与保存、窗口列表选择、热键管理、文本与按键模拟面板、钩子与进程过滤开关、实时日志视图。
- [ ] **TASK-19.8**：新增 `README.md`（项目定位、能力清单、环境要求、快速开始、构建与测试、目录结构、兼容性说明、许可与依赖）。
- [ ] **TASK-19.9**：重写 `Docs/GameTools_使用与运维手册.html`，覆盖 WPF 界面操作、命令行参数、日志位置与排障、部署与升级。
- [ ] **TASK-19.10**：双目标构建与测试验证，并执行 App 启动冒烟（仅验证进程可启动与容器可解析，不注入真实输入、不截取用户桌面）。

## 变更记录

| 时间 | 变更摘要 |
| --- | --- |
| 2026-10-08 19:10:00 +08:00 | 追加 S8 阶段（TASK-19）：`GameTools.App` 技术栈替换为 WPF + Prism + NLog，并新增 `README.md` 与使用与运维手册两项交付物。 |
| 2026-10-08 18:40:00 +08:00 | 补充 TASK-11.9 至 11.11：解决方案改为经典 `GameTools.sln` 以兼容 VS 2022 17.8 之前版本，`global.json` 基线放宽至 SDK 8.0.100 + latestMajor，新增 `toolchain-compatibility.md`；SDK 8.0.420 与 10.0.401 双版本构建 0 警告 0 错误、双目标各 13 项测试通过。 |
| 2026-10-08 18:15:00 +08:00 | S0 完成（TASK-11.1 至 11.8 全部勾选）：新增 Directory.Build.props、Directory.Packages.props、global.json、.editorconfig、build 目录 net48 垫片与全局 using、App manifest；5 个 csproj 去除重复属性并接入中央包管理；替换 6 处 net48 缺失 BCL 调用；52 个文本文件统一 UTF-8 BOM。双目标构建 0 警告 0 错误，13 项测试在 net8.0-windows 与 net48 下均通过。验证记录见 `Outputs/Features/GameTools/S0-工程治理与多目标_20261008-181500/`。 || 2026-10-08 17:45:00 +08:00 | 依据两份复核报告重排任务：撤回 TASK-10 的全功能验证完成勾选（复核确认验证覆盖不足），新增 S0–S7 改进任务看板（TASK-11 至 TASK-18，覆盖工程治理与多目标、平台一致性、钩子宿主线程、稳定性与退出、功能缺口、验证体系、Core 去平台依赖、文档同步）。 |
| 2026-10-08 16:35:00 +08:00 | 各阶段功能已实现，新增 13 个单元测试，编译构建全流程完成。 |
| 2026-10-08 15:58:00 +08:00 | 创建任务分解看板。 |