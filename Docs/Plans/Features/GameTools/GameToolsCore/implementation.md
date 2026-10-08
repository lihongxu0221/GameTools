# GameTools 实现记录与技术细节

## 1. 运行平台与依赖约定
- **目标框架**：多目标 `net8.0-windows;net48`（`GameTools.Core` 于 S6 阶段收敛为 `netstandard2.0`）。
- **旧系统交付形态**：`net48` 目标面向 Windows 7 SP1 / 8 / 8.1，走自包含部署（不支持单文件发布与 ReadyToRun），需前置 VC++ 2015-2022 运行库。
- **系统支持**：Windows 7 SP1+ (Windows 7 / 8 / 8.1 / 10 / 11)。
- **语言标准**：C# 12，启用 Nullable 检查。

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
## 变更记录
| 时间 | 变更摘要 |
| --- | --- |
| 2026-10-08 16:35:00 +08:00 | 记录 RegisterHotKey 跨线程 1408 解决机制与全功能验证成果。 |
| 2026-10-08 15:58:00 +08:00 | 创建实现记录文档。 |
