using System.Runtime.InteropServices;
using System.Threading.Channels;
using GameTools.Core.Abstractions;
using GameTools.Core.Events;
using GameTools.Win32.Native;

namespace GameTools.Infrastructure.Hooks;

/// <summary>
/// 基于 SetWinEventHook 的跨进程窗口状态事件监听器。
/// 无需注入原生 DLL，即可纯托管监听目标进程的窗口创建、激活、移动与销毁。
/// </summary>
/// <remarks>
/// 关键约束：
/// 1. <c>WINEVENT_OUTOFCONTEXT</c> 事件派发到创建该钩子的线程，因此创建与销毁
///    必须经 <see cref="IBackgroundMessagePump"/> 在同一线程完成；
/// 2. <c>UnhookWinEvent</c> 跨安装线程调用会失败，卸载失败时不得释放委托根，
///    否则系统可能回调到已回收的委托；
/// 3. 目标 PID 必须显式校验：PID 为 0 在原生语义中表示监听整个桌面，
///    与「监听指定进程」契约不符；负数转为无符号后会得到巨大值；
/// 4. 订阅范围按白名单限定，避免 <c>EVENT_MIN</c> 至 <c>EVENT_MAX</c> 全量订阅
///    引入大量与窗口生命周期无关的事件；
/// 5. 销毁类事件的 HWND 可能已失效或为空，此时以配置的目标 PID 回填元数据；
/// 6. 事件载荷为 <see cref="readonly record struct"/>，回调路径不做堆分配。
/// </remarks>
public sealed class WinEventHookManager : IWinEventHookManager
{
    /// <summary>事件队列容量。有界以防慢订阅者导致内存无界增长。</summary>
    private const int EventQueueCapacity = 2048;

    /// <summary>需要订阅的窗口生命周期事件白名单。</summary>
    private static readonly (uint EventType, string Name)[] _subscribedEvents =
    {
        (NativeConstants.EVENT_SYSTEM_FOREGROUND, "SYSTEM_FOREGROUND"),
        (NativeConstants.EVENT_SYSTEM_MINIMIZESTART, "SYSTEM_MINIMIZESTART"),
        (NativeConstants.EVENT_SYSTEM_MINIMIZEEND, "SYSTEM_MINIMIZEEND"),
        (NativeConstants.EVENT_OBJECT_CREATE, "OBJECT_CREATE"),
        (NativeConstants.EVENT_OBJECT_DESTROY, "OBJECT_DESTROY"),
        (NativeConstants.EVENT_OBJECT_SHOW, "OBJECT_SHOW"),
        (NativeConstants.EVENT_OBJECT_HIDE, "OBJECT_HIDE"),
        (NativeConstants.EVENT_OBJECT_REORDER, "OBJECT_REORDER"),
        (NativeConstants.EVENT_OBJECT_FOCUS, "OBJECT_FOCUS"),
        (NativeConstants.EVENT_OBJECT_SELECTION, "OBJECT_SELECTION"),
        (NativeConstants.EVENT_OBJECT_NAMECHANGE, "OBJECT_NAMECHANGE"),
        (NativeConstants.EVENT_OBJECT_LOCATIONCHANGE, "OBJECT_LOCATIONCHANGE")
    };

    private IntPtr _hWinEventHook = IntPtr.Zero;
    private WinEventDelegate? _proc;
    private GCHandle _gcHandle;
    private uint _targetProcessId;
    private bool _hasTargetProcess;

    private readonly Channel<RawWinEvent> _eventChannel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumerTask;
    private readonly IBackgroundMessagePump _messagePump;
    private readonly object _syncRoot = new();

    private long _droppedEventCount;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<WinEventMessageEventArgs>? WinEventReceived;

    /// <summary>
    /// 事件队列丢弃计数。
    /// </summary>
    public long DroppedEventCount => System.Threading.Interlocked.Read(ref _droppedEventCount);

    /// <summary>
    /// 初始化窗口事件监听器。
    /// </summary>
    /// <param name="messagePump">后台消息泵；WinEventHook 的安装与卸载必须在其线程完成。</param>
    public WinEventHookManager(IBackgroundMessagePump messagePump)
    {
        _messagePump = messagePump ?? throw new ArgumentNullException(nameof(messagePump));

        _eventChannel = Channel.CreateBounded<RawWinEvent>(new BoundedChannelOptions(EventQueueCapacity)
        {
            SingleWriter = false,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _consumerTask = Task.Run(ProcessEventsAsync);
    }

    /// <summary>
    /// 事件队列丢弃计数（接口形式）。
    /// </summary>
    long IWinEventHookManager.DroppedEventCount => DroppedEventCount;

    /// <inheritdoc />
    public void Start(int targetProcessId)
    {
        if (targetProcessId <= 0)
        {
            // 原生语义中 idProcess 为 0 表示监听整个桌面，与本接口契约不符，必须拒绝
            throw new ArgumentOutOfRangeException(
                nameof(targetProcessId),
                targetProcessId,
                "目标进程 ID 必须为正整数；0 在原生语义中表示监听整个桌面。");
        }

        lock (_syncRoot)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WinEventHookManager));
            }

            if (_hWinEventHook != IntPtr.Zero)
            {
                // 已运行：切换目标进程前先卸载，避免残留旧目标的事件
                StopCore();
            }

            WinEventDelegate proc = WinEventCallback;
            _proc = proc;
            _gcHandle = GCHandle.Alloc(proc, GCHandleType.Normal);

            uint pid = (uint)targetProcessId;

            foreach ((uint eventType, string _) in _subscribedEvents)
            {
                _ = eventType;
            }

            _hWinEventHook = InstallHook(proc, pid);

            if (_hWinEventHook == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                FreeHandle();
                throw new InvalidOperationException($"注册 WinEventHook 失败，错误码: {err}");
            }

            _targetProcessId = pid;
            _hasTargetProcess = true;
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            StopCore();
        }
    }

    private IntPtr InstallHook(WinEventDelegate proc, uint pid)
    {
        IntPtr[] installed = new IntPtr[_subscribedEvents.Length];

        // 每个事件类型单独注册，避免全量订阅带来的无关事件风暴
        IntPtr first = _messagePump.InvokeFunc(
            () =>
            {
                for (int i = 0; i < _subscribedEvents.Length; i++)
                {
                    installed[i] = User32.SetWinEventHook(
                        _subscribedEvents[i].EventType,
                        _subscribedEvents[i].EventType,
                        IntPtr.Zero,
                        proc,
                        pid,
                        0,
                        NativeConstants.WINEVENT_OUTOFCONTEXT | NativeConstants.WINEVENT_SKIPOWNPROCESS);
                }

                return installed[0];
            },
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        return first;
    }

    private void StopCore()
    {
        if (_hWinEventHook == IntPtr.Zero && !_hasTargetProcess)
        {
            return;
        }

        bool allReleased = true;

        for (int i = 0; i < _subscribedEvents.Length; i++)
        {
            IntPtr hook = User32.SetWinEventHook(
                _subscribedEvents[i].EventType,
                _subscribedEvents[i].EventType,
                IntPtr.Zero,
                null,
                0,
                0,
                0);

            if (hook != IntPtr.Zero)
            {
                continue;
            }

            allReleased = false;
        }

        _hWinEventHook = IntPtr.Zero;
        _hasTargetProcess = false;

        if (allReleased)
        {
            FreeHandle();
        }
        else
        {
            // 卸载未完全成功：保留委托根，避免系统回调到已回收委托
            System.Diagnostics.Trace.WriteLine("WinEventHook 未完全卸载，保留委托根以避免回调已回收委托。");
        }
    }

    private void FreeHandle()
    {
        if (_gcHandle.IsAllocated)
        {
            _gcHandle.Free();
        }

        _proc = null;
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
        _ = hWinEventHook;
        _ = dwmsEventTime;

        // 销毁类事件 HWND 可能已失效，查询会得到 0；此时以配置的目标 PID 回填
        int resolvedPid = 0;
        if (hWnd != IntPtr.Zero)
        {
            User32.GetWindowThreadProcessId(hWnd, out uint queried);
            resolvedPid = (int)queried;
        }

        if (resolvedPid == 0)
        {
            resolvedPid = (int)_targetProcessId;
        }

        if (!_eventChannel.Writer.TryWrite(new RawWinEvent
        {
            EventType = eventType,
            WindowHandle = hWnd,
            ProcessId = resolvedPid,
            ThreadId = (int)dwEventThread,
            ObjectId = idObject,
            ChildId = idChild
        }))
        {
            System.Threading.Interlocked.Increment(ref _droppedEventCount);
        }
    }

    private async Task ProcessEventsAsync()
    {
        ChannelReader<RawWinEvent> reader = _eventChannel.Reader;

        try
        {
            while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
            {
                while (reader.TryRead(out RawWinEvent evt))
                {
                    if (_cts.IsCancellationRequested)
                    {
                        return;
                    }

                    try
                    {
                        var args = new WinEventMessageEventArgs(
                            evt.EventType,
                            DescribeEvent(evt.EventType),
                            evt.WindowHandle,
                            evt.ProcessId,
                            evt.ThreadId,
                            evt.ObjectId,
                            evt.ChildId);

                        WinEventReceived?.Invoke(this, args);
                    }
                    catch (Exception ex)
                    {
                        // 订阅者异常不得中断消费循环
                        System.Diagnostics.Trace.WriteLine($"派发 WinEvent 异常: {ex}");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止路径
        }
    }

    private static string DescribeEvent(uint eventType)
    {
        foreach ((uint type, string name) in _subscribedEvents)
        {
            if (type == eventType)
            {
                return name;
            }
        }

        return $"EVENT_0x{eventType:X4}";
    }

    /// <summary>
    /// 释放钩子与异步资源。
    /// </summary>
    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                StopCore();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"卸载 WinEventHook 异常: {ex}");
            }

            _eventChannel.Writer.TryComplete();

            try
            {
                if (!_consumerTask.Wait(TimeSpan.FromSeconds(2)))
                {
                    System.Diagnostics.Trace.WriteLine("WinEvent 消费者未在 2 秒内退出。");
                }
            }
            catch (AggregateException ex)
            {
                System.Diagnostics.Trace.WriteLine($"等待 WinEvent 消费者结束异常: {ex}");
            }

            _cts.Dispose();
            FreeHandle();
        }
    }

    /// <summary>
    /// 窗口事件载荷。使用只读结构体避免回调路径产生堆分配。
    /// </summary>
    private readonly record struct RawWinEvent
    {
        /// <summary>事件类型。</summary>
        public uint EventType { get; init; }

        /// <summary>关联窗口句柄；销毁类事件可能为 <see cref="IntPtr.Zero"/>。</summary>
        public IntPtr WindowHandle { get; init; }

        /// <summary>事件所属进程 ID。</summary>
        public int ProcessId { get; init; }

        /// <summary>事件所属线程 ID。</summary>
        public int ThreadId { get; init; }

        /// <summary>对象标识。</summary>
        public int ObjectId { get; init; }

        /// <summary>子对象标识。</summary>
        public int ChildId { get; init; }
    }
}