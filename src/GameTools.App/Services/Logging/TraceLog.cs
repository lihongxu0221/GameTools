using System.Diagnostics;
using System.Globalization;
using NLog;

namespace GameTools.App.Services.Logging;

/// <summary>
/// 将库层的 <see cref="Trace"/> 输出桥接到 NLog 日志文件。
/// </summary>
/// <remarks>
/// <para>
/// <c>Infrastructure</c> 与 <c>Win32</c> 层不直接依赖 NLog（避免把日志框架下沉到库层），
/// 仍使用 <c>Trace.WriteLine</c> 输出诊断信息。该桥接器让这些既有输出在启用 NLog 后
/// 进入统一日志文件，使应用层获得完整诊断信息，而库层无需新增依赖。
/// </para>
/// <para>
/// <strong>实现约束</strong>：本类型已加入 <see cref="Trace.Listeners"/>，
/// 因此各重写方法内<strong>禁止</strong>再次调用 <c>Trace.WriteLine</c> / <c>Trace.Write</c>。
/// 那会重新进入监听器链并回调到自身，形成无限递归并导致栈溢出
/// （进程以 <c>0xC00000FD</c> 终止，无可用托管异常）。
/// 监听器的职责是消费 Trace 输出，转发工作交由 NLog 目标完成。
/// </para>
/// </remarks>
public sealed class TraceLog : TraceListener
{
    private static TraceLog? _instance;

    private TraceLog()
    {
    }

    /// <summary>
    /// 获取或创建桥接器单例。
    /// </summary>
    public static TraceLog Instance => _instance ??= new TraceLog();

    /// <inheritdoc />
    public override void Write(string? message) => Forward(LogLevel.Debug, message);

    /// <inheritdoc />
    public override void WriteLine(string? message) => Forward(LogLevel.Debug, message);

    /// <inheritdoc />
    public override void TraceEvent(
        TraceEventCache? eventCache,
        string source,
        TraceEventType eventType,
        int id,
        string? message) =>
        Forward(MapLevel(eventType), message);

    /// <inheritdoc />
    public override void TraceEvent(
        TraceEventCache? eventCache,
        string source,
        TraceEventType eventType,
        int id,
        string? format,
        params object?[]? args)
    {
        string text = format != null && args != null && args.Length > 0
            ? string.Format(CultureInfo.InvariantCulture, format, args)
            : (format ?? string.Empty);

        Forward(MapLevel(eventType), text);
    }

    /// <summary>
    /// 将一条 Trace 输出转发至 NLog。
    /// </summary>
    /// <remarks>
    /// 日志系统不可用时静默跳过：Trace 桥接属于诊断辅助手段，
    /// 不得因日志后端故障而抛异常影响调用方，更不得递归回 Trace。
    /// </remarks>
    /// <param name="level">目标日志级别。</param>
    /// <param name="message">输出内容。</param>
    private static void Forward(LogLevel level, string? message)
    {
        if (!AppLogFactory.Enabled)
        {
            return;
        }

        try
        {
            AppLogFactory.GetLogger().Log(level, "{0}", message ?? string.Empty);
        }
        catch (Exception)
        {
            // 桥接失败不得外溢：调用方通常处于错误处理路径中
        }
    }

    /// <summary>
    /// 将 Trace 事件类型映射为对应的 NLog 级别。
    /// </summary>
    /// <param name="eventType">Trace 事件类型。</param>
    /// <returns>NLog 日志级别。</returns>
    private static LogLevel MapLevel(TraceEventType eventType) => eventType switch
    {
        TraceEventType.Critical or TraceEventType.Error => LogLevel.Error,
        TraceEventType.Warning => LogLevel.Warn,
        TraceEventType.Information => LogLevel.Info,
        _ => LogLevel.Debug
    };
}
