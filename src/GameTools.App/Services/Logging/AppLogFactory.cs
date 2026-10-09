using System.IO;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace GameTools.App.Services.Logging;

/// <summary>
/// NLog 日志工厂。
/// </summary>
/// <remarks>
/// 集中负责日志器初始化与全局开关，避免在各模块散落 <c>LogManager.GetCurrentClassLogger</c> 调用，
/// 同时保证日志配置在应用启动早期即生效，异常路径也有日志可查。
/// </remarks>
public static class AppLogFactory
{
    private static readonly object _syncRoot = new();
    private static bool _configured;

    /// <summary>供界面面板订阅的内存目标；未启用时为 null。</summary>
    private static UiLogTarget? _memoryTarget;

    /// <summary>
    /// 是否已启用日志输出。为遵守仓库规则「正式构建默认不写日志文件」，
    /// 仅在 Debug 配置或显式设置环境变量 <c>GAMETOOLS_LOG</c> 时启用。
    /// </summary>
    public static bool Enabled { get; private set; }

    /// <summary>
    /// 初始化日志基础设施。
    /// </summary>
    /// <param name="logDirectory">日志目录；为空时使用可执行程序目录下的 <c>logs</c>。</param>
    /// <param name="attachMemoryTarget">
    /// 是否挂载内存目标以供界面「运行日志」面板订阅。交互式宿主应传 <c>true</c>；
    /// tray 与 headless 模式无人查看面板，传 <c>false</c> 以免无谓占用内存。
    /// </param>
    /// <returns>初始化后的日志器，供启动阶段记录早期异常。</returns>
    public static ILogger Initialize(string? logDirectory = null, bool attachMemoryTarget = false)
    {
        lock (_syncRoot)
        {
            if (_configured)
            {
                return LogManager.GetCurrentClassLogger();
            }

            Enabled = ShouldEnableLogging();

            if (!Enabled)
            {
                LogManager.Configuration = null;
                _configured = true;
                return LogManager.GetCurrentClassLogger();
            }

            string? configuredDirectory = logDirectory;
            string directory = !string.IsNullOrWhiteSpace(configuredDirectory)
                ? configuredDirectory!
                : Path.Combine(AppContext.BaseDirectory, "logs");

            Directory.CreateDirectory(directory);

            LoggingConfiguration config = new();

            // 滚动文件目标：单文件 5MB，总量上限 20MB，保留 10 个历史文件
            FileTarget fileTarget = new("logfile")
            {
                FileName = Path.Combine(directory, "gametools-${short-date}.log"),
                Layout = "${longdate}|${level:uppercase=true}|${logger}|${message} ${exception:format=ToString}",
                ArchiveAboveSize = 5 * 1024 * 1024,
                ArchiveNumbering = ArchiveNumberingMode.Rolling,
                MaxArchiveFiles = 10,
                KeepFileOpen = false,
                ConcurrentWrites = false
            };

            // 内存目标：供界面「运行日志」面板订阅，使面板展示的内容与文件日志一致。
            // 仅在交互式宿主启用；tray/headless 模式无人查看，启用只会无谓占用内存。
            if (attachMemoryTarget)
            {
                var memoryTarget = new UiLogTarget();
                config.AddRuleForAllLevels(memoryTarget);
                _memoryTarget = memoryTarget;
            }

            // 控制台目标：仅在交互式宿主下启用，tray/headless 模式避免污染输出
            if (Environment.UserInteractive && !Console.IsOutputRedirected)
            {
                ConsoleTarget consoleTarget = new("console")
                {
                    Layout = "${time}|${level:uppercase=true:padding=-5}|${message}",
                    DetectConsoleAvailable = true
                };
                config.AddRuleForAllLevels(consoleTarget);
            }

            config.AddRuleForAllLevels(fileTarget);
            LogManager.Configuration = config;

            _configured = true;
            return LogManager.GetCurrentClassLogger();
        }
    }

    /// <summary>
    /// 获取当前类的日志器。
    /// </summary>
    public static ILogger GetLogger() => LogManager.GetCurrentClassLogger();

    /// <summary>
    /// 订阅日志事件，用于界面「运行日志」面板实时展示。
    /// </summary>
    /// <remarks>
    /// 面板内容与文件日志来自同一份日志管道，因此级别前缀与异常信息保持一致。
    /// 未启用内存目标时返回 <c>false</c>，调用方应退化为仅显示自身操作结果，
    /// 而不是让面板空白却仍标注为「NLog」。
    /// </remarks>
    /// <param name="handler">日志处理器，接收单条格式化后的文本。</param>
    /// <returns>是否成功订阅。</returns>
    public static bool TrySubscribe(Action<string> handler)
    {
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        UiLogTarget? target = _memoryTarget;
        if (!Enabled || target == null)
        {
            return false;
        }

        target.Subscribe(handler);
        return true;
    }

    /// <summary>
    /// 关闭并释放日志资源。应用退出前必须调用，确保文件句柄被释放。
    /// </summary>
    public static void Shutdown()
    {
        lock (_syncRoot)
        {
            if (!_configured)
            {
                return;
            }

            LogManager.Shutdown();
            _memoryTarget = null;
            _configured = false;
            Enabled = false;
        }
    }

    private static bool ShouldEnableLogging()
    {
        string? explicitSetting = Environment.GetEnvironmentVariable("GAMETOOLS_LOG");
        if (!string.IsNullOrWhiteSpace(explicitSetting))
        {
            return !string.Equals(explicitSetting, "off", StringComparison.OrdinalIgnoreCase);
        }

#if DEBUG
        return true;
#else
        return false;
#endif
    }
}
