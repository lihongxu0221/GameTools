using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using GameTools.Core.Abstractions;
using GameTools.Core.Enums;
using GameTools.Core.Events;
using GameTools.Win32.Native;

namespace GameTools.Infrastructure.Hotkeys;

/// <summary>
/// 基于 Win32 RegisterHotKey 的全局系统快捷键管理器。
/// 与 <see cref="BackgroundMessagePump"/> 深度集成，保证注册与注销发生在消息线程。
/// </summary>
/// <remarks>
/// 关键约束：
/// 1. Win32 要求 RegisterHotKey 必须在创建 HWND 的同一个线程调用，且在该线程读取 LastError，
///    因此注册与注销一律通过消息泵的同步调度完成；
/// 2. 冲突需区分「本进程重复注册」与「被系统或其他进程占用」，
///    前者可安全重试，后者必须以明确异常告知调用方；
/// 3. 注销顺序为「先原生后状态」：原生注销失败时保留状态以便按原 ID 重试清理。
/// </remarks>
public sealed class Win32HotkeyManager : IHotkeyManager
{
    private readonly IBackgroundMessagePump _messagePump;
    private readonly ConcurrentDictionary<int, HotkeyRegistration> _registrations = new();
    private int _currentId;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<HotkeyEventArgs>? HotkeyTriggered;

    /// <summary>
    /// 已注册的快捷键。
    /// </summary>
    private sealed record HotkeyRegistration(
        int Id,
        VirtualKey Key,
        KeyModifiers Modifiers,
        Action? Callback);

    /// <summary>
    /// 初始化快捷键管理器并订阅消息泵的 WM_HOTKEY。
    /// </summary>
    /// <param name="messagePump">后台消息泵。</param>
    public Win32HotkeyManager(IBackgroundMessagePump messagePump)
    {
        _messagePump = messagePump ?? throw new ArgumentNullException(nameof(messagePump));
        _messagePump.RegisterMessageFilter(NativeConstants.WM_HOTKEY, OnWmHotkey);
    }

    /// <inheritdoc />
    public int RegisterHotkey(VirtualKey key, KeyModifiers modifiers, Action? callback = null)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Win32HotkeyManager));
        }

        IntPtr hWnd = RequireWindowHandle();

        uint fsModifiers = ToWin32Modifiers(modifiers);
        int id = Interlocked.Increment(ref _currentId);

        // 先查重：本进程重复注册属可恢复错误，与被外部占用区分处理
        if (_registrations.Values.Any(r => r.Key == key && ToWin32Modifiers(r.Modifiers) == fsModifiers))
        {
            throw new InvalidOperationException(
                $"快捷键 [{DescribeModifiers(modifiers)} + {key}] 已在当前进程中注册，请先注销。");
        }

        // RegisterHotKey 必须在创建 HWND 的线程调用，并在该线程读取 LastError
        (bool success, int errorCode) = _messagePump.InvokeFunc(() =>
        {
            bool ok = User32.RegisterHotKey(hWnd, id, fsModifiers, (uint)key);
            return (ok, ok ? 0 : Marshal.GetLastWin32Error());
        }, TimeSpan.FromSeconds(5), CancellationToken.None);


        if (!success)
        {
            if (errorCode == NativeConstants.ERROR_HOTKEY_ALREADY_REGISTERED)
            {
                throw new InvalidOperationException(
                    $"快捷键 [{DescribeModifiers(modifiers)} + {key}] 已被系统或其他应用程序占用。");
            }

            throw new InvalidOperationException(
                $"注册快捷键失败，错误代码: {errorCode}" +
                (errorCode == NativeConstants.ERROR_WINDOW_OF_OTHER_THREAD
                    ? "（目标窗口线程与消息线程不一致）"
                    : string.Empty));
        }

        _registrations[id] = new HotkeyRegistration(id, key, modifiers, callback);
        return id;
    }

    /// <inheritdoc />
    public bool UnregisterHotkey(int hotkeyId)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Win32HotkeyManager));
        }

        if (!_registrations.TryGetValue(hotkeyId, out HotkeyRegistration? registration))
        {
            return false;
        }

        IntPtr hWnd = RequireWindowHandle();

        // 先原生注销，成功后再移除状态，避免注销失败时丢失可重试的信息
        bool unregistered = _messagePump.InvokeFunc(
            () => User32.UnregisterHotKey(hWnd, hotkeyId),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        if (unregistered)
        {
            _registrations.TryRemove(hotkeyId, out _);
        }
        else
        {
            System.Diagnostics.Trace.WriteLine($"注销快捷键 #{hotkeyId} 失败，错误码 {Marshal.GetLastWin32Error()}");
        }

        return unregistered;
    }

    /// <inheritdoc />
    public void UnregisterAll()
    {
        if (_disposed)
        {
            return;
        }

        UnregisterAllInternal();
    }

    private void UnregisterAllInternal()
    {
        if (_registrations.IsEmpty)
        {
            return;
        }

        IntPtr hWnd = MessageWindowHandleOrZero();
        if (hWnd == IntPtr.Zero)
        {
            // 消息泵已停止：原生注销已无意义，仅清理托管状态
            _registrations.Clear();
            return;
        }

        try
        {
            _messagePump.InvokeAction(() =>
            {
                foreach (KeyValuePair<int, HotkeyRegistration> pair in _registrations.ToArray())
                {
                    User32.UnregisterHotKey(hWnd, pair.Key);
                    _registrations.TryRemove(pair.Key, out _);
                }
            });
        }
        catch (Exception ex)
        {
            // 消息泵不可用时仍需清理状态，避免残留导致下次启动误判为已注册
            System.Diagnostics.Trace.WriteLine($"批量注销快捷键失败: {ex}");
            _registrations.Clear();
        }
    }


    private void OnWmHotkey(IntPtr wParam, IntPtr lParam)
    {
        _ = lParam;

        int id = wParam.ToInt32();
        if (!_registrations.TryGetValue(id, out HotkeyRegistration? registration))
        {
            return;
        }

        var args = new HotkeyEventArgs(registration.Id, registration.Key, registration.Modifiers);

        // 异步派发，避免业务处理阻塞消息线程导致 WM_HOTKEY 堆积
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                registration.Callback?.Invoke();
                HotkeyTriggered?.Invoke(this, args);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"执行快捷键回调异常: {ex}");
            }
        });
    }

    private IntPtr RequireWindowHandle()
    {
        IntPtr handle = MessageWindowHandleOrZero();

        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("后台消息泵尚未初始化或句柄无效，无法注册快捷键。");
        }

        return handle;
    }

    private IntPtr MessageWindowHandleOrZero()
    {
        try
        {
            return _messagePump.MessageWindowHandle;
        }
        catch (ObjectDisposedException)
        {
            return IntPtr.Zero;
        }
    }

    private static uint ToWin32Modifiers(KeyModifiers modifiers)
    {
        uint result = (uint)(modifiers & ~KeyModifiers.NoRepeat);

        if ((modifiers & KeyModifiers.NoRepeat) == KeyModifiers.NoRepeat)
        {
            result |= 0x4000u; // MOD_NOREPEAT
        }

        return result;
    }

    private static string DescribeModifiers(KeyModifiers modifiers)
    {
        var parts = new List<string>();

        if ((modifiers & KeyModifiers.Control) == KeyModifiers.Control)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & KeyModifiers.Shift) == KeyModifiers.Shift)
        {
            parts.Add("Shift");
        }

        if ((modifiers & KeyModifiers.Alt) == KeyModifiers.Alt)
        {
            parts.Add("Alt");
        }

        if ((modifiers & KeyModifiers.Windows) == KeyModifiers.Windows)
        {
            parts.Add("Win");
        }

        return parts.Count == 0 ? "无修饰键" : string.Join("+", parts);
    }

    /// <summary>
    /// 释放资源：注销全部快捷键并取消消息订阅。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        UnregisterAllInternal();

        try
        {
            _messagePump.UnregisterMessageFilter(NativeConstants.WM_HOTKEY);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"取消 WM_HOTKEY 订阅失败: {ex}");
        }
    }
}
