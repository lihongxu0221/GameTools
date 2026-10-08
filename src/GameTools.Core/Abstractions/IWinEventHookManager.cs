using GameTools.Core.Events;

namespace GameTools.Core.Abstractions;

/// <summary>
/// 目标进程窗口状态事件监听接口
/// </summary>
public interface IWinEventHookManager : IDisposable
{
    /// <summary>
    /// 当捕获到目标进程的窗口生命周期或焦点事件时触发
    /// </summary>
    event EventHandler<WinEventMessageEventArgs>? WinEventReceived;

    /// <summary>
    /// 开始监听指定进程的窗口事件
    /// </summary>
    /// <param name="targetProcessId">目标进程 PID</param>
    void Start(int targetProcessId);

    /// <summary>
    /// 停止监听
    /// </summary>
    void Stop();
}
