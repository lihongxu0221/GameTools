namespace GameTools.Core.Abstractions;

/// <summary>
/// 后台 Win32 消息泵宿主接口。
/// </summary>
/// <remarks>
/// 所有同步调度方法都必须具备超时与取消能力：消息泵线程崩溃、被长任务阻塞或已停止时，
/// 调用方必须能收到明确失败，而不能无限等待。
/// </remarks>
public interface IBackgroundMessagePump : IDisposable
{
    /// <summary>
    /// 后台隐藏消息窗口句柄；未就绪时为 <see cref="IntPtr.Zero"/>。
    /// </summary>
    IntPtr MessageWindowHandle { get; }

    /// <summary>
    /// 消息循环是否正在运行。
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 启动后台 STA 线程并开始消息循环。重复调用无效。
    /// </summary>
    void Start();

    /// <summary>
    /// 停止后台消息循环并释放资源。停止后不可重启。
    /// </summary>
    void Stop();

    /// <summary>
    /// 将委托投递至后台消息线程异步执行，不等待结果。
    /// </summary>
    /// <param name="action">待执行委托。</param>
    void PostAction(Action action);

    /// <summary>
    /// 将委托切换至后台消息线程同步执行并返回结果，使用默认超时。
    /// </summary>
    /// <typeparam name="T">返回值类型。</typeparam>
    /// <param name="func">待执行委托。</param>
    T InvokeFunc<T>(Func<T> func);

    /// <summary>
    /// 将委托切换至后台消息线程同步执行并返回结果。
    /// </summary>
    /// <typeparam name="T">返回值类型。</typeparam>
    /// <param name="func">待执行委托。</param>
    /// <param name="timeout">等待超时，超时抛出 <see cref="TimeoutException"/>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    T InvokeFunc<T>(Func<T> func, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    /// 将委托切换至后台消息线程同步执行。
    /// </summary>
    /// <param name="action">待执行委托。</param>
    void InvokeAction(Action action);

    /// <summary>
    /// 注册 Windows 消息回调。同一消息支持多个订阅者。
    /// </summary>
    /// <param name="messageId">消息标识。</param>
    /// <param name="handler">回调处理。</param>
    void RegisterMessageFilter(uint messageId, Action<IntPtr, IntPtr> handler);

    /// <summary>
    /// 取消注册 Windows 消息回调。
    /// </summary>
    /// <param name="messageId">消息标识。</param>
    void UnregisterMessageFilter(uint messageId);
}