using System.Diagnostics;
using System.Globalization;

namespace GameTools.App.Services.Logging;

/// <summary>
/// 将 NLog 日志事件桥接到 <see cref="Trace"/> 输出。
/// </summary>
/// <remarks>
/// <c>Infrastructure</c> 与 <c>Win32</c> 层不直接依赖 NLog（避免把日志框架下沉到库层），
/// 目前仍使用 <c>Trace.WriteLine</c> 输出诊断信息。该桥接器让这些既有输出
/// 在启用 NLog 后进入统一的日志文件，使应用层能够获得完整的诊断信息，
/// 而库层无需新增依赖。
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
    public override void Write(string? message)
    {
        // 保留原有 Trace 语义，便于调试器输出窗口查看
        System.Diagnostics.Trace.WriteLine(message);
    }

    /// <inheritdoc />
    public override void WriteLine(string? message)
    {
        string text = message ?? string.Empty;

        if (AppLogFactory.Enabled)
        {
            AppLogFactory.GetLogger().Debug(text);
        }

        System.Diagnostics.Trace.WriteLine(text);
    }

    /// <inheritdoc />
    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
    {
        if (AppLogFactory.Enabled)
        {
            NLog.LogLevel level = eventType switch
            {
                TraceEventType.Critical or TraceEventType.Error => NLog.LogLevel.Error,
                TraceEventType.Warning => NLog.LogLevel.Warn,
                TraceEventType.Information => NLog.LogLevel.Info,
                _ => NLog.LogLevel.Debug
            };

            AppLogFactory.GetLogger().Log(level, "{0}", message);
        }

        base.TraceEvent(eventCache, source, eventType, id, message);
    }

    /// <inheritdoc />
    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
    {
        string text = format != null && args != null && args.Length > 0
            ? string.Format(CultureInfo.InvariantCulture, format, args)
            : (format ?? string.Empty);

        TraceEvent(eventCache, source, eventType, id, text);
    }
}