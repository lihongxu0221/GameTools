using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using GameTools.Core.Abstractions;
using GameTools.Win32.Native;

namespace GameTools.Infrastructure.Host;

/// <summary>
/// 专用于运行 Win32 消息泵的独立 STA 后台宿主。
/// 为全局快捷键 RegisterHotKey、WinEventHook 提供稳定的消息分发线程。
/// </summary>
/// <remarks>
/// 生命周期与并发约定：
/// 1. 线程在构造时创建，<see cref="Start"/> 至多成功一次，重复调用直接返回；
/// 2. <see cref="Stop"/> 后不可重新启动（Windows 线程不可复用），再次调用直接返回；
/// 3. 所有同步调度（<see cref="InvokeFunc{T}"/>）带超时与取消，消息泵崩溃或已停止时
///    立即抛出 <see cref="TimeoutException"/> / <see cref="ObjectDisposedException"/>，
///    不允许调用方无限等待；
/// 4. 启动线程的异常通过 <see cref="TaskCompletionSource{T}"/> 回传给调用方，
///    而不是仅写入 Trace 导致启动失败被误判为超时；
/// 5. 动作在泵线程内串行执行，因此队列中的委托可能长时间占用消息线程，
///    耗时操作（截图、输入模拟）不应通过同步调度提交。
/// </remarks>
public sealed class BackgroundMessagePump : IBackgroundMessagePump
{
    private const int StartTimeoutMs = 5000;
    private const int StopJoinTimeoutMs = 2000;
    private const int DefaultInvokeTimeoutMs = 5000;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _startedEvent = new(false);
    private readonly ConcurrentDictionary<uint, Action<IntPtr, IntPtr>> _messageFilters = new();
    private readonly ConcurrentQueue<Action> _actionQueue = new();
    private readonly TaskCompletionSource<bool> _startupSignal =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private MessageWindow? _window;
    private ApplicationContext? _appContext;
    private volatile bool _isRunning;
    private int _startRequested;
    private bool _disposed;

    /// <inheritdoc />
    public IntPtr MessageWindowHandle => Volatile.Read(ref _window)?.Handle ?? IntPtr.Zero;

    /// <inheritdoc />
    public bool IsRunning => _isRunning;

    /// <summary>
    /// 创建后台消息泵宿主。
    /// </summary>
    public BackgroundMessagePump()
    {
        _thread = new Thread(ThreadProc)
        {
            IsBackground = true,
            Name = "GameTools-MessagePump-Thread"
        };

        _thread.SetApartmentState(ApartmentState.STA);
    }

    /// <inheritdoc />
    public void Start()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(BackgroundMessagePump));
        }

        // 原子化保证并发 Start 只创建一个消息循环
        if (Interlocked.CompareExchange(ref _startRequested, 1, 0) != 0)
        {
            return;
        }

        _thread.Start();

        if (!_startupSignal.Task.Wait(TimeSpan.FromMilliseconds(StartTimeoutMs)))
        {
            throw new TimeoutException("等待后台 Win32 消息泵窗口启动超时。");
        }

        // 启动线程失败时 Task 结果为失败，此处将原始异常抛给调用方
        _startupSignal.Task.GetAwaiter().GetResult();
        _isRunning = true;
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;

        IntPtr handle = MessageWindowHandle;

        // 先退出 ApplicationContext，使消息循环返回；随后兜底投递退出消息
        try
        {
            _appContext?.ExitThread();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"退出消息循环失败: {ex}");
        }

        if (handle != IntPtr.Zero)
        {
            User32.PostMessage(handle, NativeConstants.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        // 停机后清空待执行动作，避免调用方在 InvokeFunc 中无限等待
        while (_actionQueue.TryDequeue(out _))
        {
        }

        // 禁止在消息泵线程自身调用 Stop：Join 会等待自己直到超时
        if (_thread.IsAlive && Thread.CurrentThread != _thread)
        {
            _thread.Join(TimeSpan.FromMilliseconds(StopJoinTimeoutMs));
        }
    }

    /// <inheritdoc />
    public void PostAction(Action action)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        IntPtr handle = RequireWindowHandle();

        _actionQueue.Enqueue(action);
        User32.PostMessage(handle, NativeConstants.WM_EXECUTE_ACTION, IntPtr.Zero, IntPtr.Zero);
    }

    /// <inheritdoc />
    public T InvokeFunc<T>(Func<T> func)
    {
        if (func is null)
        {
            throw new ArgumentNullException(nameof(func));
        }

        return InvokeFunc(func, TimeSpan.FromMilliseconds(DefaultInvokeTimeoutMs), CancellationToken.None);
    }

    /// <summary>
    /// 在消息泵线程同步执行委托并返回结果。
    /// </summary>
    /// <typeparam name="T">返回值类型。</typeparam>
    /// <param name="func">待执行委托。</param>
    /// <param name="timeout">等待超时，超时抛出 <see cref="TimeoutException"/>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public T InvokeFunc<T>(Func<T> func, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (func is null)
        {
            throw new ArgumentNullException(nameof(func));
        }

        // 取消检查前置到入队之前，避免已取消的调用仍占用消息线程
        cancellationToken.ThrowIfCancellationRequested();

        IntPtr handle = RequireWindowHandle();

        // 已在消息线程上则直接执行，避免自等待
        if (Thread.CurrentThread == _thread)
        {
            return func();
        }

        T result = default!;
        Exception? error = null;

        using var completed = new ManualResetEventSlim(false);

        _actionQueue.Enqueue(() =>
        {
            try
            {
                result = func();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                completed.Set();
            }
        });

        User32.PostMessage(handle, NativeConstants.WM_EXECUTE_ACTION, IntPtr.Zero, IntPtr.Zero);

        bool finished;

        if (cancellationToken.CanBeCanceled)
        {
            finished = WaitWithCancellation(completed, timeout, cancellationToken);
        }
        else
        {
            finished = completed.Wait(timeout);
        }

        if (!finished)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"在 {timeout.TotalMilliseconds:F0}ms 内未获得消息泵线程响应；消息泵可能已停止或正被长任务阻塞。");
        }

        if (error != null)
        {
            // 保留原始异常栈
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        return result;
    }

    /// <inheritdoc />
    public void InvokeAction(Action action)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        InvokeFunc<object?>(() =>
        {
            action();
            return null;
        });
    }

    /// <inheritdoc />
    public void RegisterMessageFilter(uint messageId, Action<IntPtr, IntPtr> handler)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }
        _messageFilters[messageId] = handler;
    }

    /// <inheritdoc />
    public void UnregisterMessageFilter(uint messageId)
    {
        _messageFilters.TryRemove(messageId, out _);
    }

    private static bool WaitWithCancellation(ManualResetEventSlim completed, TimeSpan timeout, CancellationToken cancellationToken)
    {
        int remainingMs = (int)Math.Max(1, timeout.TotalMilliseconds);

        while (true)
        {
            // 取消优先于等待结果：调用方取消后必须尽快返回
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            int slice = Math.Min(50, remainingMs);

            if (completed.Wait(slice))
            {
                return true;
            }

            remainingMs -= slice;

            if (remainingMs <= 0)
            {
                return completed.IsSet;
            }
        }
    }

    private IntPtr RequireWindowHandle()
    {
        IntPtr handle = MessageWindowHandle;

        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("消息泵尚未启动或窗口句柄无效。");
        }

        return handle;
    }

    private void ThreadProc()
    {
        try
        {
            MessageWindow window = new(OnWndProc);
            Volatile.Write(ref _window, window);

            ApplicationContext appContext = new();
            Volatile.Write(ref _appContext, appContext);

            _startedEvent.Set();
            _startupSignal.TrySetResult(true);

            Application.Run(appContext);
        }
        catch (Exception ex)
        {
            // 启动或运行期失败必须回传调用方，不能只写 Trace
            _startupSignal.TrySetException(ex);
            System.Diagnostics.Trace.WriteLine($"消息泵线程异常: {ex}");
        }
        finally
        {
            _isRunning = false;

            MessageWindow? window = Interlocked.Exchange(ref _window, null);
            if (window != null)
            {
                try
                {
                    window.DestroyHandle();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"销毁消息窗口失败: {ex}");
                }
            }

            Volatile.Write(ref _appContext, null);
        }
    }

    private void OnWndProc(ref Message m)
    {
        uint msg = (uint)m.Msg;

        if (msg == NativeConstants.WM_EXECUTE_ACTION)
        {
            while (_actionQueue.TryDequeue(out Action? action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    // 消息线程必须吞掉动作异常，否则整个消息循环退出
                    System.Diagnostics.Trace.WriteLine($"执行消息泵动作异常: {ex}");
                }
            }

            return;
        }

        if (_messageFilters.TryGetValue(msg, out Action<IntPtr, IntPtr>? handler))
        {
            try
            {
                handler(m.WParam, m.LParam);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"处理消息 {msg} 发生异常: {ex}");
            }
        }

    }


    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Stop();
        _startedEvent.Dispose();
    }

    private delegate void MessageHandler(ref Message m);

    /// <summary>
    /// 隐藏消息窗口实现，仅承载消息分发，不显示任何界面。
    /// </summary>
    private sealed class MessageWindow : NativeWindow
    {
        private readonly MessageHandler _wndProcCallback;

        /// <summary>
        /// 创建消息窗口。
        /// </summary>
        /// <param name="wndProcCallback">窗口过程回调。</param>
        public MessageWindow(MessageHandler wndProcCallback)
        {
            _wndProcCallback = wndProcCallback;

            var cp = new CreateParams
            {
                Caption = "GameTools_HiddenMessageHost",
                Parent = NativeConstants.HWND_MESSAGE,
                Style = 0
            };

            CreateHandle(cp);
        }

        /// <inheritdoc />
        protected override void WndProc(ref Message m)
        {
            _wndProcCallback(ref m);
            base.WndProc(ref m);
        }
    }
}
