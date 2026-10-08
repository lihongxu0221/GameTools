using GameTools.Core.Abstractions;
using GameTools.Infrastructure.Host;
using Xunit;

namespace GameTools.Tests;

/// <summary>
/// <see cref="BackgroundMessagePump"/> 的生命周期、超时与并发行为测试。
/// </summary>
/// <remarks>
/// 覆盖三类风险：启动失败可诊断、同步调度不永久阻塞、重复启停幂等。
/// 测试不依赖桌面交互，仅使用隐藏消息窗口。
/// </remarks>
[Collection("MessagePump")]
public sealed class BackgroundMessagePumpTests
{
    [Fact]
    public void Start_ShouldCreateValidMessageWindow()
    {
        using var pump = new BackgroundMessagePump();

        pump.Start();

        Assert.True(pump.IsRunning);
        Assert.NotEqual(IntPtr.Zero, pump.MessageWindowHandle);
        Assert.True(User32Present(pump.MessageWindowHandle));
    }

    [Fact]
    public void Start_WhenCalledTwice_ShouldBeIdempotent()
    {
        using var pump = new BackgroundMessagePump();

        pump.Start();
        IntPtr firstHandle = pump.MessageWindowHandle;
        pump.Start();

        // 重复启动不得创建第二个消息循环
        Assert.Equal(firstHandle, pump.MessageWindowHandle);
        Assert.True(pump.IsRunning);
    }

    [Fact]
    public void Stop_ShouldReleaseWindowAndClearRunningState()
    {
        var pump = new BackgroundMessagePump();
        pump.Start();

        pump.Stop();

        Assert.False(pump.IsRunning);
        Assert.Equal(IntPtr.Zero, pump.MessageWindowHandle);

        pump.Dispose();
    }

    [Fact]
    public void Stop_WithoutStart_ShouldNotThrow()
    {
        using var pump = new BackgroundMessagePump();

        // 未启动即停止属于幂等场景
        pump.Stop();
    }

    [Fact]
    public void InvokeFunc_ShouldExecuteOnMessageThreadAndReturnResult()
    {
        using var pump = new BackgroundMessagePump();
        pump.Start();

        int threadIdBefore = Environment.CurrentManagedThreadId;
        int result = pump.InvokeFunc(() => Environment.CurrentManagedThreadId);

        Assert.Equal(0, result - threadIdBefore == 0 ? result : 0);
        Assert.NotEqual(threadIdBefore, result);
    }

    [Fact]
    public void InvokeFunc_ShouldPropagateOriginalException()
    {
        using var pump = new BackgroundMessagePump();
        pump.Start();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => pump.InvokeFunc<int>(() => throw new InvalidOperationException("boom")));

        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public void InvokeFunc_WithShortTimeoutAndBlockingAction_ShouldThrowTimeout()
    {
        using var pump = new BackgroundMessagePump();
        pump.Start();

        using var blocker = new ManualResetEventSlim(false);

        // 先投递一个阻塞动作占住消息线程
        var queued = new ManualResetEventSlim(false);
        pump.PostAction(() =>
        {
            queued.Set();
            blocker.Wait(TimeSpan.FromSeconds(5));
        });

        Assert.True(queued.Wait(TimeSpan.FromSeconds(5)), "阻塞动作未被调度");

        TimeoutException ex = Assert.Throws<TimeoutException>(
            () => pump.InvokeFunc(() => 1, TimeSpan.FromMilliseconds(200), CancellationToken.None));

        Assert.Contains("消息泵", ex.Message);

        blocker.Set();
    }

    [Fact]
    public void InvokeFunc_WithCancelledToken_ShouldThrowOperationCanceled()
    {
        using var pump = new BackgroundMessagePump();
        pump.Start();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => pump.InvokeFunc(() => 1, TimeSpan.FromSeconds(5), cts.Token));
    }

    [Fact]
    public void InvokeFunc_AfterStop_ShouldThrowInsteadOfHanging()
    {
        var pump = new BackgroundMessagePump();
        pump.Start();
        pump.Stop();

        // 停机后调用必须立即失败，而不是永久等待队列中的委托被执行
        Assert.Throws<InvalidOperationException>(() => pump.InvokeFunc(() => 1));

        pump.Dispose();
    }

    [Fact]
    public void PostAction_WithoutStart_ShouldThrow()
    {
        using var pump = new BackgroundMessagePump();

        Assert.Throws<InvalidOperationException>(() => pump.PostAction(() => { }));
    }

    [Fact]
    public void Start_AfterDispose_ShouldThrow()
    {
        var pump = new BackgroundMessagePump();
        pump.Dispose();

        Assert.Throws<ObjectDisposedException>(() => pump.Start());
    }

    [Fact]
    public void PostAction_WithNullAction_ShouldThrow()
    {
        using var pump = new BackgroundMessagePump();
        pump.Start();

        Assert.Throws<ArgumentNullException>(() => pump.PostAction(null!));
    }

    [Fact]
    public void MessageFilter_ShouldReceivePostedMessages()
    {
        using var pump = new BackgroundMessagePump();
        pump.Start();

        // 避开 WM_APP + 1（消息泵自身的 WM_EXECUTE_ACTION）
        const uint TestMessage = 0x8101;
        using var received = new ManualResetEventSlim(false);
        IntPtr capturedWParam = IntPtr.Zero;

        pump.RegisterMessageFilter(TestMessage, (wParam, _) =>
        {
            capturedWParam = wParam;
            received.Set();
        });

        pump.PostAction(() => User32Bridge.PostMessage(pump.MessageWindowHandle, TestMessage, new IntPtr(42), IntPtr.Zero));

        Assert.True(received.Wait(TimeSpan.FromSeconds(5)), "未收到测试消息");
        Assert.Equal(42, capturedWParam.ToInt32());

        pump.UnregisterMessageFilter(TestMessage);
    }

    [Fact]
    public void MessageFilter_ExceptionShouldNotBreakMessageLoop()
    {
        using var pump = new BackgroundMessagePump();
        pump.Start();

        // 避开 WM_APP + 1
        const uint BadMessage = 0x8102;
        const uint GoodMessage = 0x8103;

        using var recovered = new ManualResetEventSlim(false);

        pump.RegisterMessageFilter(BadMessage, (_, _) => throw new InvalidOperationException("handler failed"));
        pump.RegisterMessageFilter(GoodMessage, (_, _) => recovered.Set());

        pump.PostAction(() =>
        {
            User32Bridge.PostMessage(pump.MessageWindowHandle, BadMessage, IntPtr.Zero, IntPtr.Zero);
            User32Bridge.PostMessage(pump.MessageWindowHandle, GoodMessage, IntPtr.Zero, IntPtr.Zero);
        });

        // 处理器抛异常不得导致消息循环退出
        Assert.True(recovered.Wait(TimeSpan.FromSeconds(5)), "消息循环因处理器异常而中断");
    }

    private static bool User32Present(IntPtr hWnd) =>
        hWnd != IntPtr.Zero && GameTools.Win32.Native.User32.IsWindow(hWnd);
}