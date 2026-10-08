# GameTools 核心系统实施计划

## 1. 计划概述

本项目为面向 Windows 桌面平台的底层交互系统，涵盖后台常驻进程、屏幕/窗口截图、全局快捷键、前后台文本输入模拟、全局及进程消息钩子监听五大核心能力。

项目采用**多目标框架**同时支持现代与旧版 Windows：现代目标 `net8.0-windows` 面向 Windows 10/11，旧系统目标 `net48` 面向 Windows 7 SP1 / Windows 8 / Windows 8.1。

> **修订说明**：本计划早期版本假设「.NET 8 可运行于 Windows 7」，该前提不成立（.NET Core 3.1 是最后支持 Win7 SP1 的版本，.NET 5 起最低要求 Windows 10 1607）。2026-10-08 复核确认改为多目标路线；实测改造量为 6 处 BCL 调用替换与 4 个语言垫片类型，`.NET Framework 4.8` targeting pack 无需额外安装。

## 2. 实施目标与技术选型

- **目标框架**：多目标 `net8.0-windows;net48`；契约层 `GameTools.Core` 收敛为 `netstandard2.0`（平台无关）。
- **兼容策略**：
  - 底层 P/Invoke 按 Windows 版本做特性探测与优雅降级（PrintWindow 标志位区分、DWM 扩展边界、Per-Monitor DPI）。
  - **平台契约统一**：能力探测按目标框架归一（net48 走 `RtlGetVersion` 并以注册表兜底，net8 走 `Environment.OSVersion`），使同一代码在两个目标上的分支判定与降级结果一致。
  - 后台消息循环采用独立单线程单元（STA）线程的消息泵（`BackgroundMessagePump`）；全局热键、低级钩子与 WinEvent 的安装与卸载全部在该线程执行。
  - GDI 资源全面采用安全句柄封装（`SafeGdiObjectHandle` / `SafeGdiDcHandle` / `SafeWindowDcHandle`），保证异常路径下 `DeleteObject` / `DeleteDC` / `ReleaseDC` 必定执行。
  - 全局钩子采用委托强引用加 `GCHandle` 保活，通过有界 `System.Threading.Channels` 异步解耦，杜绝钩子超时卸载与内存无界增长。
  - 依赖版本集中管理（`Directory.Packages.props`），工具链固定（`global.json`）。
  - **工具链兼容下限**：Visual Studio 2022 17.8 / .NET SDK 8.0.100 / MSBuild 17.8 / NuGet 6.2+；解决方案使用经典 `.sln`（Format Version 12.00）而非 `.slnx`，确保老版本 VS 无需预览特性即可加载。详见 `toolchain-compatibility.md`。

## 3. 阶段规划（现状与改进路线）

| 阶段 | 内容 | 状态 |
| --- | --- | --- |
| 阶段一 | 文档与分层解决方案（Core, Win32, Infrastructure, App, Tests） | 已完成 |
| 阶段二 | Win32 抽象与系统适配（原生 API、版本探测、安全句柄） | 已完成，DPI 适配待补（S1） |
| 阶段三 | 后台宿主与单实例控制（Mutex 与 STA 消息泵） | 已完成，生命周期与超时待补（S3） |
| 阶段四 | 截图模块（全屏、区域、后台 PrintWindow 截图与版本降级） | 已完成，黑屏判定与 BGRA 待补（S4） |
| 阶段五 | 全局快捷键（注册、冲突检测、异步派发） | 已完成，多实例与注销语义待补（S3） |
| 阶段六 | 文本与按键模拟（前台 Unicode SendInput 与后台消息投递） | 已完成，诊断与节奏待补（S4） |
| 阶段七 | 消息钩子与进程监听（低级钩子加 WinEventHook） | 已完成，宿主线程归属待补（S2） |
| 阶段八 | 应用宿主与测试验证（命令行/托盘演示加自动化测试） | 部分完成，验证覆盖不足（S5） |

**改进阶段 S0–S7 的任务清单见 `tasks.md`。**

## 4. 任务范围与非目标

### 4.1 本轮范围

- 平台契约修正：多目标框架、旧系统特性探测一致性、DPI 感知。
- 宿主正确性：钩子安装线程归属、消息泵生命周期与超时、退出与清理路径。
- 稳定性：事件队列有界化、零分配事件、进程信息缓存、停止完成屏障、并发保护。
- 功能缺口：可选宿主模式、截图黑屏判定与 BGRA、输入节奏与诊断、动态进程过滤。
- 验证体系：失败路径、并发竞态、资源耐久、双目标差异测试。
- 架构收敛：`GameTools.Core` 去除 WinForms 与 System.Drawing 依赖。
- 应用宿主重写：`GameTools.App` 由控制台 + WinForms 托盘改为 WPF + Prism + NLog（保留多目标与 net48 兼容）。
- 文档同步：五份文档、`README.md` 与使用与运维手册（HTML）与实现对齐。

### 4.2 非目标（本轮明确不做）

- 不做进程注入、驱动级输入、任意窗口消息拦截（仅低级输入钩子与 WinEvent 公开事件）。
- 不做图像识别、OCR、像素分析等算法能力。
- 不做跨平台（Linux/macOS）支持；`GameTools.Core` 平台无关化仅为契约质量，不代表新增平台。
- 不做配置持久化存储引擎（仅内存配置热更新管道）。
- 第三方依赖范围锁定为 WPF 应用所必需的 Prism（MVVM 与容器）与 NLog（日志），不额外引入其他 UI 框架、ORM、通信或遥测库。
- 不搭建自动化 CI 流水线（本轮验证以本地命令为准）。

## 5. 受影响模块

| 模块 | 涉及改动 | 风险等级 |
| --- | --- | --- |
| `src/GameTools.Core` | 目标框架改 `netstandard2.0`；新增平台无关值对象；接口签名调整 | 高（破坏性 API 变更） |
| `src/GameTools.Win32` | 版本探测改造、消息循环 P/Invoke 签名修正、窗口边界与 DPI 适配 | 中 |
| `src/GameTools.Infrastructure` | 钩子宿主线程化、消息泵超时、事件队列有界化、输入诊断、截图黑屏判定与 BGRA | 高 |
| `src/GameTools.App` | 由 WinForms 控制台宿主重写为 WPF + Prism(MVVM/DryIoc) + NLog；可选宿主模式、退出唤醒、配置热更新、异常回滚 | 高（技术栈替换） |
| `tests/GameTools.Tests` | 双目标执行、新增失败/并发/资源/差异测试 | 中 |
| 工程根 | `Directory.Build.props`、`Directory.Packages.props`、`global.json`、`.editorconfig`、`app.manifest` | 中 |

依赖方向保持不变：`Core` 低于 `Win32` 低于 `Infrastructure` 低于 `App`，`Tests` 依赖三者，不得出现反向依赖或循环依赖。

## 6. 兼容风险

| 风险 | 影响 | 应对 |
| --- | --- | --- |
| 旧系统未实机验证 | 结论基于官方支持矩阵与特性探测，非实机结论 | 文档显式声明验证缺口；建立特性探测矩阵与版本分支单测 |
| net48 版本信息被 shim 截断 | 缺 manifest 时 Win10 上报为 6.2，导致降级分支判定错误 | 改用 `RtlGetVersion` 加注册表 `CurrentBuildNumber` 兜底，不依赖 manifest |
| net48 交付形态受限 | 不支持单文件发布与 ReadyToRun | Win7 走自包含部署并前置 VC++ 运行库，文档写明 |
| 双目标产物行为分叉 | 相同输入产生不同结果，违反一致性要求 | 平台契约归一并将双目标差异测试纳入验收 |
| Core 破坏性 API 变更 | 调用方签名不兼容 | 独立提交、单独可回退；提供值对象转换器；由测试保护网覆盖 |
| 事件队列有界化后可能丢弃事件 | 高频场景丢事件 | 容量可配置加丢弃计数可观测；健康状态丢弃率阈值纳入验收 |
| 旧系统 GDI+ 与新运行时编码差异 | PNG 字节可能不一致 | 按目标分别定义期望值，不假设字节一致 |
| WPF 技术栈替换引入新依赖 | Prism 与 NLog 需同时支持 net48 与 net8.0-windows，且不得抬高 VS 17.8 下限 | 已实测 Prism 9.0.537 与 NLog 5.4.0 在双目标下可解析并编译；WPF 多目标在 SDK 8.0.420 下 0 警告 0 错误 |
| WPF 应用不应引用 WinForms | 现有统一 `UseWindowsForms=true` 会给 WPF 应用引入无关依赖与 WFAC010 诊断 | `Directory.Build.props` 改为按项目条件设置，App 关闭 WinForms 并启用 WPF |

## 7. 验收标准

### 7.1 构建与测试（已确定阈值）

| 项 | 阈值 |
| --- | --- |
| 双目标构建 | `dotnet build -f net8.0-windows` 与 `-f net48` 均 0 错误 0 警告 |
| 双目标测试 | 两目标测试套件全部通过 |
| 消息泵同步调度 | 默认超时 5000ms，超时抛出 `TimeoutException`（不得无限等待） |
| 钩子回调耗时 | P99 小于 1ms；同时将 `LowLevelHooksTimeout` 设为 1000ms 以便尽早暴露超时（Windows 默认 3000ms 会静默卸载钩子） |
| 事件队列 | 容量 4096 且有界；健康状态丢弃率小于 0.01%，丢弃计数可查询 |
| GDI 资源 | 连续 10000 次截图后 `GetGuiResources` 的 GDI 增量不超过 5 |
| 托管内存 | 钩子运行 1 小时托管堆增长小于 5MB |
| 启动时间 | 进程启动到消息泵就绪小于 1s |
| App 技术栈 | `GameTools.App` 为 WPF + Prism + NLog；双目标构建 0 警告 0 错误；Prism 容器可解析全部核心服务；NLog 输出文件与控制台双通道 |

### 7.2 待需求方确认的阈值（缺口，不得自行宣称达标）

| 项 | 缺口内容 |
| --- | --- |
| 待机 CPU 小于 0.1% | 缺硬件型号、采样窗口长度、计量口径（进程级或机器级） |
| 截图耗时 | 缺分辨率基线、硬件环境、预热方式与采样数量 |
| 长周期高频无泄漏 | 缺数据规模、调用频率、持续时长与允许的事件丢弃率 |
| 零阻塞 | 缺钩子回调与消息泵各自的耗时预算定义 |
| 非管理员运行 | 缺 UIPI 受限场景的期望结果定义（明确降级还是报错） |

### 7.3 功能验收

每条需求（`requirements.md` 中 REQ 编号）须有对应测试或显式说明未覆盖原因；`tasks.md` 中任务仅在验证通过后方可勾选。

## 8. 验证方案

1. **构建验证**：双目标分别执行 `dotnet build`，记录警告与错误数。
2. **测试验证**：双目标执行测试套件，按失败路径、并发竞态、资源耐久、双目标差异四类分层覆盖。
3. **测试隔离原则**：不截取用户桌面、不录制真实键鼠、不向第三方应用注入输入；使用自建测试宿主窗口与文本控件及辅助进程，原生调用以可替换依赖注入替身。
4. **版本分支验证**：抽象 `IOsVersionProvider`，以注入方式覆盖 Win7/8/8.1/10/11 全部版本分支，不依赖真实运行系统。
5. **回归范围**：Core 层改动（S6）执行全量回归；其余阶段执行受影响模块聚焦测试并附加消息泵相关回归。
6. **验证记录**：构建与测试产物、运行配置写入 `Outputs/Features/GameTools/功能点名_时间戳/`，并更新 `implementation.md` 验收日志。
7. **未验证声明**：真机未覆盖的平台必须在 `requirements.md` 与本文件显式声明，不得以设计推论替代验证结论。

## 9. 回退方案

| 阶段 | 回退方式 |
| --- | --- |
| S0 工程治理 | 独立提交，删除新增文件即回退；csproj 属性还原随提交回退 |
| S1 平台一致性 | 版本探测与 manifest 分两个提交，可单独回退；移除 manifest 后由 `RtlGetVersion` 独立承担 |
| S2 钩子宿主线程 | 单一提交，回退后恢复为调用线程安装，须同时标注风险 |
| S3 稳定性 | 按子项拆分提交（消息泵、退出路径、事件流、停止语义、热键、输入、单实例），逐项可回退 |
| S4 功能缺口 | 按需求分组提交，逐组可回退；代理对与 BGRA 为新增能力，回退不影响既有行为 |
| S5 测试体系 | 仅新增测试，无生产行为变更 |
| S6 Core 去耦 | 独立提交，单独可 revert；回退后恢复原签名，转换器保留为内部工具 |
| S7 文档 | 纯文档提交 |

通用约束：不使用 `--no-verify`、`--amend`、裸 `--force`；不重写历史；每阶段提交前审阅 `git diff --cached` 并以中文汇总变更与风险。

## 10. 文档与输出目录

| 用途 | 位置 |
| --- | --- |
| 版本化文档（唯一来源） | `Docs/Plans/Features/GameTools/GameToolsCore/`：`plan.md`、`requirements.md`、`design.md`、`implementation.md`、`tasks.md` |
| 工具链兼容基线 | `Docs/Plans/Features/GameTools/GameToolsCore/toolchain-compatibility.md` |
| 用户手册 | `Docs/GameTools_用户使用手册.html` |
| 分析报告、验证日志、构建与测试产物 | `Outputs/Features/GameTools/功能点名_时间戳/`；缺陷类产物用 `Outputs/Fixes/GameTools/`，跨项目用 `Outputs/Fixes/CrossProjects/`（均在 `.gitignore` 中，禁止提交） |

本项目属单项目专属功能特性，按仓库文档规范置于 `Docs/Plans/Features/GameTools/GameToolsCore/`；若后续出现跨仓库联动修复，按规范另置于 `Docs/Plans/Fixes/CrossProjects/`，不得与本目录混放。同一功能仅维护唯一文档来源，禁止通过近义名称或目录副本创建第二来源。

## 11. 分支与基线记录

| 项 | 值 |
| --- | --- |
| 功能分支 | `lihongxu/gametools-multi-target` |
| 基线提交 | `20d5a4c`（feat: 初始化 GameTools 底层交互系统与 Wiki 手册） |
| 基线来源 | `master`（已同步 `origin/master`；合并 `lihongxu/gametools-core` 结果为 Already up to date） |
| 分支创建前置条件 | 工作区干净、远端引用已更新、无并行 worktree 代码改动 |

## 变更记录

| 时间 | 变更摘要 |
| --- | --- |
| 2026-10-08 17:40:00 +08:00 | 依据两份复核报告重写计划：技术前提由「.NET 8 与 Win7」修正为多目标 `net8.0-windows;net48`；补齐范围与非目标、受影响模块、兼容风险、可度量验收标准、验证方案、回退方案、文档与输出目录、分支与基线记录等强制章节；阶段状态表对齐实现现状并指向 S0–S7 改进路线。 |
| 2026-10-08 19:10:00 +08:00 | 追加应用宿主重写范围：`GameTools.App` 由控制台 + WinForms 改为 WPF + Prism + NLog（用户指定），并据此调整受影响模块风险等级、兼容风险与验收标准；新增 `README.md` 与使用与运维手册为交付物。 |
| 2026-10-08 18:40:00 +08:00 | 补充工具链兼容基线：解决方案由 `.slnx` 改为经典 `GameTools.sln`（兼容 VS 2022 17.8 之前的版本），`global.json` 基线放宽为 8.0.100 + rollForward latestMajor，新增 `toolchain-compatibility.md` 记录最低版本、约束来源与新特性引入检查清单。 |
| 2026-10-08 17:50:00 +08:00 | 按仓库文档目录规范迁移本专题：五段式文档由 `Docs/GameToolsCore/` 迁至 `Docs/Plans/Features/GameTools/GameToolsCore/`（单项目专属功能特性），同步更新第 8 节与第 10 节的文档及产物路径表述。 || 2026-10-08 15:58:00 +08:00 | 创建计划文档，明确 .NET 8 与 Win7+ 平台架构目标与分阶段计划。 |