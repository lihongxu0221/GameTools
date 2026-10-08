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
| 2026-10-08 19:10:00 +08:00 | 记录 S8 技术选型决策与兼容性实测：确认 WPF 多目标与 Prism 9.0.537、NLog 5.4.0 在 net48 与 net8.0-windows 双目标下可用，不抬高 VS 2022 17.8 下限。 |
| 2026-10-08 18:40:00 +08:00 | 记录工具链向后兼容改造：解决方案改为经典 .sln、global.json 基线放宽至 SDK 8.0.100 + latestMajor、新增 toolchain-compatibility.md；SDK 8.0.420 与 10.0.401 双版本构建与双目标测试均通过。 |
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
| 2026-10-08 19:10:00 +08:00 | 记录 S8 技术选型决策与兼容性实测：确认 WPF 多目标与 Prism 9.0.537、NLog 5.4.0 在 net48 与 net8.0-windows 双目标下可用，不抬高 VS 2022 17.8 下限。 |
| 2026-10-08 18:40:00 +08:00 | 记录工具链向后兼容改造：解决方案改为经典 .sln、global.json 基线放宽至 SDK 8.0.100 + latestMajor、新增 toolchain-compatibility.md；SDK 8.0.420 与 10.0.401 双版本构建与双目标测试均通过。 | --- |
| 解决方案 | GameTools.slnx（XML 格式） | GameTools.sln（经典 Format Version 12.00），含 src 与 tests 两个解决方案文件夹 |
| global.json | version 10.0.401，rollForward latestFeature | version 8.0.100，rollForward latestMajor，allowPrerelease false |

改造原因：`.slnx` 仅 VS 2022 17.13+ / SDK 9.0.200+ 原生支持；原 `global.json` 锁定 10.0.401 会使 VS 17.8 内置的 SDK 8.0.x 直接构建失败。放宽后 `latestMajor` 允许任意更高版本 SDK，老版本 VS 可正常构建，且新 SDK 仍可使用。

新增 `toolchain-compatibility.md` 记录最低版本矩阵、约束来源与「新特性引入前检查清单」。

验证（同一份源码与工程配置，仅 SDK 不同）：

| SDK | 解决方案 | 构建 | net8.0-windows | net48 |
| --- | --- |
| 2026-10-08 19:10:00 +08:00 | 记录 S8 技术选型决策与兼容性实测：确认 WPF 多目标与 Prism 9.0.537、NLog 5.4.0 在 net48 与 net8.0-windows 双目标下可用，不抬高 VS 2022 17.8 下限。 |
| 2026-10-08 18:40:00 +08:00 | 记录工具链向后兼容改造：解决方案改为经典 .sln、global.json 基线放宽至 SDK 8.0.100 + latestMajor、新增 toolchain-compatibility.md；SDK 8.0.420 与 10.0.401 双版本构建与双目标测试均通过。 | --- | --- |
| 2026-10-08 19:10:00 +08:00 | 记录 S8 技术选型决策与兼容性实测：确认 WPF 多目标与 Prism 9.0.537、NLog 5.4.0 在 net48 与 net8.0-windows 双目标下可用，不抬高 VS 2022 17.8 下限。 | --- |
| 2026-10-08 18:40:00 +08:00 | 记录工具链向后兼容改造：解决方案改为经典 .sln、global.json 基线放宽至 SDK 8.0.100 + latestMajor、新增 toolchain-compatibility.md；SDK 8.0.420 与 10.0.401 双版本构建与双目标测试均通过。 |
| 8.0.420 | GameTools.sln | 0 警告 0 错误 | 13 通过 | 13 通过 |
| 10.0.401 | GameTools.sln | 0 警告 0 错误 | 13 通过 | 13 通过 |

顺带修正 `GameTools.sln` 两处 CLI 生成缺陷：C# 项目类型 GUID 由旧的 `FAE04EC0` 改为 SDK-style 正确的 `9A19103F`；Tests 项目由误置于 src 文件夹改为 tests 文件夹。

未验证：VS 2022 17.8 图形界面加载与调试体验（本机无该版本 VS），以 SDK 8.0.420 命令行等价验证。


### S8 技术选型决策：WPF + Prism + NLog（2026-10-08 19:10）

用户指定 `GameTools.App` 采用 WPF + Prism + NLog。实施前先做兼容性实测，避免引入抬高工具链下限的依赖。

实测结论（SDK 8.0.420，即 VS 2022 17.8~17.11 区间）：

| 验证项 | 结果 |
| --- | --- |
| 2026-10-08 19:10:00 +08:00 | 记录 S8 技术选型决策与兼容性实测：确认 WPF 多目标与 Prism 9.0.537、NLog 5.4.0 在 net48 与 net8.0-windows 双目标下可用，不抬高 VS 2022 17.8 下限。 |
| WPF 多目标（net8.0-windows + net48，UseWPF） | 0 警告 0 错误，两目标产物均生成 |
| Prism.DryIoc 9.0.537 | 双目标还原与编译通过；传递依赖 Prism.Core/Events/Wpf 9.0.537、Prism.Container.Abstractions/DryIoc 9.0.106 |
| NLog 5.4.0 | 双目标还原通过，支持 net35 至 net481 与 netstandard2.0 |
| NLog.Extensions.Logging 5.3.8 | 双目标还原通过 |

依赖版本决策：NLog 使用 5.4.0（5.3.8 在源上不可用，会触发 NU1603 近似匹配警告，故直接锁定 5.4.0 以保证零警告）。

兼容性说明：Prism 9 的核心库面向 netstandard2.0 与 .NET Framework 4.6/4.7，WPF 平台库面向 net472 及以上，net48 与 net8.0-windows 均在支持范围内，不抬高 VS 2022 17.8 下限。

架构影响：现有 `Directory.Build.props` 全局设置 `UseWindowsForms=true`，WPF 应用引入该属性会带来无关 WinForms 依赖并触发 WFAC010 诊断。因此改为按项目条件设置：App 启用 WPF 且显式关闭 WinForms；Infrastructure 因 `WindowsInputSimulator` 与 `BackgroundMessagePump` 依赖 `System.Windows.Forms`（`Keys`、`NativeWindow`、`ApplicationContext`）继续保留 WinForms。

## 变更记录
| 时间 | 变更摘要 |
| --- | --- |
| 2026-10-08 19:10:00 +08:00 | 记录 S8 技术选型决策与兼容性实测：确认 WPF 多目标与 Prism 9.0.537、NLog 5.4.0 在 net48 与 net8.0-windows 双目标下可用，不抬高 VS 2022 17.8 下限。 |
| 2026-10-08 18:40:00 +08:00 | 记录工具链向后兼容改造：解决方案改为经典 .sln、global.json 基线放宽至 SDK 8.0.100 + latestMajor、新增 toolchain-compatibility.md；SDK 8.0.420 与 10.0.401 双版本构建与双目标测试均通过。 |
| 2026-10-08 16:35:00 +08:00 | 记录 RegisterHotKey 跨线程 1408 解决机制与全功能验证成果。 |
| 2026-10-08 15:58:00 +08:00 | 创建实现记录文档。 |
