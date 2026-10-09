using System;
using NLog;
using NLog.Common;
using NLog.Layouts;
using NLog.Targets;

namespace GameTools.App.Services.Logging;

/// <summary>
/// 把日志事件转发给订阅者的内存目标。
/// </summary>
/// <remarks>
/// <para>
/// 界面「运行日志」面板需要实时展示日志，NLog 自带的 <see cref="MemoryTarget"/>
/// 在 5.4 中既不提供 <c>LogReceived</c> 事件，也不提供容量上限设置
/// （<c>Overflow</c> 与 <c>OptimizeBufferReuse</c> 均不可用），
/// 且仅能通过轮询 <c>Logs</c> 读取。因此本类改为直接继承
/// <see cref="Target"/>，在写入点即时转发，避免轮询延迟与无界内存增长。
/// </para>
/// <para>
/// 与文件目标位于同一日志管道，因此面板展示的内容与文件日志一致。
/// </para>
/// </remarks>
public sealed class UiLogTarget : Target
{
    /// <summary>
    /// 面板单行布局：级别前缀 + 消息正文。
    /// </summary>
    /// <remarks>
    /// 使用 <see cref="SimpleLayout"/> 而非 <see cref="Target.Layout"/>：
    /// 后者在 NLog 5 的子类构造上下文中不可直接赋值，且
    /// <c>RenderLogEvent</c> 的签名随版本变化，稳定性不如前者。
    /// </remarks>
    private static readonly SimpleLayout PanelLayout =
        new("[${level:uppercase=true}] ${message}");

    /// <summary>
    /// 创建面板日志目标。
    /// </summary>
    public UiLogTarget()
    {
        Name = "ui";
    }

    /// <summary>
    /// 日志事件。可能在任意线程触发，订阅方需自行切回界面线程。
    /// </summary>
    public event EventHandler<UiLogEventArgs>? LogReceived;

    /// <inheritdoc />
    protected override void Write(LogEventInfo logEvent)
    {
        EventHandler<UiLogEventArgs>? handler = LogReceived;
        if (handler == null)
        {
            return;
        }

        try
        {
            handler(this, new UiLogEventArgs(PanelLayout.Render(logEvent)));
        }
        catch (Exception)
        {
            // 面板订阅方异常不得中断日志管道
        }
    }

    /// <summary>
    /// 订阅日志。
    /// </summary>
    /// <param name="handler">日志处理器。</param>
    public void Subscribe(Action<string> handler)
    {
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        LogReceived += (_, args) => handler(args.Message);
    }
}

/// <summary>
/// 面板日志事件参数。
/// </summary>
public sealed class UiLogEventArgs : EventArgs
{
    /// <summary>
    /// 创建事件参数。
    /// </summary>
    /// <param name="message">已格式化的单行日志文本。</param>
    public UiLogEventArgs(string message) => Message = message;

    /// <summary>
    /// 已格式化的单行日志文本。
    /// </summary>
    public string Message { get; }
}
