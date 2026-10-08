using System.Diagnostics;
using GameTools.App.ViewModels;
using GameTools.Core.Abstractions;
using GameTools.Core.Events;

namespace GameTools.App.Services;

/// <summary>
/// 基于 <see cref="ILowLevelHookManager"/> 与 <see cref="IWinEventHookManager"/> 的钩子服务实现。
/// </summary>
/// <remarks>
/// 负责把底层钩子事件桥接为界面可消费的文本，并把配置操作转换为对两个管理器的成对设置：
/// 「键盘与鼠标的目标进程相互独立」由底层管理器保证，本类不做合并。
/// </remarks>
public sealed class HookService : IHookService
{
    private readonly ILowLevelHookManager _hookManager;
    private readonly IWinEventHookManager _winEventManager;

    private Action<string>? _hookEventSink;
    private Action<string>? _winEventSink;

    private bool _keyboardEnabled;
    private bool _mouseEnabled;
    private int? _filterProcessId;

    /// <summary>
    /// 初始化钩子服务。
    /// </summary>
    /// <param name="hookManager">低级输入钩子管理器。</param>
    /// <param name="winEventManager">窗口事件监听管理器。</param>
    public HookService(ILowLevelHookManager hookManager, IWinEventHookManager winEventManager)
    {
        _hookManager = hookManager ?? throw new ArgumentNullException(nameof(hookManager));
        _winEventManager = winEventManager ?? throw new ArgumentNullException(nameof(winEventManager));

        _hookManager.KeyDown += OnKeyDown;
        _hookManager.KeyUp += OnKeyUp;
        _hookManager.MouseEvent += OnMouse;

        _winEventManager.WinEventReceived += OnWinEvent;
    }

    /// <summary>
    /// 设置钩子事件接收回调。
    /// </summary>
    /// <param name="sink">接收事件描述文本的回调。</param>
    public void SetHookEventSink(Action<string> sink) => _hookEventSink = sink;

    /// <summary>
    /// 设置窗口事件接收回调。
    /// </summary>
    /// <param name="sink">接收事件描述文本的回调。</param>
    public void SetWinEventSink(Action<string> sink) => _winEventSink = sink;

    /// <inheritdoc />
    public void ApplyHookConfiguration(bool enableKeyboard, bool enableMouse, int? filterProcessId)
    {
        if (filterProcessId is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(filterProcessId),
                filterProcessId,
                "过滤进程 ID 必须为正整数。");
        }

        // 先停后启，避免在运行中修改过滤目标导致状态不一致
        if (_keyboardEnabled && !enableKeyboard)
        {
            _hookManager.StopKeyboardHook();
        }

        if (_mouseEnabled && !enableMouse)
        {
            _hookManager.StopMouseHook();
        }

        if (enableKeyboard)
        {
            _hookManager.StartKeyboardHook(filterProcessId);
        }

        if (enableMouse)
        {
            _hookManager.StartMouseHook(filterProcessId);
        }

        _keyboardEnabled = enableKeyboard;
        _mouseEnabled = enableMouse;
        _filterProcessId = filterProcessId;
    }

    /// <inheritdoc />
    public void StartWindowEventHook(int processId)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processId),
                processId,
                "目标进程 ID 必须为正整数；0 在原生语义中表示监听整个桌面。");
        }

        _winEventManager.Start(processId);
    }

    /// <inheritdoc />
    public void StopWindowEventHook()
    {
        _winEventManager.Stop();
    }

    /// <inheritdoc />
    public IReadOnlyList<ProcessChoice> ListProcesses()
    {
        var result = new List<ProcessChoice>();

        foreach (Process proc in Process.GetProcesses())
        {
            using (proc)
            {
                try
                {
                    // 进程 ID 与名称是枚举结果的基本属性，读取失败意味着该进程已退出，跳过即可。
                    int processId = proc.Id;
                    string processName = proc.ProcessName;

                    // MainWindowHandle 仅用于界面标记：钩子目标也可能是控制台或后台服务，
                    // 不能据此过滤，否则绝大多数进程会从列表中消失。
                    bool hasMainWindow;
                    try
                    {
                        hasMainWindow = proc.MainWindowHandle != IntPtr.Zero;
                    }
                    catch (Exception ex)
                    {
                        // 跨进程窗口探测可能因权限不足失败，此时按“无窗口”呈现，不影响枚举结果
                        System.Diagnostics.Trace.WriteLine(
                            $"读取进程 {processId} 主窗口句柄失败: {ex.Message}");
                        hasMainWindow = false;
                    }

                    result.Add(new ProcessChoice(processId, processName, hasMainWindow));
                }
                catch (Exception ex)
                {
                    // 目标进程可能在枚举期间退出：属正常情况，跳过即可
                    System.Diagnostics.Trace.WriteLine($"枚举进程 {proc.Id} 失败: {ex.Message}");
                }
            }
        }

        // 有窗口的进程排在前，其余按名称序排列，便于在长列表中定位目标
        result.Sort((a, b) =>
        {
            int byWindow = b.HasMainWindow.CompareTo(a.HasMainWindow);
            return byWindow != 0
                ? byWindow
                : string.CompareOrdinal(a.ProcessName, b.ProcessName);
        });
        return result;
    }

    private void OnKeyDown(object? sender, KeyboardHookEventArgs e)
    {
        _hookEventSink?.Invoke(
            $"{DateTime.Now:HH:mm:ss.fff} [键盘↓] {e.Key} (VK:{e.KeyCode:X2}) [{e.ProcessName}:{e.ProcessId}] {(e.IsTargetProcess ? "命中" : "过滤")}");
    }

    private void OnKeyUp(object? sender, KeyboardHookEventArgs e)
    {
        _hookEventSink?.Invoke(
            $"{DateTime.Now:HH:mm:ss.fff} [键盘↑] {e.Key} (VK:{e.KeyCode:X2}) [{e.ProcessName}:{e.ProcessId}]");
    }

    private void OnMouse(object? sender, MouseHookEventArgs e)
    {
        _hookEventSink?.Invoke(
            $"{DateTime.Now:HH:mm:ss.fff} [鼠标] {e.Message:X4} @({e.Location.X},{e.Location.Y}) [{e.ProcessName}:{e.ProcessId}]");
    }

    private void OnWinEvent(object? sender, WinEventMessageEventArgs e)
    {
        _winEventSink?.Invoke(
            $"{DateTime.Now:HH:mm:ss.fff} {e.EventName} HWND=0x{e.WindowHandle:X} PID={e.ProcessId} Obj={e.ObjectId}/{e.ChildId}");
    }
}
