using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GameTools.Core.Abstractions;
using GameTools.Core.Enums;
using GameTools.Core.Events;
using GameTools.Win32.Native;

namespace GameTools.Infrastructure.Hotkeys;

/// <summary>
/// 基于 Win32 RegisterHotKey 的全局系统快捷键管理器
/// 与 BackgroundMessagePump 深度集成，提供线程安全的注册、注销与异步回调
/// </summary>
public sealed class Win32HotkeyManager : IHotkeyManager
{
    private readonly IBackgroundMessagePump _messagePump;
    private readonly ConcurrentDictionary<int, HotkeyRegistration> _registrations = new();
    private int _currentId;
    private bool _disposed;

    public event EventHandler<HotkeyEventArgs>? HotkeyTriggered;

    private sealed record HotkeyRegistration(
        int Id,
        Keys Key,
        KeyModifiers Modifiers,
        Action? Callback);

    public Win32HotkeyManager(IBackgroundMessagePump messagePump)
    {
        _messagePump = messagePump ?? throw new ArgumentNullException(nameof(messagePump));
        _messagePump.RegisterMessageFilter(NativeConstants.WM_HOTKEY, OnWmHotkey);
    }

    /// <summary>
    /// 注册一个全局快捷键
    /// </summary>
    public int RegisterHotkey(Keys key, KeyModifiers modifiers, Action? callback = null)
    {
        ThrowIfDisposed();

        int id = Interlocked.Increment(ref _currentId);
        IntPtr hWnd = _messagePump.MessageWindowHandle;

        if (hWnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("后台消息泵尚未初始化或句柄无效。");
        }

        uint fsModifiers = (uint)(modifiers & ~KeyModifiers.NoRepeat);
        if ((modifiers & KeyModifiers.NoRepeat) == KeyModifiers.NoRepeat)
        {
            fsModifiers |= 0x4000;
        }

        // Win32 要求 RegisterHotKey 必须在创建 hWnd 的同一个线程调用 (并在该线程获取 LastError)
        var (success, errorCode) = _messagePump.InvokeFunc(() =>
        {
            bool ok = User32.RegisterHotKey(hWnd, id, fsModifiers, (uint)key);
            int err = ok ? 0 : Marshal.GetLastWin32Error();
            return (ok, err);
        });

        if (!success)
        {
            if (errorCode == 1409) // ERROR_HOTKEY_ALREADY_REGISTERED
            {
                throw new InvalidOperationException($"快捷键 [{modifiers} + {key}] 已被系统或其他应用程序占用。");
            }

            throw new InvalidOperationException($"注册快捷键失败，错误代码: {errorCode}");
        }

        var reg = new HotkeyRegistration(id, key, modifiers, callback);
        _registrations[id] = reg;
        return id;
    }

    /// <summary>
    /// 注销指定 ID 的快捷键
    /// </summary>
    public bool UnregisterHotkey(int hotkeyId)
    {
        ThrowIfDisposed();

        if (_registrations.TryRemove(hotkeyId, out _))
        {
            IntPtr hWnd = _messagePump.MessageWindowHandle;
            if (hWnd != IntPtr.Zero)
            {
                return _messagePump.InvokeFunc(() => User32.UnregisterHotKey(hWnd, hotkeyId));
            }
        }

        return false;
    }

    /// <summary>
    /// 注销所有已注册的快捷键
    /// </summary>
    public void UnregisterAll()
    {
        ThrowIfDisposed();
        UnregisterAllInternal();
    }

    private void UnregisterAllInternal()
    {
        IntPtr hWnd = _messagePump.MessageWindowHandle;
        if (hWnd == IntPtr.Zero) return;

        _messagePump.InvokeAction(() =>
        {
            foreach (int id in _registrations.Keys)
            {
                if (_registrations.TryRemove(id, out _))
                {
                    User32.UnregisterHotKey(hWnd, id);
                }
            }
        });
    }

    private void OnWmHotkey(IntPtr wParam, IntPtr lParam)
    {
        int id = wParam.ToInt32();
        if (_registrations.TryGetValue(id, out var reg))
        {
            var args = new HotkeyEventArgs(reg.Id, reg.Key, reg.Modifiers);

            // 异步解耦派发，避免阻塞消息泵
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    reg.Callback?.Invoke();
                    HotkeyTriggered?.Invoke(this, args);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"执行快捷键回调异常: {ex}");
                }
            });
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        UnregisterAllInternal();
        _messagePump.UnregisterMessageFilter(NativeConstants.WM_HOTKEY);
    }
}
