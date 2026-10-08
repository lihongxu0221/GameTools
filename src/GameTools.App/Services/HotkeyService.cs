using GameTools.App.Services;
using GameTools.Core.Abstractions;
using GameTools.Core.Enums;
using System.Windows.Threading;

namespace GameTools.App.Services;

/// <summary>
/// 基于 <see cref="IHotkeyManager"/> 的快捷键服务实现。
/// </summary>
/// <remarks>
/// 负责把「动作文本」解释为可执行命令并调度到 UI 线程，
/// 使 ViewModel 不必关心热键注册细节与线程模型。
/// </remarks>
public sealed class HotkeyService : IHotkeyService
{
    private static readonly Dictionary<string, Action> _commandTable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["capture"] = () => { },
            ["screenshot"] = () => { },
            ["hook"] = () => { },
            ["hooks"] = () => { },
            ["quit"] = () => { },
            ["exit"] = () => { }
        };

    private readonly IHotkeyManager _hotkeyManager;
    private readonly Dictionary<int, Action> _callbacks = new();
    private readonly Dictionary<int, string> _actions = new();
    private readonly Dispatcher _dispatcher;
    private int _nextId = 1;

    /// <summary>
    /// 初始化快捷键服务。
    /// </summary>
    /// <param name="hotkeyManager">底层快捷键管理器。</param>
    /// <param name="dispatcher">UI 调度器；热键回调需切换到该线程执行。</param>
    public HotkeyService(IHotkeyManager hotkeyManager, Dispatcher dispatcher)
    {
        _hotkeyManager = hotkeyManager ?? throw new ArgumentNullException(nameof(hotkeyManager));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <inheritdoc />
    public HotkeyRegistrationResult Register(VirtualKey key, KeyModifiers modifiers, string action)
    {
        int id = Interlocked.Increment(ref _nextId);

        if (!_commandTable.TryGetValue(action ?? string.Empty, out Action? handler))
        {
            return new HotkeyRegistrationResult
            {
                Success = false,
                ErrorMessage = $"未知命令「{action}」；可用命令：{string.Join("、", _commandTable.Keys)}"
            };
        }

        try
        {
            int registered = _hotkeyManager.RegisterHotkey(key, modifiers, () =>
            {
                // 热键回调来自线程池，必须切回 UI 线程才能安全更新界面
                if (_dispatcher.CheckAccess())
                {
                    handler();
                }
                else
                {
                    _dispatcher.BeginInvoke(handler);
                }
            });

            _callbacks[registered] = handler ?? (() => { });
            _actions[registered] = action ?? string.Empty;

            return new HotkeyRegistrationResult { Success = true, Id = registered };
        }
        catch (InvalidOperationException ex)
        {
            return new HotkeyRegistrationResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <inheritdoc />
    public bool Unregister(int id)
    {
        _callbacks.Remove(id);
        _actions.Remove(id);
        return _hotkeyManager.UnregisterHotkey(id);
    }

    /// <inheritdoc />
    public void UnregisterAll()
    {
        _callbacks.Clear();
        _actions.Clear();
        _hotkeyManager.UnregisterAll();
    }

    /// <summary>
    /// 列出可用命令名，供界面提示。
    /// </summary>
    public static IReadOnlyCollection<string> AvailableCommands => _commandTable.Keys;
}
