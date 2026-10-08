using GameTools.App.Services;
using System.Collections.Concurrent;
using GameTools.App.ViewModels;
using GameTools.Infrastructure.Host;
using GameTools.Core.Abstractions;
using GameTools.Core.Events;
using GameTools.Core.Enums;
using GameTools.Core.Models;
using Xunit;

namespace GameTools.Tests;

/// <summary>
/// <see cref="MainViewModel"/> 的输入校验与命令行为测试。
/// </summary>
/// <remarks>
/// 通过注入替身服务实现，可在无桌面交互、无真实输入的环境下验证：
/// 参数边界校验、失败路径的状态文本、列表上限保护。
/// 明确不验证：真实截图、真实输入注入、真实钩子回调（需人工或专用环境）。
/// </remarks>
public sealed class MainViewModelTests
{
    [Fact]
    public void Constructor_ShouldPopulateKeyChoicesAndWindows()
    {
        MainViewModel vm = CreateViewModel();

        Assert.NotEmpty(vm.AvailableKeys);
        Assert.NotNull(vm.SelectedKey);
        Assert.NotNull(vm.SelectedSendKey);
    }

    [Fact]
    public void CaptureRegion_WithNonNumericInput_ShouldReportError()
    {
        MainViewModel vm = CreateViewModel();
        vm.RegionX = "abc";
        vm.RegionY = "0";
        vm.RegionWidth = "100";
        vm.RegionHeight = "100";

        vm.CaptureRegionCommand.Execute();

        Assert.Contains("整数", vm.StatusBar);
        Assert.Equal(0, ((FakeCaptureService)vm.CaptureService).RegionCaptureCount);
    }

    [Fact]
    public void CaptureRegion_WithZeroWidth_ShouldReportError()
    {
        MainViewModel vm = CreateViewModel();
        vm.RegionX = "0";
        vm.RegionY = "0";
        vm.RegionWidth = "0";
        vm.RegionHeight = "100";

        vm.CaptureRegionCommand.Execute();

        Assert.Contains("大于 0", vm.StatusBar);
        Assert.Equal(0, ((FakeCaptureService)vm.CaptureService).RegionCaptureCount);
    }

    [Fact]
    public void CaptureRegion_WithValidInput_ShouldInvokeService()
    {
        MainViewModel vm = CreateViewModel();
        vm.RegionX = "10";
        vm.RegionY = "20";
        vm.RegionWidth = "300";
        vm.RegionHeight = "200";

        vm.CaptureRegionCommand.Execute();

        Assert.Equal(1, ((FakeCaptureService)vm.CaptureService).RegionCaptureCount);
        Assert.Equal("已保存", vm.LastCaptureInfo.Split(' ')[0]);
    }

    [Fact]
    public void CaptureRegion_WithNegativeDelayInput_ShouldReportError()
    {
        MainViewModel vm = CreateViewModel();
        vm.ForegroundText = "abc";
        vm.CharDelay = "-5";

        vm.SendForegroundTextCommand.Execute();

        Assert.Contains("不能为负数", vm.StatusBar);
        Assert.Equal(0, ((FakeInputService)vm.InputService).SendTextCount);
    }

    [Fact]
    public void SendForegroundText_ShouldForwardDelayAndJitter()
    {
        MainViewModel vm = CreateViewModel();
        vm.ForegroundText = "hello";
        vm.CharDelay = "20";
        vm.CharJitter = "5";

        vm.SendForegroundTextCommand.Execute();

        FakeInputService service = (FakeInputService)vm.InputService;
        Assert.Equal(1, service.SendTextCount);
        Assert.Equal(20, service.LastDelay);
        Assert.Equal(5, service.LastJitter);
    }

    [Fact]
    public void RegisterHotkey_WithFilterPidNotNumber_ShouldReportError()
    {
        MainViewModel vm = CreateViewModel();
        vm.KeyboardHookEnabled = true;
        vm.MouseHookEnabled = true;
        vm.FilterProcessId = "not-a-pid";

        vm.ApplyHookConfigCommand.Execute();

        Assert.Contains("正整数", vm.StatusBar);
        Assert.Equal(0, ((FakeHookService)vm.HookService).ApplyCount);
    }

    [Fact]
    public void ApplyHookConfiguration_WithNullFilter_ShouldApplyGlobalHooks()
    {
        MainViewModel vm = CreateViewModel();
        vm.KeyboardHookEnabled = true;
        vm.MouseHookEnabled = false;
        vm.FilterProcessId = string.Empty;

        vm.ApplyHookConfigCommand.Execute();

        FakeHookService service = (FakeHookService)vm.HookService;
        Assert.Equal(1, service.ApplyCount);
        Assert.True(service.LastKeyboard);
        Assert.False(service.LastMouse);
        Assert.Null(service.LastFilterPid);
    }

    [Fact]
    public void ApplyHookConfiguration_WithSpecificPid_ShouldForwardFilter()
    {
        MainViewModel vm = CreateViewModel();
        vm.KeyboardHookEnabled = true;
        vm.FilterProcessId = "4321";

        vm.ApplyHookConfigCommand.Execute();

        Assert.Equal(4321, ((FakeHookService)vm.HookService).LastFilterPid);
    }

    [Fact]
    public void AppendHookEvent_ShouldCapListLength()
    {
        MainViewModel vm = CreateViewModel();

        // 超出上限后应丢弃最旧条目，避免长时间运行内存无界增长
        for (int i = 0; i < 1200; i++)
        {
            vm.AppendHookEvent($"event-{i}");
        }

        Assert.True(vm.HookEvents.Count <= 500, $"事件条数应被限制，实际为 {vm.HookEvents.Count}");
        Assert.DoesNotContain("event-0", vm.HookEvents);
    }

    [Fact]
    public void ShutdownCommand_ShouldRaiseShutdownRequested()
    {
        MainViewModel vm = CreateViewModel();
        bool raised = false;
        vm.ShutdownRequested += (_, _) => raised = true;

        vm.ShutdownCommand.Execute();

        Assert.True(raised);
        Assert.Equal("正在退出...", vm.StatusBar);
    }

    [Fact]
    public void SelectedProcess_ShouldSyncFilterProcessId()
    {
        MainViewModel vm = CreateViewModel();

        vm.SelectedProcess = new ProcessChoice(1234, "notepad");

        Assert.Equal("1234", vm.FilterProcessId);
    }

    [Fact]
    public void CaptureWindow_WithoutSelection_ShouldReportError()
    {
        MainViewModel vm = CreateViewModel();
        vm.SelectedWindow = null;

        vm.CaptureWindowCommand.Execute();

        Assert.Contains("请先选择目标窗口", vm.StatusBar);
    }

    private static MainViewModel CreateViewModel()
    {
        return new MainViewModel(
            new FakeCaptureService(),
            new FakeHotkeyService(),
            new FakeInputService(),
            new FakeHookService());
    }

    private sealed class FakeCaptureService : IScreenCaptureService
    {
        internal int RegionCaptureCount;

        public CaptureOutcome CaptureFullScreen(bool allMonitors) =>
            Success(allMonitors ? 3840 : 1920, 1080);

        public CaptureOutcome CaptureRegion(int x, int y, int width, int height)
        {
            RegionCaptureCount++;
            return Success(width, height);
        }

        public CaptureOutcome CaptureWindow(IntPtr hWnd) => Success(800, 600);

        public IReadOnlyList<WindowInfo> ListTopLevelWindows() => new[]
        {
            new WindowInfo(new IntPtr(0x1000), "Test Window", "TestClass", new CaptureBounds(0, 0, 800, 600), 100, "test", true, false)
        };

        private static CaptureOutcome Success(int width, int height) => new()
        {
            Success = true,
            FilePath = "fake.png",
            Width = width,
            Height = height,
            Elapsed = TimeSpan.FromMilliseconds(5)
        };
    }

    private sealed class FakeHotkeyService : IHotkeyService
    {
        private int _nextId;

        public HotkeyRegistrationResult Register(
            VirtualKey key,
            KeyModifiers modifiers,
            string action) => new()
        {
            Success = true,
            Id = Interlocked.Increment(ref _nextId)
        };

        public bool Unregister(int id) => true;

        public void UnregisterAll()
        {
        }
    }

    private sealed class FakeInputService : IInputService
    {
        internal int SendTextCount;
        internal int LastDelay;
        internal int LastJitter;

        public void SendText(string text, int delayMs, int jitterMs)
        {
            SendTextCount++;
            LastDelay = delayMs;
            LastJitter = jitterMs;
        }

        public void SendKey(VirtualKey key, KeyModifiers modifiers)
        {
        }

        public void PostText(IntPtr hWnd, string text, int delayMs)
        {
        }

        public void PostKey(IntPtr hWnd, VirtualKey key)
        {
        }

        public bool SetWindowText(IntPtr hWnd, string text) => true;
    }

    private sealed class FakeHookService : IHookService
    {
        internal int ApplyCount;
        internal bool LastKeyboard;
        internal bool LastMouse;
        internal int? LastFilterPid;

        public void ApplyHookConfiguration(bool enableKeyboard, bool enableMouse, int? filterProcessId)
        {
            ApplyCount++;
            LastKeyboard = enableKeyboard;
            LastMouse = enableMouse;
            LastFilterPid = filterProcessId;
        }

        public void StartWindowEventHook(int processId)
        {
        }

        public void StopWindowEventHook()
        {
        }

        public IReadOnlyList<ProcessChoice> ListProcesses() => new[]
        {
            new ProcessChoice(100, "notepad"),
            new ProcessChoice(200, "explorer")
        };
    }
}