using System.Collections.Concurrent;
using System.Windows.Forms;
using GameTools.Core.Abstractions;
using GameTools.Win32.Native;

namespace GameTools.Infrastructure.Host;

/// <summary>
/// 专用于运行 Win32 消息泵的独立 STA 后台宿主
/// 为全局快捷键 RegisterHotKey 与消息钩子提供稳定的消息分发驱动
/// </summary>
public sealed class BackgroundMessagePump : IBackgroundMessagePump
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _startedEvent = new(false);
    private readonly ConcurrentDictionary<uint, Action<IntPtr, IntPtr>> _messageFilters = new();
    private readonly ConcurrentQueue<Action> _actionQueue = new();
    private MessageWindow? _window;
    private ApplicationContext? _appContext;
    private volatile bool _isRunning;
    private bool _disposed;

    public IntPtr MessageWindowHandle => _window?.Handle ?? IntPtr.Zero;
    public bool IsRunning => _isRunning;

    public BackgroundMessagePump()
    {
        _thread = new Thread(ThreadProc)
        {
            IsBackground = true,
            Name = "GameTools-MessagePump-Thread"
        };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    public void Start()
    {
        if (_isRunning) return;

        _thread.Start();
        if (!_startedEvent.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("等待后台 Win32 消息泵窗口启动超时。");
        }
        _isRunning = true;
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;

        if (_window != null && _window.Handle != IntPtr.Zero)
        {
            User32.PostMessage(_window.Handle, NativeConstants.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        if (_appContext != null)
        {
            _appContext.ExitThread();
        }

        if (_thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    public void PostAction(Action action)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }
        if (_window == null || _window.Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("消息泵尚未启动或窗口句柄无效。");
        }

        _actionQueue.Enqueue(action);
        User32.PostMessage(_window.Handle, NativeConstants.WM_EXECUTE_ACTION, IntPtr.Zero, IntPtr.Zero);
    }

    public T InvokeFunc<T>(Func<T> func)
    {
        if (func is null)
        {
            throw new ArgumentNullException(nameof(func));
        }
        if (_window == null || _window.Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("消息泵尚未启动或窗口句柄无效。");
        }

        if (Thread.CurrentThread == _thread)
        {
            return func();
        }

        T result = default!;
        Exception? error = null;
        using var ev = new ManualResetEventSlim(false);

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
                ev.Set();
            }
        });

        User32.PostMessage(_window.Handle, NativeConstants.WM_EXECUTE_ACTION, IntPtr.Zero, IntPtr.Zero);
        ev.Wait();

        if (error != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }

        return result;
    }

    public void InvokeAction(Action action)
    {
        InvokeFunc<object?>(() =>
        {
            action();
            return null;
        });
    }

    public void RegisterMessageFilter(uint messageId, Action<IntPtr, IntPtr> handler)
    {
        _messageFilters[messageId] = handler;
    }

    public void UnregisterMessageFilter(uint messageId)
    {
        _messageFilters.TryRemove(messageId, out _);
    }

    private void ThreadProc()
    {
        try
        {
            _window = new MessageWindow(OnWndProc);
            _appContext = new ApplicationContext();

            _startedEvent.Set();
            Application.Run(_appContext);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"消息泵线程异常: {ex}");
        }
        finally
        {
            _window?.DestroyHandle();
            _window = null;
            _isRunning = false;
        }
    }

    private void OnWndProc(ref Message m)
    {
        uint msg = (uint)m.Msg;

        if (msg == NativeConstants.WM_EXECUTE_ACTION)
        {
            while (_actionQueue.TryDequeue(out var act))
            {
                try
                {
                    act();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"执行消息泵动作异常: {ex}");
                }
            }
            return;
        }

        if (_messageFilters.TryGetValue(msg, out var handler))
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _startedEvent.Dispose();
    }

    private delegate void MessageHandler(ref Message m);

    /// <summary>
    /// 隐藏消息窗口实现
    /// </summary>
    private sealed class MessageWindow : NativeWindow
    {
        private readonly MessageHandler _wndProcCallback;

        public MessageWindow(MessageHandler wndProcCallback)
        {
            _wndProcCallback = wndProcCallback;

            var cp = new CreateParams
            {
                Caption = "GameTools_HiddenMessageHost",
                Parent = IntPtr.Zero,
                Style = 0
            };
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            _wndProcCallback(ref m);
            base.WndProc(ref m);
        }
    }
}
