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

    /// <summary>
    /// 是否已启用日志输出。为遵守仓库规则「正式构建默认不写日志文件」，
    /// 仅在 Debug 配置或显式设置环境变量 <c>GAMETOOLS_LOG</c> 时启用。
    /// </summary>
    public static bool Enabled { get; private set; }

    /// <summary>
    /// 初始化日志基础设施。
    /// </summary>
    /// <param name="logDirectory">日志目录；为空时使用可执行程序目录下的 <c>logs</c>。</param>
    /// <returns>初始化后的日志器，供启动阶段记录早期异常。</returns>
    public static ILogger Initialize(string? logDirectory = null)
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
