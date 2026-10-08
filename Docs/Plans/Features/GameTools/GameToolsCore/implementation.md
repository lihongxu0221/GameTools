# GameTools 实现记录与技术细节

## 1. 运行平台与依赖约定
- **目标框架**：多目标 `net8.0-windows;net48`（`GameTools.Core` 于 S6 阶段收敛为 `netstandard2.0`）。
- **旧系统交付形态**：`net48` 目标面向 Windows 7 SP1 / 8 / 8.1，走自包含部署（不支持单文件发布与 ReadyToRun），需前置 VC++ 2015-2022 运行库。
- **系统支持**：Windows 7 SP1+ (Windows 7 / 8 / 8.1 / 10 / 11)。
- **语言标准**：C# 12，启用 Nullable 检查。
- **工具链兼容下限**：Visual Studio 2022 17.8 / .NET SDK 8.0.100 / MSBuild 17.8 / NuGet 6.2+；解决方案为经典 `GameTools.sln`（不使用 `.slnx`）。

## 2. 关键设计实现要点
1. **Windows 7 兼容机制**：
   - `PrintWindow` 在 Windows 7 下如果传入 `PW_RENDERFULLCONTENT (0x02)` 会直接失败。在 `OSVersionHelper` 中检测 Windows 版本，若主版本为 6 且次版本为 1（Win7），严格使用 `PW_DEFAULT (0)` 或 `PW_CLIENTONLY (1)`。
2. **后台消息循环**：
   - 在独立 STA 线程中运行 `ApplicationContext` 或纯 Win32 消息泵 `GetMessage`，对外提供句柄 `MessageWindowHandle`，支持全局热键与钩子驱动。
3. **低级钩子防止 GC 收集与系统超时**：
   - 回调委托由静态字段持有并由 `GCHandle.Alloc` 锁定。
   - 回调内部仅通过 `Channel<T>.Writer.TryWrite` 将事件投递至通道，立刻调用 `CallNextHookEx` 返回，零阻塞保证系统稳定性。
4. **Unicode 文本输入**：
   - 利用 `SendInput` 的 `KEYEVENTF_UNICODE` 标志，绕过宿主输入法，确保多语言与符号精准输出。
5. **Win32 RegisterHotKey 线程亲和性保护**：
   - Windows 要求 RegisterHotKey 必须在创建 HWND 的同一个线程执行（否则返回错误码 1408 ERROR_WINDOW_OF_OTHER_THREAD）。通过 `BackgroundMessagePump` 内置的 `InvokeFunc` 将热键注册调度切换到宿主 STA 线程内部，彻底根治跨线程热键注册失败问题。

## 3. Git 功能分支记录
- **功能分支**：`lihongxu/gametools-multi-target`（多目标改造分支，由 `master` 于基线 `20d5a4c` 切出）
- **基线提交**：`20d5a4c`（feat: 初始化 GameTools 底层交互系统与 Wiki 手册）
- **基线来源**：`master`，已同步 `origin/master`；`lihongxu/gametools-core` 合并结果为 Already up to date
- **目标框架**：多目标 `net8.0-windows;net48`（`GameTools.Core` 于 S6 阶段收敛为 `netstandard2.0`）

## 4. 阶段验收日志

### S0 工程治理与多目标框架（2026-10-08 18:15）

| 验证项 | 结果 |
| --- | --- |
| 双目标构建 dotnet build GameTools.slnx | 成功，5 个工程，0 警告 0 错误，4.83s |
| net8.0-windows 测试 | 13 通过 / 0 失败 / 0 跳过 |
| net48 测试 | 13 通过 / 0 失败 / 0 跳过 |

关键实现要点：

1. 统一构建设置：Directory.Build.props 承载 TFM、LangVersion、Nullable、分析器与 DPI 感知；Directory.Packages.props 集中包版本并开启传递依赖锁定；global.json 固定 SDK 10.0.401（.slnx 格式需 9.0.200 以上 SDK）。
2. net48 语言垫片：build/Net48Shims.cs 补齐 IsExternalInit、RequiredMemberAttribute、CompilerFeatureRequiredAttribute、SetsRequiredMembersAttribute 等编译器内省类型，使 init、record、required 成员在 net48 下可用，与 net8 共用同一份源码；build/Net48GlobalUsings.cs 等价复现 SDK 隐式全局 using（net48 不支持 ImplicitUsings）。
   易踩点：RequiredMemberAttribute 位于 System.Runtime.CompilerServices，而非 System.Diagnostics.CodeAnalysis。
3. net48 缺失 API 替换：ObjectDisposedException.ThrowIf 与 ArgumentNullException.ThrowIfNull 为 .NET 6/8 新增，改为显式 if 抛出并使用 nameof；string.Contains(string, StringComparison) 双参重载改为 IndexOf(...) 大于等于 0。均为双目标兼容写法，无需条件编译。
4. 平台依赖：net48 引入 System.Threading.Channels 与 System.Runtime.InteropServices.RuntimeInformation 两个官方 netstandard2.0 包；System.Drawing 与 System.Windows.Forms 在 net48 属 BCL，无需额外包。
5. DPI 感知按 TFM 分离：net8.0-windows 由 ApplicationHighDpiMode=PerMonitorV2 提供（WinForms 分析器 WFAC010 禁止在 manifest 中配置 DPI），net48 需依赖 manifest 声明，该部分留待 S1 按条件引入。首轮构建的 2 条 WFAC010 警告由此消除。

未验证项：Windows 7 SP1 / 8 / 8.1 真机运行、Win7 自包含交付形态、跨目标 PNG/BGRA 字节一致性。旧系统兼容性结论基于官方支持矩阵与特性探测设计，不构成实机验证结论。

### S0 补充：工具链向后兼容改造（2026-10-08 18:40）

| 项 | 变更前 | 变更后 |
| --- | --- |
| 解决方案 | GameTools.slnx（XML 格式） | GameTools.sln（经典 Format Version 12.00），含 src 与 tests 两个解决方案文件夹 |
| global.json | version 10.0.401，rollForward latestFeature | version 8.0.100，rollForward latestMajor，allowPrerelease false |

改造原因：`.slnx` 仅 VS 2022 17.13+ / SDK 9.0.200+ 原生支持；原 `global.json` 锁定 10.0.401 会使 VS 17.8 内置的 SDK 8.0.x 直接构建失败。放宽后 `latestMajor` 允许任意更高版本 SDK，老版本 VS 可正常构建，且新 SDK 仍可使用。

新增 `toolchain-compatibility.md` 记录最低版本矩阵、约束来源与「新特性引入前检查清单」。

验证（同一份源码与工程配置，仅 SDK 不同）：

| SDK | 解决方案 | 构建 | net8.0-windows | net48 |
| --- | --- |
| 8.0.420 | GameTools.sln | 0 警告 0 错误 | 13 通过 | 13 通过 |
| 10.0.401 | GameTools.sln | 0 警告 0 错误 | 13 通过 | 13 通过 |

顺带修正 `GameTools.sln` 两处 CLI 生成缺陷：C# 项目类型 GUID 由旧的 `FAE04EC0` 改为 SDK-style 正确的 `9A19103F`；Tests 项目由误置于 src 文件夹改为 tests 文件夹。

未验证：VS 2022 17.8 图形界面加载与调试体验（本机无该版本 VS），以 SDK 8.0.420 命令行等价验证。


### S8 技术选型决策：WPF + Prism + NLog（2026-10-08 19:10）

用户指定 `GameTools.App` 采用 WPF + Prism + NLog。实施前先做兼容性实测，避免引入抬高工具链下限的依赖。

实测结论（SDK 8.0.420，即 VS 2022 17.8~17.11 区间）：

| 验证项 | 结果 |
| --- | --- |
| WPF 多目标（net8.0-windows + net48，UseWPF） | 0 警告 0 错误，两目标产物均生成 |
| Prism.DryIoc 9.0.537 | 双目标还原与编译通过；传递依赖 Prism.Core/Events/Wpf 9.0.537、Prism.Container.Abstractions/DryIoc 9.0.106 |
| NLog 5.4.0 | 双目标还原通过，支持 net35 至 net481 与 netstandard2.0 |
| NLog.Extensions.Logging 5.3.8 | 双目标还原通过 |

依赖版本决策：NLog 使用 5.4.0（5.3.8 在源上不可用，会触发 NU1603 近似匹配警告，故直接锁定 5.4.0 以保证零警告）。

兼容性说明：Prism 9 的核心库面向 netstandard2.0 与 .NET Framework 4.6/4.7，WPF 平台库面向 net472 及以上，net48 与 net8.0-windows 均在支持范围内，不抬高 VS 2022 17.8 下限。

架构影响：现有 `Directory.Build.props` 全局设置 `UseWindowsForms=true`，WPF 应用引入该属性会带来无关 WinForms 依赖并触发 WFAC010 诊断。因此改为按项目条件设置：App 启用 WPF 且显式关闭 WinForms；Infrastructure 因 `WindowsInputSimulator` 与 `BackgroundMessagePump` 依赖 `System.Windows.Forms`（`Keys`、`NativeWindow`、`ApplicationContext`）继续保留 WinForms。


### S1-S8 实现与验证（2026-10-08 19:00）

提交 `46684df`。范围：平台一致性、钩子宿主线程、稳定性与退出、功能缺口、验证体系、Core 去平台依赖、应用宿主重写。

关键实现要点：

1. **版本探测去 shim**：`OSVersionProvider` 改用 `ntdll!RtlGetVersion`，抽象为 `IOsVersionProvider` 便于注入。实测在当前系统返回 Build 26200（真实构建号），对比 shim 截断值 6.2 可确认改造生效。
2. **窗口边界语义拆分**：`GetWindowBounds` 返回可见边界（DWM 扩展边界，排除不可见边框），`GetPrintWindowSize` 返回绘制尺寸（完整外框）。`PrintWindow` 按完整外框绘制，混用会导致右侧与底部被裁切。
3. **钩子宿主线程统一**：两个 Hook Manager 注入 `IBackgroundMessagePump`，安装与卸载均在消息线程完成；`UnhookWinEvent` 跨线程会失败，卸载失败时保留委托根。
4. **有界事件流**：低级钩子容量 4096、WinEvent 2048，均为 `DropOldest` 并暴露丢弃计数。载荷改为 `readonly record struct`，消除回调路径堆分配。
5. **消息泵超时与取消**：`InvokeFunc` 提供带超时与取消重载；取消检查前置到入队之前；停机后调用立即失败而非永久等待。
6. **输入正确性**：代理对成组投递；`WM_KEYUP` 的 `lParam` 按 Win32 约定置位并补发 `WM_CHAR`；`WM_SETTEXT` 改用 `SendMessageTimeout`；`SendInput` 与 `PostMessage` 返回值检查。
7. **Core 平台无关化**：收敛为 `netstandard2.0`，新增 `VirtualKey`、`CaptureBounds`、`CaptureFrame` 三个值对象；契约不再出现 `Keys`、`Bitmap`、`Rectangle`。转换器（`VirtualKeyConverter`、`CaptureFrameConverter`）下沉至 Infrastructure。
8. **应用宿主重写**：WPF + Prism 9 + NLog 5.4，支持交互/仅托盘/无头三种模式。UI 框架按项目隔离：App 用 WPF 并关闭 WinForms，Infrastructure 保留 WinForms。
9. **P/Invoke 契约修正**：`GetMessage` / `TranslateMessage` / `DispatchMessage` 原声明误用 `System.Windows.Forms.Message` 作为 `MSG` 参数且返回类型为 `sbyte`；改为原生 `MSG` 结构与 `BOOL` 返回值。
10. **测试体系**：从 13 项冒烟扩充为 75 项，覆盖版本分支（Win7~Win11 全覆盖）、消息泵超时与取消、热键冲突分类、单实例锁边界、ViewModel 参数校验与列表上限。

验证结果：

| SDK | 构建 | net8.0-windows | net48 |
| --- | --- |
| 10.0.401 | 0 警告 0 错误 | 75 通过 | 75 通过 |
| 8.0.420 | 0 警告 0 错误 | 75 通过 | 75 通过 |

headless 启动冒烟通过，日志正确写出且版本信息为真实构建号。

未验证项：Windows 7/8/8.1 真机运行、真实 Unicode 输入端到端、硬件加速窗口截图与全黑降级、GDI 句柄耐久曲线、待机 CPU 占用、WPF 界面交互、非管理员与 UIPI 受限场景。旧系统兼容性结论基于官方支持矩阵与特性探测设计，不构成实机验证结论。

### S9 应用宿主运行期缺陷修复（2026-10-08 21:40）

用户实测反馈「无法获取窗口句柄，也无法获取到进程」，管理员模式下同样复现。管理员权限只会放宽访问而不会收紧，故先排除权限假设，再用探针逐层实测：底层 `EnumWindows` 回调 836 次、可见窗口 24 个、`FindTopLevelWindows` 返回 13 个、进程 422 个，确认底层枚举完全正常，问题定位在应用层装配。

#### 根因

| 序号 | 缺陷 | 位置 | 表现 |
| --- | --- | --- | --- |
| 1 | `MainWindow.DataContext` 从未被赋值 | `App.CreateShell` | `MainWindow.xaml` 全部 `{Binding ...}` 失效，窗口与进程下拉框恒为空 |
| 2 | `MainViewModel` 与三个应用层服务未注册到容器 | `App.RegisterTypes` | 视图模型不可解析，界面无数据来源 |
| 3 | `Dispatcher` 未注册 | `App.RegisterTypes` | `HotkeyService` 依赖注入 `Dispatcher`，容器无法自动解析静态属性，一旦解析即抛 `ContainerResolutionException` |
| 4 | 进程枚举按 `MainWindowHandle != 0` 过滤 | `HookService.ListProcesses` | 422 个进程被削减至 11 个，控制台程序与后台服务全部消失 |

缺陷 1 与 4 相互独立，各自单独即可导致一个列表为空，叠加后即用户观察到的「两者都取不到」。缺陷 3 在修复缺陷 2 后由探针暴露：补齐注册后 `CreateShell` 解析 `MainViewModel` 时因 `Dispatcher` 无法解析而失败。

#### 修复

- `CreateShell` 完整装配：解析 `MainWindow` 与 `MainViewModel`、显式赋值 `DataContext`、桥接钩子与窗口事件到界面事件流、接管 `ShutdownRequested`、按运行模式决定是否最小化。
- `RegisterTypes` 补齐 `MainViewModel`、`IScreenCaptureService`、`IInputService`、`IHookService`、`IHotkeyService` 注册，并以 `RegisterSingleton<Dispatcher>(factoryMethod: ...)` 绑定 `Application.Current.Dispatcher`。使用工厂而非实例注册，确保取值时 `Application.Current` 已完成初始化。
- `ProcessChoice` 新增 `HasMainWindow` 标记；`ListProcesses` 不再按是否有窗口过滤，仅将该标记用于显示提示（`[无窗口]`）与排序（有窗口优先、组内按名），并对 `MainWindowHandle` 读取单独容错，避免跨进程探测因权限失败而丢弃整个进程项。

#### 验证

保真探针直接实例化真实 `GameTools.App.App`，与 `EntryPoint` 完全相同的 `InitializeComponent` → `Run` 加载顺序：

| 指标 | 修复前 | 修复后 |
| --- | --- | --- |
| 主窗口 `DataContext` | `null` | `MainViewModel` |
| `Windows.Count` | 0 | 14 |
| `Processes.Count` | 11 | 424 |
| 状态栏 | 空 | 已发现 14 个可见窗口 |

探针首轮曾在自建的 `ProbeApp`（无 XAML 后置类）上报 `StaticResource` 解析失败，经判定为探针自身缺陷——未加载 `App.xaml` 资源字典，故 `ToolButton` 等 5 个静态资源键不可见；改用真实 `App` 类后消失，非产品缺陷。此结论记录在此，避免后续复现时误判。

新增 `ProcessEnumerationTests` 8 项回归测试，覆盖：无窗口进程不被过滤、进程 ID 唯一、组内名称升序、进程名非空、有窗口优先、两种 `Display` 文案、`HasMainWindow` 默认值。测试用空实现钩子管理器以显式接口事件访问器满足依赖（避免 CS0067 告警）。

| SDK | 构建 | net8.0-windows | net48 |
| --- | --- | --- | --- |
| 10.0.401 | 0 警告 0 错误 | 83 通过 | 83 通过 |

headless 模式经有界启动验证：进程存活 6 秒、无 stderr 输出，符合「长驻等待 Ctrl+C」的设计语义。

#### 附带修复

`implementation.md` 曾因脚本写入方式缺陷导致变更记录行被注入到正文的 5 处表格中（同一批次记录重复 7 次，部分行尾部粘连 `| --- |`），已按「`## 变更记录` 之前的正文不应出现时间戳表格行」规则清除 12 行并复核表格结构。

未验证项：WPF 数据绑定在真实可见窗口中的最终渲染效果仍未在真机确认，探针只验证了 `DataContext` 赋值与集合填充，未覆盖布局与主题呈现。

### S10 界面布局与手动刷新（2026-10-08 22:10）

用户反馈两项问题：左侧面板多处按钮与下拉框显示不全；窗口与进程下拉框缺少手动刷新按钮。

#### 根因

| 序号 | 问题 | 根因 |
| --- | --- | --- |
| 1 | 按钮与下拉框被裁切 | 14 处横向 `StackPanel` 不换行，内容宽度超出左列固定 620px 后被直接裁掉。实测「快捷键」标签页需 728px、「钩子监听」需 734px，而可用宽度仅约 600px |
| 2 | 右侧日志面板被挤压 | `Grid` 只声明 2 列，但 `GridSplitter` 放在 `Grid.Column="1"`（即 `*` 列）、右侧面板放在 `Grid.Column="2"`（隐式自动列）。分隔条吞掉全部剩余空间，日志面板只按内容取宽 |
| 3 | `GridSplitter` 无法拖拽 | 列宽为纯固定值，无 Min/Max 约束，拖拽无实际效果 |
| 4 | 长窗口标题撑破弹出列表 | 依赖 `DisplayMemberPath`，无宽度上限约束 |
| 5 | 缺少手动刷新 | `RefreshWindowsCommand` / `RefreshProcessesCommand` 已存在但未绑定任何按钮；且刷新整体替换集合会使选中项回落到 `null`，用户每次刷新都要重新选择目标 |

#### 修复

- 主区改为 3 列（640 / 4 / `*`），`GridSplitter` 独立成列并设置 `ResizeBehavior="PreviousAndNext"`、`ResizeDirection="Columns"`；左列补 `MinWidth=420` / `MaxWidth=1200`，右列补 `MinWidth=280`，使拖拽真正生效且两侧都有可用下限。
- 14 处横向 `StackPanel` 全部改为 `WrapPanel`（自动换行），并把原本与按钮同行的状态 `TextBlock` 移到独立行，避免无限宽度约束下的单行拉伸。
- 4 个标签页外层 `ScrollViewer` 增加 `HorizontalScrollBarVisibility="Auto"` 作为兜底，极端窄窗口下仍可横向滚动查看。
- 4 个窗口/进程下拉框改用 `ItemTemplate` + `TextBlock MaxWidth="520" TextTrimming="CharacterEllipsis"`，配合 `TextSearch.TextPath` 保留键盘前缀搜索；`MaxDropDownHeight="420"` 限制列表高度。窗口标题与进程项均附 `ToolTip` 显示完整内容。
- 补齐 4 个刷新按钮：截图与输入模拟标签页各一个「刷新窗口」，钩子监听与窗口事件标签页各一个「刷新进程」。
- `RefreshWindows` / `RefreshProcesses` 在替换集合后按句柄 / 进程 ID 恢复选中项；目标已消失时回落为空，属预期结果。

> 实现注记：`MaxDropDownWidth` 是 WinForms `ComboBoxItem` 的属性，WPF 中不存在（先后尝试 `ComboBox` 与 `ComboBoxItem` 均报 `MC4005`/`MC3072`）。WPF 控制弹出列表宽度的有效手段是限制 `ItemTemplate` 内内容的 `MaxWidth`。

#### 验证

新增布局探针，逐个标签页遍历按钮与下拉框，比较 `ActualWidth` 与扣除 `Margin` 后的 `DesiredSize`，并跳过 `Content` 为空的主题内部按钮。四档窗口尺寸实测：

| 请求尺寸 | 内容区宽度（截图/输入模拟） | 内容区宽度（钩子/窗口事件） | 结果 |
| --- | --- | --- | --- |
| 1000×560（最小值） | 614 | 594 | 5 个标签页均未发现裁切 |
| 1100×600 | — | — | 未发现裁切 |
| 1280×760（默认） | 614 | 594 | 未发现裁切 |
| 1920×1080 | — | — | 未发现裁切 |

探针首轮曾把每个按钮都报为裁切，判据误将 `ToolButton` 样式的 8px `Margin` 计入所需宽度；扣除边距后归零，属探针缺陷而非界面缺陷。

新增 `SelectionPersistenceTests` 5 项回归测试，覆盖：按句柄恢复窗口选中、按进程 ID 恢复进程选中、目标消失时回落为空、刷新不凭空产生选中项。

| 配置 | 构建 | net8.0-windows | net48 |
| --- | --- | --- | --- |
| Release | 0 警告 0 错误 | 88 通过 | 88 通过 |

未验证项：DPI 缩放（125% / 150%）与中文输入法候选窗遮挡下的布局表现未实测；探针在 100% 缩放下运行。

### S11 消息泵未启动与 Trace 日志递归（2026-10-08 22:35）

用户实测勾选钩子或启动窗口事件监听后，运行日志反复出现：
「应用钩子配置失败 / 启动窗口事件监听失败：InvalidOperationException 消息泵尚未启动或窗口句柄无效。」

#### 根因

| 序号 | 缺陷 | 根因 | 影响 |
| --- | --- | --- | --- |
| 1 | `BackgroundMessagePump.Start()` 从未被调用 | 该调用只存在于 `HeadlessHost.Run()`；WPF 交互与托盘模式下 `App` 从不启动消息泵，但容器照常注册了它 | 全局快捷键、低级钩子、窗口事件全部不可用，句柄恒为 0，`RequireWindowHandle` 抛 `InvalidOperationException` |
| 2 | 退出时不停止消息泵 | `OnExit` 未调用 `Stop()`，且消息泵未实现 `IDisposable`，Prism 释放容器也不会停止它 | 后台 STA 线程与消息窗口泄漏，钩子无法完成 `UnhookWinEvent` |
| 3 | `SingleInstanceLock` 为局部变量 | 在 `OnStartup` 中以 `var` 创建，方法返回后即具备被回收条件 | 互斥量可能提前释放，单实例保护失效 |
| 4 | `TraceLog` 无限递归导致栈溢出 | `TraceLog` 是 `TraceListener` 并已加入 `Trace.Listeners`，却在 `Write` / `WriteLine` 内部再次调用 `Trace.WriteLine`，重新进入监听器链并回调自身 | 进程以 `0xC00000FD`（栈溢出）终止，**无可捕获的托管异常**。库层任何一处 `Trace.WriteLine` 都会触发，例如 `WinEventHookManager.StopCore` |

缺陷 4 是修复 1 之后才暴露的：消息泵启动后「停止窗口事件」会走到 `StopCore`，其中的 `Trace.WriteLine` 立刻触发递归，探针进程以 `0xC00000FD` 崩溃。该缺陷意味着只要触发一次钩子/事件停止动作，真实应用就会无提示崩溃。

#### 修复

- `CreateShell` 在解析任何依赖服务之前解析并启动消息泵（`RegisterHotKey`、`SetWinEventHook`、`UnhookWinEvent` 均要求与 HWND 同线程，必须经由消息线程调度）。启动失败按降级处理：记录日志并弹出说明性提示，不中断进程——截图与输入模拟不依赖消息泵，仍可使用。
- `OnExit` 在 Prism 释放容器单例之前显式 `Stop()` 消息泵，使仍在运行的钩子有机会在消息线程上完成卸载；随后释放单实例互斥量。
- `SingleInstanceLock` 与消息泵均提升为字段并在 `OnExit` 中释放。
- `TraceLog` 重写：各重写方法只向 NLog 转发，**禁止**再调用 `Trace.WriteLine` / `Trace.Write`；转发失败静默跳过（桥接属诊断手段，不得因日志后端故障影响调用方，更不得递归回 Trace）。类型文档新增实现约束说明，避免后续误改。

#### 验证

探针实例化真实 `App`，依次执行勾选钩子 → 应用钩子配置 → 停止钩子 → 启动窗口事件 → 停止窗口事件：

| 操作 | 修复前 | 修复后 |
| --- | --- | --- |
| 应用钩子配置 | InvalidOperationException 消息泵尚未启动或窗口句柄无效 | 键盘=开，鼠标=开，过滤=全局 |
| 停止钩子 | 同上异常 | 键盘=关，鼠标=关，过滤=全局 |
| 启动窗口事件 | 同上异常 | 正在监听 PID 36128（HookProbe） |
| 停止窗口事件 | **进程栈溢出终止（0xC00000FD）** | 未监听 |

Debug 与 Release 两套配置分别验证，均无「消息泵尚未启动」且无崩溃。Debug 配置下 NLog 文件确认写入 `后台消息泵已启动，消息窗口句柄: 0x2180E96` 与库层 `Trace` 桥接内容，证明修复后转发链路正常而非仅抑制递归。

新增 `TraceLogTests` 4 项回归测试：经 `Trace.WriteLine` 单次写入只触发一次监听器调用、20 次批量写入不累积、`TraceSource.TraceEvent` 路径调用次数稳定（因 `TraceSource` 会先派发 `TraceEvent` 再落到 `Write`，故断言次数稳定而非绝对值）、直接调用各重写方法不抛异常。哨兵监听器在单次写入超过 20 次调用时主动抛出可诊断异常，使递归回归时测试以明确原因失败，而不是让测试宿主以 `0xC00000FD` 崩溃。测试置于 `DisableParallelization` 集合内，避免修改 `Trace.Listeners` 全局状态干扰其他测试。

| 配置 | 构建 | net8.0-windows | net48 |
| --- | --- | --- | --- |
| Release | 0 警告 0 错误 | 92 通过 | 92 通过 |

未验证项：长时间挂机下的消息泵线程与 GDI 句柄增长曲线未测量；本次仅验证启动、调度与停止路径正确。

## 变更记录
| 时间 | 变更摘要 |
| --- | --- |
| 2026-10-08 22:35:00 +08:00 | 记录 S11 消息泵与 Trace 日志缺陷：修复 WPF 模式下消息泵从未启动、退出不停止、单实例锁为局部变量、TraceLog 监听器无限递归导致栈溢出四项缺陷；新增 4 项回归测试（88 → 92 项）。 |
| 2026-10-08 22:10:00 +08:00 | 记录 S10 界面布局与手动刷新：主区改 3 列并修复分隔条吞掉剩余空间、14 处横向容器改 WrapPanel、4 个下拉框加宽度与省略号约束、补齐 4 个刷新按钮、刷新保留选中项；布局探针四档尺寸未发现裁切；新增 5 项回归测试（83 → 88 项）。 |
| 2026-10-08 21:40:00 +08:00 | 记录 S9 应用宿主运行期缺陷修复：补齐 DataContext 赋值与容器注册、新增 Dispatcher 工厂注册、取消进程枚举的窗口过滤（11 → 424）；新增 8 项回归测试（75 → 83 项）；同时清除本文件被脚本误注入的 12 行变更记录。 |
| 2026-10-08 19:15:00 +08:00 | 记录 S1-S8 实现与验证：版本探测去 shim、窗口边界语义拆分、钩子宿主线程统一、有界事件流、消息泵超时取消、输入正确性、Core 平台无关化、应用宿主重写、P/Invoke 契约修正、测试体系扩充（13 → 75 项）；双 SDK 双目标构建与测试全部通过。 |
| 2026-10-08 19:10:00 +08:00 | 记录 S8 技术选型决策与兼容性实测：确认 WPF 多目标与 Prism 9.0.537、NLog 5.4.0 在 net48 与 net8.0-windows 双目标下可用，不抬高 VS 2022 17.8 下限。 |
| 2026-10-08 18:40:00 +08:00 | 记录工具链向后兼容改造：解决方案改为经典 .sln、global.json 基线放宽至 SDK 8.0.100 + latestMajor、新增 toolchain-compatibility.md；SDK 8.0.420 与 10.0.401 双版本构建与双目标测试均通过。 |
| 2026-10-08 16:35:00 +08:00 | 记录 RegisterHotKey 跨线程 1408 解决机制与全功能验证成果。 |
| 2026-10-08 15:58:00 +08:00 | 创建实现记录文档。 |
