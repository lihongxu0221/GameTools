using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using GameTools.Core.Abstractions;
using GameTools.Core.Events;
using GameTools.Win32.Native;

namespace GameTools.Infrastructure.Hooks;

/// <summary>
/// 基于 SetWinEventHook 的跨进程窗口状态事件监听器
/// 无需将原生 DLL 注入目标进程，即可纯托管监听目标进程的窗口创建、激活、移动与销毁
/// </summary>
public sealed class WinEventHookManager : IWinEventHookManager
{
    private IntPtr _hWinEventHook = IntPtr.Zero;
    private WinEventDelegate? _proc;
    private GCHandle _gcHandle;

    private readonly Channel<RawWinEvent> _eventChannel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumerTask;
    private bool _disposed;

    public event EventHandler<WinEventMessageEventArgs>? WinEventReceived;

    public WinEventHookManager()
    {
        _eventChannel = Channel.CreateUnbounded<RawWinEvent>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = true
        });

        _consumerTask = Task.Run(ProcessEventsAsync);
    }

    /// <summary>
    /// 开始监听指定进程的窗口事件
    /// </summary>
    public void Start(int targetProcessId)
    {
        ThrowIfDisposed();
        if (_hWinEventHook != IntPtr.Zero) return;

        _proc = WinEventCallback;
        _gcHandle = GCHandle.Alloc(_proc, GCHandleType.Normal);

        _hWinEventHook = User32.SetWinEventHook(
            NativeConstants.EVENT_MIN,
            NativeConstants.EVENT_MAX,
            IntPtr.Zero,
            _proc,
            (uint)targetProcessId,
            0,
            NativeConstants.WINEVENT_OUTOFCONTEXT | NativeConstants.WINEVENT_SKIPOWNPROCESS);

        if (_hWinEventHook == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            if (_gcHandle.IsAllocated) _gcHandle.Free();
            _proc = null;
            throw new InvalidOperationException($"注册 WinEventHook 失败，错误码: {err}");
        }
    }

    /// <summary>
    /// 停止监听
    /// </summary>
    public void Stop()
    {
        if (_hWinEventHook != IntPtr.Zero)
        {
            User32.UnhookWinEvent(_hWinEventHook);
            _hWinEventHook = IntPtr.Zero;

            if (_gcHandle.IsAllocated)
            {
                _gcHandle.Free();
            }
            _proc = null;
        }
    }

    private void WinEventCallback(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hWnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        User32.GetWindowThreadProcessId(hWnd, out uint pid);

        _eventChannel.Writer.TryWrite(new RawWinEvent
        {
            EventType = eventType,
            WindowHandle = hWnd,
            ProcessId = (int)pid,
            ThreadId = (int)dwEventThread,
            ObjectId = idObject,
            ChildId = idChild
        });
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
                    string eventName = GetEventName(evt.EventType);
                    var args = new WinEventMessageEventArgs(
                        evt.EventType,
                        eventName,
                        evt.WindowHandle,
                        evt.ProcessId,
                        evt.ThreadId,
                        evt.ObjectId,
                        evt.ChildId);

                    WinEventReceived?.Invoke(this, args);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"派发 WinEvent 异常: {ex}");
                }
            }
        }
    }

    private static string GetEventName(uint eventType) => eventType switch
    {
        NativeConstants.EVENT_SYSTEM_FOREGROUND => "SYSTEM_FOREGROUND",
        NativeConstants.EVENT_OBJECT_CREATE => "OBJECT_CREATE",
        NativeConstants.EVENT_OBJECT_DESTROY => "OBJECT_DESTROY",
        NativeConstants.EVENT_OBJECT_SHOW => "OBJECT_SHOW",
        NativeConstants.EVENT_OBJECT_HIDE => "OBJECT_HIDE",
        NativeConstants.EVENT_OBJECT_NAMECHANGE => "OBJECT_NAMECHANGE",
        _ => $"EVENT_0x{eventType:X4}"
    };

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WinEventHookManager));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();

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

    private sealed record RawWinEvent
    {
        public uint EventType { get; init; }
        public IntPtr WindowHandle { get; init; }
        public int ProcessId { get; init; }
        public int ThreadId { get; init; }
        public long ObjectId { get; init; }
        public long ChildId { get; init; }
    }
}
