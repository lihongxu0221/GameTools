using System;
using System.Collections.Generic;
using System.Linq;
using GameTools.App.Services;
using GameTools.App.ViewModels;
using GameTools.Core.Abstractions;
using GameTools.Core.Enums;
using GameTools.Core.Models;
using Xunit;

namespace GameTools.Tests;

/// <summary>
/// 列表刷新后选中项保持的回归测试。
/// </summary>
/// <remarks>
/// 回归背景：刷新会整体替换集合，而列表项为不可变记录，
/// 导致 <c>SelectedWindow</c> / <c>SelectedProcess</c> 绑定回落到 <c>null</c>，
/// 用户每刷新一次就要重新选择目标窗口或进程。
/// </remarks>
public class SelectionPersistenceTests
{
    /// <summary>
    /// 窗口列表刷新后，按句柄恢复原选中项。
    /// </summary>
    [Fact]
    public void RefreshWindows_ShouldPreserveSelectionByHandle()
    {
        var capture = new ScriptedCaptureService();
        var vm = CreateViewModel(capture);

        vm.SelectedWindow = vm.Windows.Single(w => w.Handle == new IntPtr(0x2000));

        vm.RefreshWindows();

        Assert.NotNull(vm.SelectedWindow);
        Assert.Equal(new IntPtr(0x2000), vm.SelectedWindow!.Handle);
    }

    /// <summary>
    /// 原窗口已消失时，选中项应回落为空，不得指向错误窗口。
    /// </summary>
    [Fact]
    public void RefreshWindows_ShouldClearSelectionWhenWindowGone()
    {
        var capture = new ScriptedCaptureService();
        var vm = CreateViewModel(capture);

        vm.SelectedWindow = vm.Windows.Single(w => w.Handle == new IntPtr(0x2000));

        // 目标窗口已关闭，剩余列表不再包含该句柄
        capture.NextWindows = capture.AllWindows.Where(w => w.Handle != new IntPtr(0x2000)).ToArray();
        vm.RefreshWindows();

        Assert.Null(vm.SelectedWindow);
    }

    /// <summary>
    /// 进程列表刷新后，按进程 ID 恢复原选中项。
    /// </summary>
    [Fact]
    public void RefreshProcesses_ShouldPreserveSelectionByProcessId()
    {
        var hooks = new ScriptedHookService();
        var vm = CreateViewModel(new ScriptedCaptureService(), hooks);

        vm.SelectedProcess = vm.Processes.Single(p => p.ProcessId == 2222);

        vm.RefreshProcesses();

        Assert.NotNull(vm.SelectedProcess);
        Assert.Equal(2222, vm.SelectedProcess!.ProcessId);
    }

    /// <summary>
    /// 原进程已退出时，选中项应回落为空。
    /// </summary>
    [Fact]
    public void RefreshProcesses_ShouldClearSelectionWhenProcessGone()
    {
        var hooks = new ScriptedHookService();
        var vm = CreateViewModel(new ScriptedCaptureService(), hooks);

        vm.SelectedProcess = vm.Processes.Single(p => p.ProcessId == 2222);

        hooks.NextProcesses = hooks.AllProcesses.Where(p => p.ProcessId != 2222).ToArray();
        vm.RefreshProcesses();

        Assert.Null(vm.SelectedProcess);
    }

    /// <summary>
    /// 尚未选中任何项时刷新，不应凭空产生选中项。
    /// </summary>
    [Fact]
    public void Refresh_ShouldNotInventSelection()
    {
        var vm = CreateViewModel();

        Assert.Null(vm.SelectedWindow);
        Assert.Null(vm.SelectedProcess);

        vm.RefreshWindows();
        vm.RefreshProcesses();

        Assert.Null(vm.SelectedWindow);
        Assert.Null(vm.SelectedProcess);
    }

    private static MainViewModel CreateViewModel(
        IScreenCaptureService? capture = null,
        IHookService? hooks = null) =>
        new(
            capture ?? new ScriptedCaptureService(),
            new NoOpHotkeyService(),
            new NoOpInputService(),
            hooks ?? new ScriptedHookService());

    /// <summary>
    /// 可编排返回内容的截图服务。
    /// </summary>
    private sealed class ScriptedCaptureService : IScreenCaptureService
    {
        /// <summary>
        /// 全部候选窗口。
        /// </summary>
        internal WindowInfo[] AllWindows { get; } =
        {
            MakeWindow(0x1000, "Alpha", 1111),
            MakeWindow(0x2000, "Beta", 2222),
            MakeWindow(0x3000, "Gamma", 3333),
        };

        /// <summary>
        /// 下次刷新返回的内容，缺省为 <see cref="AllWindows"/>。
        /// </summary>
        internal WindowInfo[] NextWindows { get; set; } = Array.Empty<WindowInfo>();

        /// <summary>
        /// 构造时立即返回候选窗口，使视图模型初始化即有数据。
        /// </summary>
        internal ScriptedCaptureService() => NextWindows = AllWindows;

        /// <inheritdoc />
        public IReadOnlyList<WindowInfo> ListTopLevelWindows() => NextWindows;

        /// <inheritdoc />
        public CaptureOutcome CaptureFullScreen(bool allMonitors) => throw new NotSupportedException();

        /// <inheritdoc />
        public CaptureOutcome CaptureRegion(int x, int y, int width, int height) => throw new NotSupportedException();

        /// <inheritdoc />
        public CaptureOutcome CaptureWindow(IntPtr hWnd) => throw new NotSupportedException();

        private static WindowInfo MakeWindow(long handle, string title, int processId) =>
            new(
                new IntPtr(handle),
                title,
                "FakeClass",
                new CaptureBounds(0, 0, 800, 600),
                processId,
                "fake",
                true,
                false);
    }

    /// <summary>
    /// 可编排返回内容的钩子服务。
    /// </summary>
    private sealed class ScriptedHookService : IHookService
    {
        /// <summary>
        /// 全部候选进程。
        /// </summary>
        internal ProcessChoice[] AllProcesses { get; } =
        {
            new ProcessChoice(1111, "alpha", true),
            new ProcessChoice(2222, "beta", false),
            new ProcessChoice(3333, "gamma", true),
        };

        /// <summary>
        /// 下次刷新返回的内容，缺省为 <see cref="AllProcesses"/>。
        /// </summary>
        internal ProcessChoice[] NextProcesses { get; set; } = Array.Empty<ProcessChoice>();

        /// <summary>
        /// 构造时立即返回候选进程，使视图模型初始化即有数据。
        /// </summary>
        internal ScriptedHookService() => NextProcesses = AllProcesses;

        /// <inheritdoc />
        public IReadOnlyList<ProcessChoice> ListProcesses() => NextProcesses;

        /// <inheritdoc />
        public void ApplyHookConfiguration(bool enableKeyboard, bool enableMouse, int? filterProcessId)
        {
        }

        /// <inheritdoc />
        public void StartWindowEventHook(int processId)
        {
        }

        /// <inheritdoc />
        public void StopWindowEventHook()
        {
        }
    }

    /// <summary>
    /// 空实现的快捷键服务：本组测试不涉及快捷键。
    /// </summary>
    private sealed class NoOpHotkeyService : IHotkeyService
    {
        /// <inheritdoc />
        public HotkeyRegistrationResult Register(VirtualKey key, KeyModifiers modifiers, string action) => new()
        {
            Success = true,
            Id = 1
        };

        /// <inheritdoc />
        public bool Unregister(int id) => true;

        /// <inheritdoc />
        public void UnregisterAll()
        {
        }
    }

    /// <summary>
    /// 空实现的输入模拟服务：本组测试不涉及输入。
    /// </summary>
    private sealed class NoOpInputService : IInputService
    {
        /// <inheritdoc />
        public void SendText(string text, int delayMs, int jitterMs)
        {
        }

        /// <inheritdoc />
        public void SendKey(VirtualKey key, KeyModifiers modifiers)
        {
        }

        /// <inheritdoc />
        public void PostText(IntPtr hWnd, string text, int delayMs)
        {
        }

        /// <inheritdoc />
        public void PostKey(IntPtr hWnd, VirtualKey key)
        {
        }

        /// <inheritdoc />
        public bool SetWindowText(IntPtr hWnd, string text) => true;
    }
}
