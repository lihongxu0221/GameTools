using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Windows.Forms;
using GameTools.Core.Abstractions;
using GameTools.Core.Events;
using GameTools.Win32.Native;

namespace GameTools.Infrastructure.Hooks;

/// <summary>
/// 高性能全局低级键盘与鼠标钩子管理器
/// 具备 GCHandle 防 GC 回收保护、Channel 异步管道零阻塞处理以及目标进程精准过滤
/// </summary>
public sealed class LowLevelHookManager : ILowLevelHookManager
{
    private IntPtr _hKeyboardHook = IntPtr.Zero;
    private IntPtr _hMouseHook = IntPtr.Zero;

    private HookProc? _keyboardProc;
    private HookProc? _mouseProc;
    private GCHandle _keyboardGcHandle;
    private GCHandle _mouseGcHandle;

    private int? _targetProcessId;
    private readonly Channel<RawInputEvent> _eventChannel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumerTask;
    private bool _disposed;

    public event EventHandler<KeyboardHookEventArgs>? KeyDown;
    public event EventHandler<KeyboardHookEventArgs>? KeyUp;
    public event EventHandler<MouseHookEventArgs>? MouseEvent;

    public LowLevelHookManager()
    {
        // 创建单消费者高性能内存通道
        _eventChannel = Channel.CreateUnbounded<RawInputEvent>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = true
        });

        _consumerTask = Task.Run(ProcessEventsAsync);
    }

    /// <summary>
    /// 启动低级键盘钩子
    /// </summary>
    public void StartKeyboardHook(int? targetProcessId = null)
    {
        ThrowIfDisposed();
        if (_hKeyboardHook != IntPtr.Zero) return;

        _targetProcessId = targetProcessId;
        _keyboardProc = KeyboardHookCallback;
        _keyboardGcHandle = GCHandle.Alloc(_keyboardProc, GCHandleType.Normal);

        IntPtr hModule = Kernel32.GetModuleHandle(null);
        _hKeyboardHook = User32.SetWindowsHookEx(
            NativeConstants.WH_KEYBOARD_LL,
            _keyboardProc,
            hModule,
            0);

        if (_hKeyboardHook == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            if (_keyboardGcHandle.IsAllocated) _keyboardGcHandle.Free();
            _keyboardProc = null;
            throw new InvalidOperationException($"安装全局键盘钩子失败，错误码: {err}");
        }
    }

    /// <summary>
    /// 停止低级键盘钩子
    /// </summary>
    public void StopKeyboardHook()
    {
        if (_hKeyboardHook != IntPtr.Zero)
        {
            User32.UnhookWindowsHookEx(_hKeyboardHook);
            _hKeyboardHook = IntPtr.Zero;

            if (_keyboardGcHandle.IsAllocated)
            {
                _keyboardGcHandle.Free();
            }
            _keyboardProc = null;
        }
    }

    /// <summary>
    /// 启动低级鼠标钩子
    /// </summary>
    public void StartMouseHook(int? targetProcessId = null)
    {
        ThrowIfDisposed();
        if (_hMouseHook != IntPtr.Zero) return;

        _targetProcessId = targetProcessId;
        _mouseProc = MouseHookCallback;
        _mouseGcHandle = GCHandle.Alloc(_mouseProc, GCHandleType.Normal);

        IntPtr hModule = Kernel32.GetModuleHandle(null);
        _hMouseHook = User32.SetWindowsHookEx(
            NativeConstants.WH_MOUSE_LL,
            _mouseProc,
            hModule,
            0);

        if (_hMouseHook == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            if (_mouseGcHandle.IsAllocated) _mouseGcHandle.Free();
            _mouseProc = null;
            throw new InvalidOperationException($"安装全局鼠标钩子失败，错误码: {err}");
        }
    }

    /// <summary>
    /// 停止低级鼠标钩子
    /// </summary>
    public void StopMouseHook()
    {
        if (_hMouseHook != IntPtr.Zero)
        {
            User32.UnhookWindowsHookEx(_hMouseHook);
            _hMouseHook = IntPtr.Zero;

            if (_mouseGcHandle.IsAllocated)
            {
                _mouseGcHandle.Free();
            }
            _mouseProc = null;
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            uint msg = (uint)wParam;
            var kbStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            // 零阻塞写入异步管道
            _eventChannel.Writer.TryWrite(new RawInputEvent
            {
                IsKeyboard = true,
                Message = msg,
                VkCode = (int)kbStruct.VkCode,
                ScanCode = (int)kbStruct.ScanCode
            });
        }

        return User32.CallNextHookEx(_hKeyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            uint msg = (uint)wParam;
            var msStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

            _eventChannel.Writer.TryWrite(new RawInputEvent
            {
                IsKeyboard = false,
                Message = msg,
                MouseLocation = new Point(msStruct.Pt.X, msStruct.Pt.Y),
                MouseData = (int)msStruct.MouseData
            });
        }

        return User32.CallNextHookEx(_hMouseHook, nCode, wParam, lParam);
    }

    private async Task ProcessEventsAsync()
    {
        var reader = _eventChannel.Reader;
        while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
        {
            while (reader.TryRead(out var evt))
            {
                try
                {
                    DispatchEvent(evt);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"派发钩子事件异常: {ex}");
                }
            }
        }
    }

    private void DispatchEvent(RawInputEvent evt)
    {
        IntPtr fgHwnd = User32.GetForegroundWindow();
        User32.GetWindowThreadProcessId(fgHwnd, out uint pid);
        int procId = (int)pid;

        bool isTarget = !_targetProcessId.HasValue || _targetProcessId.Value == procId;
        string procName = GetProcessName(procId);

        if (evt.IsKeyboard)
        {
            bool isDown = evt.Message is NativeConstants.WM_KEYDOWN or NativeConstants.WM_SYSKEYDOWN;
            var args = new KeyboardHookEventArgs(
                (Keys)evt.VkCode,
                evt.VkCode,
                isDown,
                fgHwnd,
                procId,
                procName,
                isTarget);

            if (_targetProcessId.HasValue && !isTarget)
            {
                // 仅监听目标进程，过滤非目标事件
                return;
            }

            if (isDown)
            {
                KeyDown?.Invoke(this, args);
            }
            else
            {
                KeyUp?.Invoke(this, args);
            }
        }
        else
        {
            var args = new MouseHookEventArgs(
                evt.Message,
                evt.MouseLocation,
                evt.MouseData,
                fgHwnd,
                procId,
                procName,
                isTarget);

            if (_targetProcessId.HasValue && !isTarget)
            {
                return;
            }

            MouseEvent?.Invoke(this, args);
        }
    }

    private static string GetProcessName(int pid)
    {
        if (pid <= 0) return "Idle";
        try
        {
            using var proc = Process.GetProcessById(pid);
            return proc.ProcessName;
        }
        catch
        {
            return "Unknown";
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LowLevelHookManager));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopKeyboardHook();
        StopMouseHook();

        _cts.Cancel();
        _eventChannel.Writer.Complete();

        try
        {
            _consumerTask.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch
        {
            // 忽略终止等待异常
        }

        _cts.Dispose();
    }

    private sealed record RawInputEvent
    {
        public bool IsKeyboard { get; init; }
        public uint Message { get; init; }
        public int VkCode { get; init; }
        public int ScanCode { get; init; }
        public Point MouseLocation { get; init; }
        public int MouseData { get; init; }
    }
}
