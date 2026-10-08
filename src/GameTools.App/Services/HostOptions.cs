namespace GameTools.App.Services;

/// <summary>
/// 宿主运行模式。
/// </summary>
public enum HostMode
{
    /// <summary>显示主窗口的交互模式。</summary>
    Interactive,

    /// <summary>仅驻留托盘，不显示主窗口。</summary>
    Tray,

    /// <summary>无界面后台模式，仅注册热键与钩子。</summary>
    Headless
}

/// <summary>
/// 宿主启动选项，由命令行参数解析得到。
/// </summary>
/// <param name="Mode">运行模式。</param>
public sealed record HostOptions(HostMode Mode)
{
    /// <summary>
    /// 默认交互模式选项。
    /// </summary>
    public static HostOptions Interactive { get; } = new(HostMode.Interactive);

    /// <summary>
    /// 解析命令行参数。
    /// </summary>
    /// <remarks>
    /// 两条约定：
    /// 1. 采用「显式声明」策略：只识别已支持的开关，未知参数不改变行为，
    ///    避免因参数拼写错误导致意外进入无头模式而让用户以为程序未启动；
    /// 2. 多个模式同时出现时，<c>--headless</c> 优先级最高：
    ///    无界面运行是更强的意图声明，不应被参数顺序稀释。
    /// </remarks>
    /// <param name="args">命令行参数数组。</param>
    public static HostOptions Parse(string[] args)
    {
        if (args == null || args.Length == 0)
        {
            return Interactive;
        }

        bool trayRequested = false;
        bool headlessRequested = false;

        foreach (string raw in args)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string arg = raw.Trim();
            int separator = arg.IndexOf('=');
            string name = separator > 0 ? arg.Substring(0, separator) : arg;

            if (string.Equals(name, "--tray", StringComparison.OrdinalIgnoreCase))
            {
                trayRequested = true;
            }

            if (string.Equals(name, "--headless", StringComparison.OrdinalIgnoreCase))
            {
                headlessRequested = true;
            }
        }

        if (headlessRequested)
        {
            return new HostOptions(HostMode.Headless);
        }

        if (trayRequested)
        {
            return new HostOptions(HostMode.Tray);
        }

        return Interactive;
    }
}