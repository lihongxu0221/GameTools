namespace GameTools.Core.Abstractions;

/// <summary>
/// 后台 Win32 消息泵宿主接口
/// </summary>
public interface IBackgroundMessagePump : IDisposable
{
    /// <summary>
    /// 后台隐藏消息窗口的 HWND 句柄
    /// </summary>
    IntPtr MessageWindowHandle { get; }

    /// <summary>
    /// 消息循环是否正在运行
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 启动后台 STA 线程并开始消息循环
    /// </summary>
    void Start();

    /// <summary>
    /// 停止后台消息循环并释放资源
    /// </summary>
    void Stop();

    /// <summary>
    /// 将委托安全投递至后台消息线程中异步执行
    /// </summary>
    void PostAction(Action action);

    /// <summary>
    /// 将委托切换至后台消息线程中同步执行并返回结果
    /// </summary>
    T InvokeFunc<T>(Func<T> func);

    /// <summary>
    /// 将委托切换至后台消息线程中同步执行
    /// </summary>
    void InvokeAction(Action action);

    /// <summary>
    /// 注册特定 Windows 消息的回调处理
    /// </summary>
    void RegisterMessageFilter(uint messageId, Action<IntPtr, IntPtr> handler);

    /// <summary>
    /// 注销特定 Windows 消息的回调处理
    /// </summary>
    void UnregisterMessageFilter(uint messageId);
}
