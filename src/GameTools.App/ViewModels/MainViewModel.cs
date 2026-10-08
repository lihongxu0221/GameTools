using System.Collections.ObjectModel;
using GameTools.App.Services;
using GameTools.Core.Enums;
using GameTools.Core.Models;
using GameTools.Win32.Native;
using Prism.Mvvm;

namespace GameTools.App.ViewModels;

/// <summary>
/// 可供选择的按键项。
/// </summary>
/// <param name="Key">对应的 Win32 虚拟键码。</param>
/// <param name="DisplayName">界面显示名称。</param>
public sealed record KeyChoice(VirtualKey Key, string DisplayName)
{
    /// <inheritdoc />
    public override string ToString() => DisplayName;
}

/// <summary>
/// 可供选择的进程项。
/// </summary>
/// <param name="ProcessId">进程 ID。</param>
/// <param name="ProcessName">进程名。</param>
public sealed record ProcessChoice(int ProcessId, string ProcessName)
{
    /// <summary>
    /// 界面显示文本。
    /// </summary>
    public string Display => $"{ProcessName} ({ProcessId})";

    /// <inheritdoc />
    public override string ToString() => Display;
}

/// <summary>
/// 已注册快捷键的展示项。
/// </summary>
public sealed class HotkeyRow
{
    /// <summary>快捷键 ID。</summary>
    public int Id { get; init; }

    /// <summary>按键显示文本。</summary>
    public string KeyText { get; init; } = string.Empty;

    /// <summary>修饰键显示文本。</summary>
    public string ModifierText { get; init; } = string.Empty;

    /// <summary>触发次数。</summary>
    public int TriggerCount { get; set; }
}

/// <summary>
/// 主窗口视图模型。
/// </summary>
/// <remarks>
/// 设计要点：
/// 1. 所有 UI 操作通过 <see cref="DelegateCommand"/> 暴露，便于自动化测试替换执行路径；
/// 2. 钩子与 WinEvent 事件可能高频到达，统一经 <see cref="Enqueue"/> 汇入 UI 线程，
///    并对列表长度设上限，避免长时间运行导致内存无界增长；
/// 3. 所有异常转换为状态文本与日志，不向 UI 抛出，避免交互中断；
/// 4. 数值输入在提交时解析并做范围校验，非法输入给出明确提示。
/// </remarks>
public sealed class MainViewModel : BindableBase
{
    private const int MaxEventRows = 500;
    private const int MaxLogRows = 800;

    private readonly IScreenCaptureService _capture;
    private readonly IHotkeyService _hotkey;
    private readonly IInputService _input;
    private readonly IHookService _hooks;

    private bool _captureAllMonitors = true;
    private string _regionX = "0";
    private string _regionY = "0";
    private string _regionWidth = "800";
    private string _regionHeight = "600";
    private string _lastCaptureInfo = "尚未截图";
    private WindowInfo? _selectedWindow;
    private string _selectedWindowInfo = "未选择窗口";
    private ObservableCollection<WindowInfo> _windows = new();
    private KeyChoice? _selectedKey;
    private bool _modCtrl;
    private bool _modShift;
    private bool _modAlt;
    private bool _modWin;
    private bool _modNoRepeat = true;
    private string _hotkeyAction = string.Empty;
    private ObservableCollection<HotkeyRow> _hotkeys = new();
    private string _foregroundText = "Hello GameTools";
    private string _backgroundText = string.Empty;
    private string _charDelay = "10";
    private string _charJitter = "0";
    private KeyChoice? _selectedSendKey;
    private bool _sendModCtrl;
    private bool _sendModShift;
    private bool _sendModAlt;
    private bool _sendModWin;
    private string _inputResult = string.Empty;
    private bool _keyboardHookEnabled;
    private bool _mouseHookEnabled;
    private string _filterProcessId = string.Empty;
    private ProcessChoice? _selectedProcess;
    private ObservableCollection<ProcessChoice> _processes = new();
    private ObservableCollection<string> _hookEvents = new();
    private ObservableCollection<string> _winEvents = new();
    private ObservableCollection<string> _logEntries = new();
    private string _hookStatus = "钩子未启动";
    private string _winEventStatus = "未监听";
    private string _statusBar = "就绪";
    private string _statusSummary = string.Empty;

    /// <summary>
    /// 初始化视图模型。
    /// </summary>
    public MainViewModel(
        IScreenCaptureService capture,
        IHotkeyService hotkey,
        IInputService input,
        IHookService hooks)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _hotkey = hotkey ?? throw new ArgumentNullException(nameof(hotkey));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));

        AvailableKeys = BuildKeyChoices();
        _selectedKey = AvailableKeys.FirstOrDefault(k => k.Key == VirtualKey.F1);
        _selectedSendKey = AvailableKeys.FirstOrDefault(k => k.Key == VirtualKey.Enter);

        CaptureFullScreenCommand = new DelegateCommand(ExecuteCaptureFullScreen);
        CaptureRegionCommand = new DelegateCommand(ExecuteCaptureRegion);
        CaptureWindowCommand = new DelegateCommand(ExecuteCaptureWindow);
        PostTextToWindowCommand = new DelegateCommand(ExecutePostText);
        SetWindowTextCommand = new DelegateCommand(ExecuteSetWindowText);
        RegisterHotkeyCommand = new DelegateCommand(ExecuteRegisterHotkey);
        UnregisterHotkeyCommand = new DelegateCommand(ExecuteUnregisterHotkey);
        UnregisterAllHotkeysCommand = new DelegateCommand(ExecuteUnregisterAllHotkeys);
        SendForegroundTextCommand = new DelegateCommand(ExecuteSendText);
        SendKeyCommand = new DelegateCommand(ExecuteSendKey);
        PostKeyToWindowCommand = new DelegateCommand(ExecutePostKey);
        ApplyHookConfigCommand = new DelegateCommand(ExecuteApplyHookConfig);
        RefreshProcessesCommand = new DelegateCommand(RefreshProcesses);
        RefreshWindowsCommand = new DelegateCommand(RefreshWindows);
        StartWinEventHookCommand = new DelegateCommand(ExecuteStartWinEvent);
        StopWinEventHookCommand = new DelegateCommand(ExecuteStopWinEvent);
        ShutdownCommand = new DelegateCommand(ExecuteShutdown);

        RefreshWindows();
        RefreshProcesses();
    }

    /// <summary>
    /// 截图服务实例。供单元测试断言服务调用次数与参数。
    /// </summary>
    internal IScreenCaptureService CaptureService => _capture;

    /// <summary>
    /// 输入服务实例。供单元测试断言服务调用次数与参数。
    /// </summary>
    internal IInputService InputService => _input;

    /// <summary>
    /// 钩子服务实例。供单元测试断言服务调用次数与参数。
    /// </summary>
    internal IHookService HookService => _hooks;

    /// <summary>可选按键列表。</summary>
    public IReadOnlyList<KeyChoice> AvailableKeys { get; }

    /// <summary>全屏截图命令。</summary>
    public DelegateCommand CaptureFullScreenCommand { get; }

    /// <summary>区域截图命令。</summary>
    public DelegateCommand CaptureRegionCommand { get; }

    /// <summary>窗口截图命令。</summary>
    public DelegateCommand CaptureWindowCommand { get; }

    /// <summary>后台投递文本命令。</summary>
    public DelegateCommand PostTextToWindowCommand { get; }

    /// <summary>写入窗口文本命令。</summary>
    public DelegateCommand SetWindowTextCommand { get; }

    /// <summary>注册快捷键命令。</summary>
    public DelegateCommand RegisterHotkeyCommand { get; }

    /// <summary>注销快捷键命令。</summary>
    public DelegateCommand UnregisterHotkeyCommand { get; }

    /// <summary>注销全部快捷键命令。</summary>
    public DelegateCommand UnregisterAllHotkeysCommand { get; }

    /// <summary>前台输入文本命令。</summary>
    public DelegateCommand SendForegroundTextCommand { get; }

    /// <summary>前台按键命令。</summary>
    public DelegateCommand SendKeyCommand { get; }

    /// <summary>后台按键命令。</summary>
    public DelegateCommand PostKeyToWindowCommand { get; }

    /// <summary>应用钩子配置命令。</summary>
    public DelegateCommand ApplyHookConfigCommand { get; }

    /// <summary>刷新进程列表命令。</summary>
    public DelegateCommand RefreshProcessesCommand { get; }

    /// <summary>刷新窗口列表命令。</summary>
    public DelegateCommand RefreshWindowsCommand { get; }

    /// <summary>开始窗口事件监听命令。</summary>
    public DelegateCommand StartWinEventHookCommand { get; }

    /// <summary>停止窗口事件监听命令。</summary>
    public DelegateCommand StopWinEventHookCommand { get; }

    /// <summary>退出应用命令。</summary>
    public DelegateCommand ShutdownCommand { get; }

    /// <summary>是否包含全部显示器。</summary>
    public bool CaptureAllMonitors
    {
        get => _captureAllMonitors;
        set => SetProperty(ref _captureAllMonitors, value);
    }

    /// <summary>区域起点 X。</summary>
    public string RegionX
    {
        get => _regionX;
        set => SetProperty(ref _regionX, value);
    }

    /// <summary>区域起点 Y。</summary>
    public string RegionY
    {
        get => _regionY;
        set => SetProperty(ref _regionY, value);
    }

    /// <summary>区域宽度。</summary>
    public string RegionWidth
    {
        get => _regionWidth;
        set => SetProperty(ref _regionWidth, value);
    }

    /// <summary>区域高度。</summary>
    public string RegionHeight
    {
        get => _regionHeight;
        set => SetProperty(ref _regionHeight, value);
    }

    /// <summary>最近一次截图结果描述。</summary>
    public string LastCaptureInfo
    {
        get => _lastCaptureInfo;
        set => SetProperty(ref _lastCaptureInfo, value);
    }

    /// <summary>可选窗口列表。</summary>
    public ObservableCollection<WindowInfo> Windows
    {
        get => _windows;
        set => SetProperty(ref _windows, value);
    }

    /// <summary>当前选中的窗口。</summary>
    public WindowInfo? SelectedWindow
    {
        get => _selectedWindow;
        set
        {
            if (SetProperty(ref _selectedWindow, value) && value != null)
            {
                SelectedWindowInfo = $"{value.ProcessName} · PID {value.ProcessId} · {value.Bounds.Width}x{value.Bounds.Height}";
            }
        }
    }

    /// <summary>选中窗口的描述文本。</summary>
    public string SelectedWindowInfo
    {
        get => _selectedWindowInfo;
        set => SetProperty(ref _selectedWindowInfo, value);
    }

    /// <summary>当前选中的按键。</summary>
    public KeyChoice? SelectedKey
    {
        get => _selectedKey;
        set => SetProperty(ref _selectedKey, value);
    }

    /// <summary>是否按下 Ctrl 修饰键。</summary>
    public bool ModCtrl
    {
        get => _modCtrl;
        set => SetProperty(ref _modCtrl, value);
    }

    /// <summary>是否按下 Shift 修饰键。</summary>
    public bool ModShift
    {
        get => _modShift;
        set => SetProperty(ref _modShift, value);
    }

    /// <summary>是否按下 Alt 修饰键。</summary>
    public bool ModAlt
    {
        get => _modAlt;
        set => SetProperty(ref _modAlt, value);
    }

    /// <summary>是否按下 Win 修饰键。</summary>
    public bool ModWin
    {
        get => _modWin;
        set => SetProperty(ref _modWin, value);
    }

    /// <summary>是否抑制重复触发。</summary>
    public bool ModNoRepeat
    {
        get => _modNoRepeat;
        set => SetProperty(ref _modNoRepeat, value);
    }

    /// <summary>快捷键触发后要执行的命令文本。</summary>
    public string HotkeyAction
    {
        get => _hotkeyAction;
        set => SetProperty(ref _hotkeyAction, value);
    }

    /// <summary>已注册快捷键列表。</summary>
    public ObservableCollection<HotkeyRow> Hotkeys
    {
        get => _hotkeys;
        set => SetProperty(ref _hotkeys, value);
    }

    /// <summary>前台输入文本内容。</summary>
    public string ForegroundText
    {
        get => _foregroundText;
        set => SetProperty(ref _foregroundText, value);
    }

    /// <summary>后台投递文本内容。</summary>
    public string BackgroundText
    {
        get => _backgroundText;
        set => SetProperty(ref _backgroundText, value);
    }

    /// <summary>字符间隔毫秒数。</summary>
    public string CharDelay
    {
        get => _charDelay;
        set => SetProperty(ref _charDelay, value);
    }

    /// <summary>字符间隔随机抖动毫秒数。</summary>
    public string CharJitter
    {
        get => _charJitter;
        set => SetProperty(ref _charJitter, value);
    }

    /// <summary>当前选中的模拟按键。</summary>
    public KeyChoice? SelectedSendKey
    {
        get => _selectedSendKey;
        set => SetProperty(ref _selectedSendKey, value);
    }

    /// <summary>模拟按键是否带 Ctrl。</summary>
    public bool SendModCtrl
    {
        get => _sendModCtrl;
        set => SetProperty(ref _sendModCtrl, value);
    }

    /// <summary>模拟按键是否带 Shift。</summary>
    public bool SendModShift
    {
        get => _sendModShift;
        set => SetProperty(ref _sendModShift, value);
    }

    /// <summary>模拟按键是否带 Alt。</summary>
    public bool SendModAlt
    {
        get => _sendModAlt;
        set => SetProperty(ref _sendModAlt, value);
    }

    /// <summary>模拟按键是否带 Win。</summary>
    public bool SendModWin
    {
        get => _sendModWin;
        set => SetProperty(ref _sendModWin, value);
    }

    /// <summary>输入操作结果描述。</summary>
    public string InputResult
    {
        get => _inputResult;
        set => SetProperty(ref _inputResult, value);
    }

    /// <summary>是否启用键盘钩子。</summary>
    public bool KeyboardHookEnabled
    {
        get => _keyboardHookEnabled;
        set => SetProperty(ref _keyboardHookEnabled, value);
    }

    /// <summary>是否启用鼠标钩子。</summary>
    public bool MouseHookEnabled
    {
        get => _mouseHookEnabled;
        set => SetProperty(ref _mouseHookEnabled, value);
    }

    /// <summary>钩子过滤目标进程 ID 文本。</summary>
    public string FilterProcessId
    {
        get => _filterProcessId;
        set => SetProperty(ref _filterProcessId, value);
    }

    /// <summary>可选进程列表。</summary>
    public ObservableCollection<ProcessChoice> Processes
    {
        get => _processes;
        set => SetProperty(ref _processes, value);
    }

    /// <summary>当前选中的进程。</summary>
    public ProcessChoice? SelectedProcess
    {
        get => _selectedProcess;
        set
        {
            if (SetProperty(ref _selectedProcess, value) && value != null)
            {
                FilterProcessId = value.ProcessId.ToString();
            }
        }
    }

    /// <summary>钩子事件流。</summary>
    public ObservableCollection<string> HookEvents
    {
        get => _hookEvents;
        set => SetProperty(ref _hookEvents, value);
    }

    /// <summary>窗口事件流。</summary>
    public ObservableCollection<string> WinEvents
    {
        get => _winEvents;
        set => SetProperty(ref _winEvents, value);
    }

    /// <summary>日志视图。</summary>
    public ObservableCollection<string> LogEntries
    {
        get => _logEntries;
        set => SetProperty(ref _logEntries, value);
    }

    /// <summary>钩子状态描述。</summary>
    public string HookStatus
    {
        get => _hookStatus;
        set => SetProperty(ref _hookStatus, value);
    }

    /// <summary>窗口事件监听状态。</summary>
    public string WinEventStatus
    {
        get => _winEventStatus;
        set => SetProperty(ref _winEventStatus, value);
    }

    /// <summary>状态栏文本。</summary>
    public string StatusBar
    {
        get => _statusBar;
        set => SetProperty(ref _statusBar, value);
    }

    /// <summary>顶部摘要文本。</summary>
    public string StatusSummary
    {
        get => _statusSummary;
        set => SetProperty(ref _statusSummary, value);
    }

    /// <summary>
    /// 应用退出请求，由宿主在收到时触发。
    /// </summary>
    public event EventHandler? ShutdownRequested;

    /// <summary>
    /// 刷新可见窗口列表。
    /// </summary>
    public void RefreshWindows()
    {
        try
        {
            IReadOnlyList<WindowInfo> found = _capture.ListTopLevelWindows();
            Windows = new ObservableCollection<WindowInfo>(found.Take(200));
            StatusBar = $"已发现 {found.Count} 个可见窗口";
        }
        catch (Exception ex)
        {
            ReportError("枚举窗口失败", ex);
        }
    }

    /// <summary>
    /// 刷新进程列表。
    /// </summary>
    public void RefreshProcesses()
    {
        try
        {
            Processes = new ObservableCollection<ProcessChoice>(_hooks.ListProcesses());
        }
        catch (Exception ex)
        {
            ReportError("枚举进程失败", ex);
        }
    }

    /// <summary>
    /// 将钩子事件追加到事件流。
    /// </summary>
    /// <param name="message">事件描述。</param>
    public void AppendHookEvent(string message) => Enqueue(HookEvents, message);

    /// <summary>
    /// 将窗口事件追加到事件流。
    /// </summary>
    /// <param name="message">事件描述。</param>
    public void AppendWinEvent(string message) => Enqueue(WinEvents, message);

    /// <summary>
    /// 将日志追加到日志视图。
    /// </summary>
    /// <param name="message">日志内容。</param>
    public void AppendLog(string message) => Enqueue(LogEntries, message);

    private void ExecuteCaptureFullScreen()
    {
        try
        {
            CaptureOutcome outcome = _capture.CaptureFullScreen(CaptureAllMonitors);
            LastCaptureInfo = outcome.Success
                ? $"已保存 {outcome.FilePath}（{outcome.Width}x{outcome.Height}，{outcome.Elapsed.TotalMilliseconds:F0}ms，{outcome.Mode}）"
                : $"截图失败：{outcome.ErrorMessage}";
            StatusBar = LastCaptureInfo;
        }
        catch (Exception ex)
        {
            ReportError("全屏截图失败", ex);
        }
    }

    private void ExecuteCaptureRegion()
    {
        if (!TryParseRegion(out int x, out int y, out int width, out int height))
        {
            return;
        }

        try
        {
            CaptureOutcome outcome = _capture.CaptureRegion(x, y, width, height);
            LastCaptureInfo = outcome.Success
                ? $"已保存 {outcome.FilePath}（{outcome.Width}x{outcome.Height}，{outcome.Elapsed.TotalMilliseconds:F0}ms）"
                : $"截图失败：{outcome.ErrorMessage}";
            StatusBar = LastCaptureInfo;
        }
        catch (Exception ex)
        {
            ReportError("区域截图失败", ex);
        }
    }

    private void ExecuteCaptureWindow()
    {
        if (SelectedWindow == null)
        {
            StatusBar = "请先选择目标窗口";
            return;
        }

        try
        {
            CaptureOutcome outcome = _capture.CaptureWindow(SelectedWindow.Handle);
            LastCaptureInfo = outcome.Success
                ? $"已保存 {outcome.FilePath}（{outcome.Width}x{outcome.Height}，模式 {outcome.Mode}）"
                : $"窗口截图失败：{outcome.ErrorMessage}";
            StatusBar = LastCaptureInfo;
        }
        catch (Exception ex)
        {
            ReportError("窗口截图失败", ex);
        }
    }

    private void ExecutePostText()
    {
        if (SelectedWindow == null)
        {
            StatusBar = "请先选择目标窗口";
            return;
        }

        try
        {
            int delay = ClampDelay(CharDelay, out bool delayValid);
            if (!delayValid)
            {
                return;
            }

            _input.PostText(SelectedWindow.Handle, BackgroundText, delay);
            InputResult = $"已向 0x{SelectedWindow.Handle.ToInt64():X} 投递 {BackgroundText.Length} 个字符";
            StatusBar = InputResult;
        }
        catch (Exception ex)
        {
            ReportError("后台投递文本失败", ex);
        }
    }

    private void ExecuteSetWindowText()
    {
        if (SelectedWindow == null)
        {
            StatusBar = "请先选择目标窗口";
            return;
        }

        try
        {
            string text = string.IsNullOrEmpty(BackgroundText) ? ForegroundText : BackgroundText;
            bool ok = _input.SetWindowText(SelectedWindow.Handle, text);
            InputResult = ok ? "窗口文本已替换" : "窗口文本写入失败（可能被 UIPI 拒绝或目标不可写）";
            StatusBar = InputResult;
        }
        catch (Exception ex)
        {
            ReportError("写入窗口文本失败", ex);
        }
    }

    private void ExecuteRegisterHotkey()
    {
        if (SelectedKey == null)
        {
            StatusBar = "请先选择按键";
            return;
        }

        try
        {
            KeyModifiers modifiers = BuildModifiers();
            HotkeyRegistrationResult result = _hotkey.Register(SelectedKey.Key, modifiers, HotkeyAction);
            if (!result.Success)
            {
                StatusBar = $"快捷键注册失败：{result.ErrorMessage}";
                return;
            }

            Hotkeys.Add(new HotkeyRow
            {
                Id = result.Id,
                KeyText = SelectedKey.DisplayName,
                ModifierText = DescribeModifiers(modifiers)
            });

            StatusBar = $"已注册快捷键 #{result.Id}（{DescribeModifiers(modifiers)} + {SelectedKey.DisplayName}）";
        }
        catch (Exception ex)
        {
            ReportError("注册快捷键失败", ex);
        }
    }

    private void ExecuteUnregisterHotkey()
    {
        if (Hotkeys.Count == 0)
        {
            StatusBar = "没有已注册的快捷键";
            return;
        }

        HotkeyRow? last = Hotkeys[Hotkeys.Count - 1];
        try
        {
            bool ok = _hotkey.Unregister(last.Id);
            if (ok)
            {
                Hotkeys.Remove(last);
                StatusBar = $"已注销快捷键 #{last.Id}";
            }
            else
            {
                StatusBar = $"注销快捷键 #{last.Id} 失败";
            }
        }
        catch (Exception ex)
        {
            ReportError("注销快捷键失败", ex);
        }
    }

    private void ExecuteUnregisterAllHotkeys()
    {
        try
        {
            _hotkey.UnregisterAll();
            Hotkeys.Clear();
            StatusBar = "已注销全部快捷键";
        }
        catch (Exception ex)
        {
            ReportError("批量注销快捷键失败", ex);
        }
    }

    private void ExecuteSendText()
    {
        try
        {
            int delay = ClampDelay(CharDelay, out bool delayValid);
            if (!delayValid)
            {
                return;
            }

            int jitter = ClampDelay(CharJitter, out bool jitterValid);
            if (!jitterValid)
            {
                return;
            }

            _input.SendText(ForegroundText, delay, jitter);
            InputResult = $"已向前台窗口发送 {ForegroundText.Length} 个字符（间隔 {delay}ms，抖动 ±{jitter}ms）";
            StatusBar = InputResult;
        }
        catch (Exception ex)
        {
            ReportError("前台输入失败", ex);
        }
    }

    private void ExecuteSendKey()
    {
        if (SelectedSendKey == null)
        {
            StatusBar = "请先选择按键";
            return;
        }

        try
        {
            _input.SendKey(SelectedSendKey.Key, BuildSendModifiers());
            InputResult = $"已发送按键 {SelectedSendKey.DisplayName}";
            StatusBar = InputResult;
        }
        catch (Exception ex)
        {
            ReportError("前台按键失败", ex);
        }
    }

    private void ExecutePostKey()
    {
        if (SelectedWindow == null)
        {
            StatusBar = "请先选择目标窗口";
            return;
        }

        if (SelectedSendKey == null)
        {
            StatusBar = "请先选择按键";
            return;
        }

        try
        {
            _input.PostKey(SelectedWindow.Handle, SelectedSendKey.Key);
            InputResult = $"已向 0x{SelectedWindow.Handle.ToInt64():X} 投递按键 {SelectedSendKey.DisplayName}";
            StatusBar = InputResult;
        }
        catch (Exception ex)
        {
            ReportError("后台按键失败", ex);
        }
    }

    private void ExecuteApplyHookConfig()
    {
        int? filter = null;
        if (!string.IsNullOrWhiteSpace(FilterProcessId))
        {
            if (!int.TryParse(FilterProcessId, out int pid) || pid <= 0)
            {
                StatusBar = "过滤进程 ID 必须为正整数";
                return;
            }

            filter = pid;
        }

        try
        {
            _hooks.ApplyHookConfiguration(KeyboardHookEnabled, MouseHookEnabled, filter);
            HookStatus = $"键盘={(KeyboardHookEnabled ? "开" : "关")}，鼠标={(MouseHookEnabled ? "开" : "关")}，过滤={(filter.HasValue ? filter.Value.ToString() : "全局")}";
            StatusBar = "钩子配置已应用";
        }
        catch (Exception ex)
        {
            ReportError("应用钩子配置失败", ex);
        }
    }

    private void ExecuteStartWinEvent()
    {
        if (SelectedProcess == null)
        {
            StatusBar = "请先选择目标进程";
            return;
        }

        try
        {
            _hooks.StartWindowEventHook(SelectedProcess.ProcessId);
            WinEventStatus = $"正在监听 PID {SelectedProcess.ProcessId}（{SelectedProcess.ProcessName}）";
            StatusBar = WinEventStatus;
        }
        catch (Exception ex)
        {
            WinEventStatus = $"启动失败：{ex.Message}";
            ReportError("启动窗口事件监听失败", ex);
        }
    }

    private void ExecuteStopWinEvent()
    {
        try
        {
            _hooks.StopWindowEventHook();
            WinEventStatus = "未监听";
            StatusBar = "窗口事件监听已停止";
        }
        catch (Exception ex)
        {
            ReportError("停止窗口事件监听失败", ex);
        }
    }

    private void ExecuteShutdown()
    {
        ShutdownRequested?.Invoke(this, EventArgs.Empty);
        StatusBar = "正在退出...";
    }

    private bool TryParseRegion(out int x, out int y, out int width, out int height)
    {
        x = y = width = height = 0;

        if (!int.TryParse(RegionX, out x) ||
            !int.TryParse(RegionY, out y) ||
            !int.TryParse(RegionWidth, out width) ||
            !int.TryParse(RegionHeight, out height))
        {
            StatusBar = "区域坐标与宽高必须为整数";
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            StatusBar = "区域宽高必须大于 0";
            return false;
        }

        if (width > 20000 || height > 20000)
        {
            StatusBar = "区域宽超出可接受范围（上限 20000）";
            return false;
        }

        return true;
    }

    private int ClampDelay(string raw, out bool valid)
    {
        valid = true;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0;
        }

        if (!int.TryParse(raw, out int value))
        {
            StatusBar = "延时间隔必须为整数";
            valid = false;
            return 0;
        }

        if (value < 0)
        {
            StatusBar = "延时间隔不能为负数";
            valid = false;
            return 0;
        }

        // 上限保护：避免误填超大数值导致界面长时间无响应
        return Math.Min(value, 5000);
    }

    private KeyModifiers BuildModifiers()
    {
        KeyModifiers modifiers = KeyModifiers.None;

        if (ModCtrl)
        {
            modifiers |= KeyModifiers.Control;
        }

        if (ModShift)
        {
            modifiers |= KeyModifiers.Shift;
        }

        if (ModAlt)
        {
            modifiers |= KeyModifiers.Alt;
        }

        if (ModWin)
        {
            modifiers |= KeyModifiers.Windows;
        }

        if (ModNoRepeat)
        {
            modifiers |= KeyModifiers.NoRepeat;
        }

        return modifiers;
    }

    private KeyModifiers BuildSendModifiers()
    {
        KeyModifiers modifiers = KeyModifiers.None;

        if (SendModCtrl)
        {
            modifiers |= KeyModifiers.Control;
        }

        if (SendModShift)
        {
            modifiers |= KeyModifiers.Shift;
        }

        if (SendModAlt)
        {
            modifiers |= KeyModifiers.Alt;
        }

        if (SendModWin)
        {
            modifiers |= KeyModifiers.Windows;
        }

        return modifiers;
    }

    private static string DescribeModifiers(KeyModifiers modifiers)
    {
        List<string> parts = new();

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(KeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        if (modifiers.HasFlag(KeyModifiers.NoRepeat))
        {
            parts.Add("NoRepeat");
        }

        return parts.Count == 0 ? "无" : string.Join("+", parts);
    }

    private void ReportError(string context, Exception ex)
    {
        StatusBar = $"{context}：{ex.Message}";
        AppendLog($"[错误] {context}：{ex.GetType().Name} {ex.Message}");
    }

    private void Enqueue(ObservableCollection<string> target, string message)
    {
        void Append()
        {
            target.Add(message);

            int limit = ReferenceEquals(target, LogEntries) ? MaxLogRows : MaxEventRows;
            while (target.Count > limit)
            {
                target.RemoveAt(0);
            }
        }

        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher &&
            !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(Append);
            return;
        }

        Append();
    }

    private static IReadOnlyList<KeyChoice> BuildKeyChoices()
    {
        return new[]
        {
            new KeyChoice(VirtualKey.F1, "F1"), new KeyChoice(VirtualKey.F2, "F2"),
            new KeyChoice(VirtualKey.F3, "F3"), new KeyChoice(VirtualKey.F4, "F4"),
            new KeyChoice(VirtualKey.F5, "F5"), new KeyChoice(VirtualKey.F6, "F6"),
            new KeyChoice(VirtualKey.F7, "F7"), new KeyChoice(VirtualKey.F8, "F8"),
            new KeyChoice(VirtualKey.F9, "F9"), new KeyChoice(VirtualKey.F10, "F10"),
            new KeyChoice(VirtualKey.F11, "F11"), new KeyChoice(VirtualKey.F12, "F12"),
            new KeyChoice(VirtualKey.A, "A"), new KeyChoice(VirtualKey.B, "B"),
            new KeyChoice(VirtualKey.C, "C"), new KeyChoice(VirtualKey.D, "D"),
            new KeyChoice(VirtualKey.E, "E"), new KeyChoice(VirtualKey.F, "F"),
            new KeyChoice(VirtualKey.G, "G"), new KeyChoice(VirtualKey.H, "H"),
            new KeyChoice(VirtualKey.I, "I"), new KeyChoice(VirtualKey.J, "J"),
            new KeyChoice(VirtualKey.K, "K"), new KeyChoice(VirtualKey.L, "L"),
            new KeyChoice(VirtualKey.M, "M"), new KeyChoice(VirtualKey.N, "N"),
            new KeyChoice(VirtualKey.O, "O"), new KeyChoice(VirtualKey.P, "P"),
            new KeyChoice(VirtualKey.Q, "Q"), new KeyChoice(VirtualKey.R, "R"),
            new KeyChoice(VirtualKey.S, "S"), new KeyChoice(VirtualKey.T, "T"),
            new KeyChoice(VirtualKey.U, "U"), new KeyChoice(VirtualKey.V, "V"),
            new KeyChoice(VirtualKey.W, "W"), new KeyChoice(VirtualKey.X, "X"),
            new KeyChoice(VirtualKey.Y, "Y"), new KeyChoice(VirtualKey.Z, "Z"),
            new KeyChoice(VirtualKey.D0, "0"), new KeyChoice(VirtualKey.D1, "1"),
            new KeyChoice(VirtualKey.D2, "2"), new KeyChoice(VirtualKey.D3, "3"),
            new KeyChoice(VirtualKey.D4, "4"), new KeyChoice(VirtualKey.D5, "5"),
            new KeyChoice(VirtualKey.D6, "6"), new KeyChoice(VirtualKey.D7, "7"),
            new KeyChoice(VirtualKey.D8, "8"), new KeyChoice(VirtualKey.D9, "9"),
            new KeyChoice(VirtualKey.Enter, "Enter"), new KeyChoice(VirtualKey.Escape, "Esc"),
            new KeyChoice(VirtualKey.Tab, "Tab"), new KeyChoice(VirtualKey.Back, "Backspace"),
            new KeyChoice(VirtualKey.Space, "Space"), new KeyChoice(VirtualKey.Insert, "Insert"),
            new KeyChoice(VirtualKey.Delete, "Delete"), new KeyChoice(VirtualKey.Home, "Home"),
            new KeyChoice(VirtualKey.End, "End"), new KeyChoice(VirtualKey.PageUp, "PageUp"),
            new KeyChoice(VirtualKey.PageDown, "PageDown"),
            new KeyChoice(VirtualKey.Left, "←"), new KeyChoice(VirtualKey.Right, "→"),
            new KeyChoice(VirtualKey.Up, "↑"), new KeyChoice(VirtualKey.Down, "↓"),
            new KeyChoice(VirtualKey.Pause, "Pause"), new KeyChoice(VirtualKey.PrintScreen, "PrintScreen"),
            new KeyChoice(VirtualKey.CapsLock, "CapsLock"), new KeyChoice(VirtualKey.NumLock, "NumLock"),
            new KeyChoice(VirtualKey.Scroll, "ScrollLock")
        };
    }
}
