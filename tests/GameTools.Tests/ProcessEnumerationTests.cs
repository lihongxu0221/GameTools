using System;
using System.Diagnostics;
using System.Linq;
using GameTools.App.Services;
using GameTools.App.ViewModels;
using GameTools.Core.Abstractions;
using GameTools.Core.Events;
using Xunit;

namespace GameTools.Tests;

/// <summary>
/// 进程枚举与进程项显示契约的回归测试。
/// </summary>
/// <remarks>
/// 回归背景：曾按 <c>MainWindowHandle != IntPtr.Zero</c> 过滤进程，
/// 导致控制台程序、后台服务等无窗口进程被整体排除（实测 422 个进程仅剩 11 个），
/// 用户在界面上无法选择钩子目标。此处锁定“不得按是否有窗口过滤”这一行为。
/// </remarks>
public class ProcessEnumerationTests
{
    /// <summary>
    /// 枚举结果必须覆盖无窗口进程，且数量显著多于仅含有窗口进程的子集。
    /// </summary>
    [Fact]
    public void ListProcesses_ShouldIncludeProcessesWithoutMainWindow()
    {
        HookService service = CreateService();
        var choices = service.ListProcesses();

        int expectedTotal = 0;
        int withMainWindow = 0;
        foreach (Process proc in Process.GetProcesses())
        {
            using (proc)
            {
                expectedTotal++;
                try
                {
                    if (proc.MainWindowHandle != IntPtr.Zero)
                    {
                        withMainWindow++;
                    }
                }
                catch (Exception)
                {
                    // 枚举期间退出的进程不参与比较
                }
            }
        }

        Assert.NotEmpty(choices);

        // 无窗口进程同样必须在结果中：这是本次修复的核心断言
        Assert.Contains(choices, c => !c.HasMainWindow);

        // 结果数量不应被"是否有窗口"削减；允许因枚举期间进程退出而略有差异
        Assert.True(
            choices.Count > withMainWindow,
            $"枚举结果 {choices.Count} 项应多于仅含有窗口进程的 {withMainWindow} 项，" +
            "否则说明仍按 MainWindowHandle 过滤。");
    }

    /// <summary>
    /// 枚举结果中的进程 ID 必须唯一，避免界面出现重复条目。
    /// </summary>
    [Fact]
    public void ListProcesses_ShouldReturnUniqueProcessIds()
    {
        HookService service = CreateService();
        var choices = service.ListProcesses();

        Assert.Equal(choices.Count, choices.Select(c => c.ProcessId).Distinct().Count());
    }

    /// <summary>
    /// 枚举结果在"有窗口"与"无窗口"两组内分别按名称升序排列。
    /// </summary>
    [Fact]
    public void ListProcesses_ShouldSortByNameWithinEachWindowGroup()
    {
        HookService service = CreateService();
        var choices = service.ListProcesses();

        AssertNameAscending(choices.Where(c => c.HasMainWindow).Select(c => c.ProcessName));
        AssertNameAscending(choices.Where(c => !c.HasMainWindow).Select(c => c.ProcessName));
    }

    /// <summary>
    /// 断言给定名称序列按序号规则升序排列。
    /// </summary>
    private static void AssertNameAscending(System.Collections.Generic.IEnumerable<string> names)
    {
        string[] array = names.ToArray();
        for (int i = 1; i < array.Length; i++)
        {
            Assert.True(
                string.CompareOrdinal(array[i - 1], array[i]) <= 0,
                $"第 {i} 项 '{array[i]}' 排在前一项 '{array[i - 1]}' 之后，组内未按名称升序。");
        }
    }

    /// <summary>
    /// 每个枚举项都必须带有非空进程名，供界面显示使用。
    /// </summary>
    [Fact]
    public void ListProcesses_ShouldAlwaysProvideProcessName()
    {
        HookService service = CreateService();
        var choices = service.ListProcesses();

        Assert.All(choices, c => Assert.False(string.IsNullOrWhiteSpace(c.ProcessName)));
    }

    /// <summary>
    /// 有窗口的进程必须排在无窗口进程之前。
    /// </summary>
    [Fact]
    public void ListProcesses_ShouldPlaceWindowedProcessesFirst()
    {
        HookService service = CreateService();
        var choices = service.ListProcesses();

        bool seenWindowless = false;
        foreach (var choice in choices)
        {
            if (!choice.HasMainWindow)
            {
                seenWindowless = true;
            }
            else
            {
                Assert.False(seenWindowless, "有窗口的进程之后不应再出现无窗口进程");
            }
        }
    }

    /// <summary>
    /// 有窗口的进程显示为“名称 (PID)”。
    /// </summary>
    [Fact]
    public void ProcessChoice_Display_ForWindowedProcess_ShouldOmitMarker()
    {
        var choice = new ProcessChoice(1234, "notepad", HasMainWindow: true);

        Assert.Equal("notepad (1234)", choice.Display);
        Assert.Equal("notepad (1234)", choice.ToString());
    }

    /// <summary>
    /// 无窗口的进程显示时须带标记，避免用户误选。
    /// </summary>
    [Fact]
    public void ProcessChoice_Display_ForWindowlessProcess_ShouldAppendMarker()
    {
        var choice = new ProcessChoice(5678, "sqlservr", HasMainWindow: false);

        Assert.Equal("sqlservr (5678) [无窗口]", choice.Display);
        Assert.Equal("sqlservr (5678) [无窗口]", choice.ToString());
    }

    /// <summary>
    /// 未显式传入窗口标记时默认按有窗口呈现，保持旧构造方式的兼容性。
    /// </summary>
    [Fact]
    public void ProcessChoice_ShouldDefaultHasMainWindowToTrue()
    {
        var choice = new ProcessChoice(99, "explorer");

        Assert.True(choice.HasMainWindow);
    }

    /// <summary>
    /// 构造只依赖钩子管理器，但进程枚举不触碰它们，故可安全传入最小替身。
    /// </summary>
    private static HookService CreateService() =>
        new HookService(new NoOpLowLevelHookManager(), new NoOpWinEventHookManager());

    /// <summary>
    /// 空实现的低级钩子管理器，仅用于满足构造依赖。
    /// </summary>
    private sealed class NoOpLowLevelHookManager : ILowLevelHookManager
    {
        /// <summary>
        /// 无需订阅：进程枚举路径不产生任何钩子事件。
        /// </summary>
        event EventHandler<KeyboardHookEventArgs>? ILowLevelHookManager.KeyDown
        {
            add { }
            remove { }
        }

        /// <summary>
        /// 无需订阅：进程枚举路径不产生任何钩子事件。
        /// </summary>
        event EventHandler<KeyboardHookEventArgs>? ILowLevelHookManager.KeyUp
        {
            add { }
            remove { }
        }

        /// <summary>
        /// 无需订阅：进程枚举路径不产生任何钩子事件。
        /// </summary>
        event EventHandler<MouseHookEventArgs>? ILowLevelHookManager.MouseEvent
        {
            add { }
            remove { }
        }

        /// <inheritdoc />
        public void StartKeyboardHook(int? targetProcessId = null)
        {
        }

        /// <inheritdoc />
        public void StopKeyboardHook()
        {
        }

        /// <inheritdoc />
        public void StartMouseHook(int? targetProcessId = null)
        {
        }

        /// <inheritdoc />
        public void StopMouseHook()
        {
        }

        /// <inheritdoc />
        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// 空实现的窗口事件钩子管理器，仅用于满足构造依赖。
    /// </summary>
    private sealed class NoOpWinEventHookManager : IWinEventHookManager
    {
        /// <summary>
        /// 无需订阅：进程枚举路径不产生任何窗口事件。
        /// </summary>
        event EventHandler<WinEventMessageEventArgs>? IWinEventHookManager.WinEventReceived
        {
            add { }
            remove { }
        }

        /// <inheritdoc />
        public long DroppedEventCount => 0;

        /// <inheritdoc />
        public void Start(int targetProcessId)
        {
        }

        /// <inheritdoc />
        public void Stop()
        {
        }

        /// <inheritdoc />
        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
