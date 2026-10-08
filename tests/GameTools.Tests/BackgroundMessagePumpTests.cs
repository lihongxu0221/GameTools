using GameTools.Infrastructure.Host;
using Xunit;

namespace GameTools.Tests;

public class BackgroundMessagePumpTests
{
    [Fact]
    public void BackgroundMessagePump_ShouldStartAndStopGracefully()
    {
        using var pump = new BackgroundMessagePump();
        Assert.False(pump.IsRunning);
        Assert.Equal(IntPtr.Zero, pump.MessageWindowHandle);

        pump.Start();
        Assert.True(pump.IsRunning);
        Assert.NotEqual(IntPtr.Zero, pump.MessageWindowHandle);

        // 测试跨线程 PostAction
        var executedEvent = new ManualResetEventSlim(false);
        pump.PostAction(() =>
        {
            executedEvent.Set();
        });

        bool executed = executedEvent.Wait(TimeSpan.FromSeconds(2));
        Assert.True(executed);

        pump.Stop();
        Assert.False(pump.IsRunning);
    }
}
