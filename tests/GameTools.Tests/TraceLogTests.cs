using System;
using System.Diagnostics;
using GameTools.App.Services.Logging;
using Xunit;

namespace GameTools.Tests;

/// <summary>
/// 使本集合内的测试串行执行。
/// </summary>
/// <remarks>
/// <see cref="Trace.Listeners"/> 是进程级全局状态，测试期间修改它会与其他测试相互干扰。
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TraceLogCollection
{
    /// <summary>
    /// 集合名称。
    /// </summary>
    public const string Name = "TraceLog";
}

/// <summary>
/// <see cref="TraceLog"/> 的回归测试。
/// </summary>
/// <remarks>
/// 回归背景：<see cref="TraceLog"/> 本身已加入 <see cref="Trace.Listeners"/>，
/// 却在 <c>Write</c> / <c>WriteLine</c> 内部再次调用 <c>Trace.WriteLine</c>，
/// 重新进入监听器链并回调到自身，形成无限递归。进程以 <c>0xC00000FD</c>（栈溢出）终止，
/// 没有可捕获的托管异常。库层任何一处 <c>Trace.WriteLine</c> 都会触发，
/// 例如 <c>WinEventHookManager.StopCore</c>。
/// </remarks>
[Collection(TraceLogCollection.Name)]
public class TraceLogTests
{
    /// <summary>
    /// 单次 <c>Trace</c> 写入允许的最大监听器调用次数。
    /// </summary>
    /// <remarks>
    /// 正确实现下每个监听器只被调用一次；递归回环会使该值迅速增长直至耗尽栈，
    /// 因此阈值取 20：既能容忍少量额外监听器，又能在崩溃前抛出可诊断异常。
    /// </remarks>
    private const int MaxInvocationsPerWrite = 20;

    /// <summary>
    /// 通过 <see cref="Trace"/> 写入时不得触发监听器自递归。
    /// </summary>
    [Fact]
    public void WriteLine_ViaTrace_ShouldNotReenterListenerChain()
    {
        var counter = new CountingListener(MaxInvocationsPerWrite);

        Trace.Listeners.Remove(TraceLog.Instance);
        Trace.Listeners.Add(TraceLog.Instance);
        Trace.Listeners.Add(counter);

        try
        {
            Trace.WriteLine("GameTools 回归探针消息");

            Assert.Equal(1, counter.Count);
        }
        finally
        {
            Trace.Listeners.Remove(counter);
            Trace.Listeners.Remove(TraceLog.Instance);
        }
    }

    /// <summary>
    /// 多次写入时每次都应只触发一次监听器调用，不得累积。
    /// </summary>
    [Fact]
    public void RepeatedWriteLine_ShouldNotAccumulateInvocations()
    {
        var counter = new CountingListener(MaxInvocationsPerWrite);

        Trace.Listeners.Remove(TraceLog.Instance);
        Trace.Listeners.Add(TraceLog.Instance);
        Trace.Listeners.Add(counter);

        try
        {
            for (int i = 0; i < 20; i++)
            {
                counter.Reset();
                Trace.WriteLine($"GameTools 批量探针 {i}");
                Assert.Equal(1, counter.Count);
            }

            Assert.Equal(20, counter.TotalCount);
        }
        finally
        {
            Trace.Listeners.Remove(counter);
            Trace.Listeners.Remove(TraceLog.Instance);
        }
    }

    /// <summary>
    /// <c>TraceEvent</c> 路径同样不得递归。
    /// </summary>
    [Fact]
    public void TraceEvent_ShouldNotReenterListenerChain()
    {
        var counter = new CountingListener(MaxInvocationsPerWrite);
        var source = new TraceSource("GameTools.Tests");
        source.Switch.Level = SourceLevels.All;
        source.Listeners.Add(TraceLog.Instance);
        source.Listeners.Add(counter);

        try
        {
            // TraceSource 会先派发 TraceEvent 再落到 Write/WriteLine，
            // 因此单次事件的调用次数为固定的小常数，此处关注的是它是否随调用次数增长。
            counter.Reset();
            source.TraceEvent(TraceEventType.Warning, 0, "GameTools 事件探针 {0}", 1);
            int first = counter.Count;
            Assert.InRange(first, 1, 4);

            counter.Reset();
            source.TraceEvent(TraceEventType.Error, 1, "GameTools 事件探针");
            int second = counter.Count;

            // 递归回环会让第二次调用成倍放大
            Assert.Equal(first, second);
        }
        finally
        {
            source.Listeners.Clear();
        }
    }

    /// <summary>
    /// 直接调用各重写方法不得抛出异常。
    /// </summary>
    /// <remarks>
    /// 日志后端未启用时 <c>TraceLog</c> 应静默跳过，这一条锁定该降级语义。
    /// </remarks>
    [Fact]
    public void DirectInvocation_ShouldNotThrow()
    {
        TraceLog listener = TraceLog.Instance;

        listener.Write("直接写入");
        listener.WriteLine("直接写行");
        listener.TraceEvent(null, "GameTools.Tests", TraceEventType.Error, 1, "直接事件");
        listener.TraceEvent(null, "GameTools.Tests", TraceEventType.Information, 2, "带参数 {0}", 7);
        listener.Flush();
    }

    /// <summary>
    /// 统计监听器调用次数的哨兵。
    /// </summary>
    /// <remarks>
    /// 递归回环会让调用次数无界增长并耗尽栈，因此在超过阈值时主动抛出可诊断异常，
    /// 使测试以明确原因失败，而不是让整个测试宿主以 <c>0xC00000FD</c> 崩溃。
    /// </remarks>
    private sealed class CountingListener : TraceListener
    {
        private readonly int _maxPerWrite;
        private int _count;
        private int _total;

        internal CountingListener(int maxPerWrite) => _maxPerWrite = maxPerWrite;

        /// <summary>
        /// 自上次 <see cref="Reset"/> 以来的调用次数。
        /// </summary>
        internal int Count => _count;

        /// <summary>
        /// 累计调用次数。
        /// </summary>
        internal int TotalCount => _total;

        /// <summary>
        /// 清零当前计数。
        /// </summary>
        internal void Reset() => _count = 0;

        /// <inheritdoc />
        public override void Write(string? message) => Enter();

        /// <inheritdoc />
        public override void WriteLine(string? message) => Enter();

        private void Enter()
        {
            _count++;
            _total++;

            if (_count > _maxPerWrite)
            {
                throw new InvalidOperationException(
                    $"单次 Trace 写入触发了 {_count} 次监听器调用（上限 {_maxPerWrite}），" +
                    "监听器链存在递归回环。");
            }
        }
    }
}
