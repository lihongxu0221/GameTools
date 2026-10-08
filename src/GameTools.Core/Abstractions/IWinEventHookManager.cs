using GameTools.Core.Events;

namespace GameTools.Core.Abstractions;

/// <summary>
/// 目标进程窗口状态事件监听接口。
/// </summary>
public interface IWinEventHookManager : IDisposable
{
    /// <summary>
    /// 目标进程发生窗口状态变化时引发。
    /// </summary>
    event EventHandler<WinEventMessageEventArgs>? WinEventReceived;

    /// <summary>
    /// 开始监听指定进程的窗口生命周期事件。
    /// </summary>
    /// <param name="targetProcessId">
    /// 目标进程 ID，必须为正整数。0 在原生语义中表示监听整个桌面，与本契约不符。
    /// </param>
    void Start(int targetProcessId);

    /// <summary>
    /// 停止监听。
    /// </summary>
    void Stop();

    /// <summary>
    /// 事件队列丢弃计数。持续增长说明订阅者处理速度不足，需评估队列容量或消费逻辑。
    /// </summary>
    long DroppedEventCount { get; }
}