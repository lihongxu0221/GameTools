using GameTools.Infrastructure.Host;
using Xunit;

namespace GameTools.Tests;

public class SingleInstanceLockTests
{
    [Fact]
    public void SingleInstanceLock_FirstInstance_ShouldAcquireSuccessfully()
    {
        string uniqueId = "TestApp_" + Guid.NewGuid().ToString("N");
        using var lock1 = new SingleInstanceLock(uniqueId);

        Assert.True(lock1.IsOnlyInstance);
    }

    [Fact]
    public void SingleInstanceLock_SecondInstance_ShouldDetectCollision()
    {
        string uniqueId = "TestApp_" + Guid.NewGuid().ToString("N");
        using var lock1 = new SingleInstanceLock(uniqueId);
        Assert.True(lock1.IsOnlyInstance);

        using var lock2 = new SingleInstanceLock(uniqueId);
        Assert.False(lock2.IsOnlyInstance);
    }
}
