# GameTools 实现记录与技术细节

## 1. 运行平台与依赖约定
- **目标框架**：.NET 8 (`net8.0-windows`)。
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
- **功能分支**：`lihongxu/gametools-core`
- **基线说明**：仓库初始提交阶段。

## 变更记录
| 时间 | 变更摘要 |
| --- | --- |
| 2026-10-08 16:35:00 +08:00 | 记录 RegisterHotKey 跨线程 1408 解决机制与全功能验证成果。 |
| 2026-10-08 15:58:00 +08:00 | 创建实现记录文档。 |
