using System.Runtime.InteropServices;
using System.Threading.Channels;
using GameTools.Core.Abstractions;
using GameTools.Core.Enums;
using GameTools.Core.Events;
using GameTools.Core.Models;
using GameTools.Win32.Helpers;
using GameTools.Win32.Native;

namespace GameTools.Infrastructure.Hooks;

/// <summary>
/// 全局低级键盘与鼠标钩子管理器。
/// 采用委托强引用加 <see cref="GCHandle"/> 保活、有界 Channel 异步管道与目标进程过滤。
/// </summary>
/// <remarks>
/// 关键约束：
/// 1. <c>WH_KEYBOARD_LL</c> 与 <c>WH_MOUSE_LL</c> 要求安装线程持续运行消息循环，
///    因此安装与卸载必须经 <see cref="IBackgroundMessagePump"/> 在同一线程完成；
/// 2. 钩子回调有超时限制（<c>LowLevelHooksTimeout</c>），回调内只做结构体读取与
///    <c>TryWrite</c>，不做任何阻塞或分配重操作；
/// 3. 事件载荷为 <see cref="readonly record struct"/>，避免每个输入事件产生堆分配；
/// 4. 键盘与鼠标的过滤目标相互独立，避免后启动者覆盖先启动者的配置；
/// 5. 前台进程 ID 在事件入队时采样，避免消费端延迟导致归属错判；
/// 6. 进程名查询走 <see cref="ProcessInfoCache"/>，避免每个事件打开进程对象。
/// </remarks>
public sealed class LowLevelHookManager : ILowLevelHookManager
{
    /// <summary>Windows 默认低级钩子超时（毫秒）。超时会静默卸载钩子。</summary>
    private const int LowLevelHooksTimeoutMs = 1000;

    /// <summary>事件队列容量。有界以防慢订阅者导致内存无界增长。</summary>
    private const int EventQueueCapacity = 4096;

    private IntPtr _hKeyboardHook = IntPtr.Zero;
    private IntPtr _hMouseHook = IntPtr.Zero;

    private HookProc? _keyboardProc;
    private HookProc? _mouseProc;
    private GCHandle _keyboardGcHandle;
    private GCHandle _mouseGcHandle;

    private int? _keyboardTargetProcessId;
    private int? _mouseTargetProcessId;

    private readonly Channel<RawInputEvent> _eventChannel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumerTask;
    private readonly IBackgroundMessagePump _messagePump;
    private readonly object _syncRoot = new();
    private long _droppedEventCount;

    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<KeyboardHookEventArgs>? KeyDown;

    /// <inheritdoc />
    public event EventHandler<KeyboardHookEventArgs>? KeyUp;

    /// <inheritdoc />
    public event EventHandler<MouseHookEventArgs>? MouseEvent;

    /// <summary>
    /// 事件队列丢弃计数。用于评估容量是否需要调整。
    /// </summary>
    public long DroppedEventCount => System.Threading.Interlocked.Read(ref _droppedEventCount);



    /// <summary>
    /// 初始化钩子管理器。
    /// </summary>
    /// <param name="messagePump">后台消息泵；低级钩子的安装与卸载必须在其线程完成。</param>
    public LowLevelHookManager(IBackgroundMessagePump messagePump)
    {
        _messagePump = messagePump ?? throw new ArgumentNullException(nameof(messagePump));

        _eventChannel = Channel.CreateBounded<RawInputEvent>(new BoundedChannelOptions(EventQueueCapacity)
        {
            SingleWriter = false,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _consumerTask = Task.Run(ProcessEventsAsync);
    }

    /// <inheritdoc />
    public void StartKeyboardHook(int? targetProcessId = null)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();

            if (_hKeyboardHook != IntPtr.Zero)
            {
                // 已在运行：更新过滤目标而非重复安装
                _keyboardTargetProcessId = targetProcessId;
                return;
            }

            HookProc proc = KeyboardHookCallback;
            _keyboardProc = proc;
            _keyboardGcHandle = GCHandle.Alloc(proc, GCHandleType.Normal);

            IntPtr hModule = Kernel32.GetModuleHandle(null);

            _hKeyboardHook = _messagePump.InvokeFunc(
                () => User32.SetWindowsHookEx(NativeConstants.WH_KEYBOARD_LL, proc, hModule, 0),
                TimeSpan.FromSeconds(5),
                CancellationToken.None);

            if (_hKeyboardHook == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                FreeHandle(ref _keyboardGcHandle);
                _keyboardProc = null;

                throw new InvalidOperationException($"安装全局键盘钩子失败，错误码: {err}");
            }

            _keyboardTargetProcessId = targetProcessId;
            SetHookTimeout(LowLevelHooksTimeoutMs);
        }
    }

    /// <inheritdoc />
    public void StartMouseHook(int? targetProcessId = null)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();

            if (_hMouseHook != IntPtr.Zero)
            {
                _mouseTargetProcessId = targetProcessId;
                return;
            }

            HookProc proc = MouseHookCallback;
            _mouseProc = proc;
            _mouseGcHandle = GCHandle.Alloc(proc, GCHandleType.Normal);

            IntPtr hModule = Kernel32.GetModuleHandle(null);

            _hMouseHook = _messagePump.InvokeFunc(
                () => User32.SetWindowsHookEx(NativeConstants.WH_MOUSE_LL, proc, hModule, 0),
                TimeSpan.FromSeconds(5),
                CancellationToken.None);

            if (_hMouseHook == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                FreeHandle(ref _mouseGcHandle);
                _mouseProc = null;

                throw new InvalidOperationException($"安装全局鼠标钩子失败，错误码: {err}");
            }

            _mouseTargetProcessId = targetProcessId;
            SetHookTimeout(LowLevelHooksTimeoutMs);
        }
    }

    /// <inheritdoc />
    public void StopKeyboardHook()
    {
        lock (_syncRoot)
        {
            if (_hKeyboardHook == IntPtr.Zero)
            {
                return;
            }

            IntPtr hook = _hKeyboardHook;
            _hKeyboardHook = IntPtr.Zero;

            TryUnhook(() => User32.UnhookWindowsHookEx(hook), "键盘");

            // 卸载失败时不得释放委托根，否则系统仍可能回调已回收的委托
            if (_hKeyboardHook == IntPtr.Zero)
            {
                FreeHandle(ref _keyboardGcHandle);
                _keyboardProc = null;
            }

            _keyboardTargetProcessId = null;
        }
    }

    /// <inheritdoc />
    public void StopMouseHook()
    {
        lock (_syncRoot)
        {
            if (_hMouseHook == IntPtr.Zero)
            {
                return;
            }

            IntPtr hook = _hMouseHook;
            _hMouseHook = IntPtr.Zero;

            TryUnhook(() => User32.UnhookWindowsHookEx(hook), "鼠标");

            if (_hMouseHook == IntPtr.Zero)
            {
                FreeHandle(ref _mouseGcHandle);
                _mouseProc = null;
            }

            _mouseTargetProcessId = null;
        }
    }

    private void TryUnhook(Func<bool> unhook, string name)
    {
        try
        {
            bool ok = _messagePump.InvokeFunc(unhook, TimeSpan.FromSeconds(5), CancellationToken.None);
            if (!ok)
            {
                System.Diagnostics.Trace.WriteLine($"卸载{name}钩子失败，错误码 {Marshal.GetLastWin32Error()}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"卸载{name}钩子异常: {ex}");
        }
    }

    private static void FreeHandle(ref GCHandle handle)
    {
        if (handle.IsAllocated)
        {
            handle.Free();
        }
    }

    private static void SetHookTimeout(int timeoutMs)
    {
        // 注册表路径为 HKCU，不影响其他进程；失败仅影响诊断精度
        try
        {
            using Microsoft.Win32.RegistryKey? key =
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer", writable: true);

            key?.SetValue("LowLevelHooksTimeout", timeoutMs, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"设置 LowLevelHooksTimeout 失败: {ex}");
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            KBDLLHOOKSTRUCT kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            // 事件发生时即采样前台窗口与进程，保证归属准确
            SampleForeground(out IntPtr fgHwnd, out int fgPid, out string fgName);

            if (!_eventChannel.Writer.TryWrite(new RawInputEvent
            {
                IsKeyboard = true,
                Message = unchecked((uint)wParam.ToInt64()),
                VkCode = (int)kb.VkCode,
                ScanCode = (int)kb.ScanCode,
                ForegroundWindow = fgHwnd,
                ProcessId = fgPid,
                ProcessName = fgName,
                TargetProcessId = _keyboardTargetProcessId
            }))
            {
                System.Threading.Interlocked.Increment(ref _droppedEventCount);
            }
        }

        return User32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            MSLLHOOKSTRUCT ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

            SampleForeground(out IntPtr fgHwnd, out int fgPid, out string fgName);

            if (!_eventChannel.Writer.TryWrite(new RawInputEvent
            {
                IsKeyboard = false,
                Message = unchecked((uint)wParam.ToInt64()),
                MouseX = ms.Pt.X,
                MouseY = ms.Pt.Y,
                MouseData = unchecked((int)ms.MouseData),
                ForegroundWindow = fgHwnd,
                ProcessId = fgPid,
                ProcessName = fgName,
                TargetProcessId = _mouseTargetProcessId
            }))
            {
                System.Threading.Interlocked.Increment(ref _droppedEventCount);
            }
        }

        return User32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private static void SampleForeground(out IntPtr fgHwnd, out int fgPid, out string fgName)
    {
        fgHwnd = User32.GetForegroundWindow();
        User32.GetWindowThreadProcessId(fgHwnd, out uint pid);
        fgPid = (int)pid;
        fgName = ProcessInfoCache.TryGetProcessName(fgPid);
    }

    private async Task ProcessEventsAsync()
    {
        ChannelReader<RawInputEvent> reader = _eventChannel.Reader;

        try
        {
            while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
            {
                while (reader.TryRead(out RawInputEvent evt))
                {
                    if (_cts.IsCancellationRequested)
                    {
                        return;
                    }

                    try
                    {
                        DispatchEvent(evt);
                    }
                    catch (Exception ex)
                    {
                        // 订阅者异常不得中断消费循环，否则事件将永久积压
                        System.Diagnostics.Trace.WriteLine($"派发钩子事件异常: {ex}");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止路径
        }
    }

    private void DispatchEvent(RawInputEvent evt)
    {
        int? filter = evt.TargetProcessId;
        bool isTarget = !filter.HasValue || filter.Value == evt.ProcessId;

        if (evt.IsKeyboard)
        {
            bool isDown = evt.Message is NativeConstants.WM_KEYDOWN or NativeConstants.WM_SYSKEYDOWN;

            var args = new KeyboardHookEventArgs(
                (VirtualKey)(ushort)evt.VkCode,
                evt.VkCode,
                isDown,
                evt.ForegroundWindow,
                evt.ProcessId,
                evt.ProcessName,
                isTarget);

            EventHandler<KeyboardHookEventArgs>? handler = isDown ? KeyDown : KeyUp;
            handler?.Invoke(this, args);
        }
        else
        {
            var args = new MouseHookEventArgs(
                evt.Message,
                new CaptureBounds(evt.MouseX, evt.MouseY, 0, 0),
                evt.MouseData,
                evt.ForegroundWindow,
                evt.ProcessId,
                evt.ProcessName,
                isTarget);

            MouseEvent?.Invoke(this, args);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LowLevelHookManager));
        }
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

            StopKeyboardHook();
            StopMouseHook();

            // 完成写入端并等待消费者退出，避免释放后仍派发事件
            _eventChannel.Writer.TryComplete();

            try
            {
                if (!_consumerTask.Wait(TimeSpan.FromSeconds(2)))
                {
                    System.Diagnostics.Trace.WriteLine("钩子事件消费者未在 2 秒内退出。");
                }
            }
            catch (AggregateException ex)
            {
                System.Diagnostics.Trace.WriteLine($"等待钩子消费者结束异常: {ex}");
            }

            _cts.Dispose();
        }
    }

    /// <summary>
    /// 钩子事件载荷。
    /// </summary>
    /// <remarks>
    /// 使用只读结构体以避免每个输入事件产生堆分配；回调路径的性能直接决定是否触发
    /// <c>LowLevelHooksTimeout</c>，因此不得改为引用类型。
    /// </remarks>
    private readonly record struct RawInputEvent
    {
        /// <summary>是否来自键盘钩子。</summary>
        public bool IsKeyboard { get; init; }

        /// <summary>原生消息标识。</summary>
        public uint Message { get; init; }

        /// <summary>虚拟键码。</summary>
        public int VkCode { get; init; }

        /// <summary>扫描码。</summary>
        public int ScanCode { get; init; }

        /// <summary>鼠标 X 坐标。</summary>
        public int MouseX { get; init; }

        /// <summary>鼠标 Y 坐标。</summary>
        public int MouseY { get; init; }

        /// <summary>鼠标附加数据。</summary>
        public int MouseData { get; init; }

        /// <summary>事件发生时的前台窗口句柄。</summary>
        public IntPtr ForegroundWindow { get; init; }

        /// <summary>事件发生时的前台进程 ID。</summary>
        public int ProcessId { get; init; }

        /// <summary>事件发生时的前台进程名。</summary>
        public string ProcessName { get; init; }

        /// <summary>配置的目标进程 ID；为 null 表示不过滤。</summary>
        public int? TargetProcessId { get; init; }
    }
}
