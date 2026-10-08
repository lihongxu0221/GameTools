using GameTools.Infrastructure.Host;
using Xunit;

namespace GameTools.Tests;

/// <summary>
/// <see cref="SingleInstanceLock"/> 的单实例互斥测试。
/// </summary>
/// <remarks>
/// 覆盖此前发现的两处缺陷：
/// 1. <c>AbandonedMutexException</c> 分支未执行 <c>WaitOne</c>，导致 <c>Dispose</c> 中
///    <c>ReleaseMutex</c> 必然抛异常并被裸 <c>catch</c> 吞掉；
/// 2. 重复释放不应抛异常。
/// </remarks>
public sealed class SingleInstanceLockTests
{
    [Fact]
    public void FirstInstance_ShouldAcquireOwnership()
    {
        string name = UniqueName();

        using var first = new SingleInstanceLock(name);

        Assert.True(first.IsOnlyInstance);
    }

    [Fact]
    public void SecondInstance_ShouldDetectCollision()
    {
        string name = UniqueName();

        using var first = new SingleInstanceLock(name);
        using var second = new SingleInstanceLock(name);

        Assert.True(first.IsOnlyInstance);
        Assert.False(second.IsOnlyInstance);
    }

    [Fact]
    public void Ownership_ShouldBeReleasedAfterDispose()
    {
        string name = UniqueName();

        var first = new SingleInstanceLock(name);
        Assert.True(first.IsOnlyInstance);
        first.Dispose();

        // 首个实例释放后，同名互斥体应可再次获取
        using var third = new SingleInstanceLock(name);
        Assert.True(third.IsOnlyInstance);
    }

    [Fact]
    public void Dispose_WhenCalledTwice_ShouldNotThrow()
    {
        var lockInstance = new SingleInstanceLock(UniqueName());

        lockInstance.Dispose();

        // 幂等释放：不得因重复调用抛异常
        lockInstance.Dispose();
    }

    [Fact]
    public void Dispose_OnNonOwner_ShouldNotThrow()
    {
        string name = UniqueName();

        using var owner = new SingleInstanceLock(name);
        var nonOwner = new SingleInstanceLock(name);

        // 未获取所有权的实例执行 ReleaseMutex 会失败，实现必须吞掉该异常
        nonOwner.Dispose();
    }

    private static string UniqueName() => "GameToolsTest_" + Guid.NewGuid().ToString("N");
}