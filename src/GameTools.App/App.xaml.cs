using System.Diagnostics;
using System.Windows;
using GameTools.App.Services;
using GameTools.App.Services.Logging;
using GameTools.Core.Abstractions;
using GameTools.Core.Enums;
using GameTools.Infrastructure.Capture;
using GameTools.Infrastructure.Host;
using GameTools.Infrastructure.Hotkeys;
using GameTools.Infrastructure.Input;
using GameTools.Win32.Helpers;
using NLog;
using Prism.Ioc;
using Prism.Modularity;

namespace GameTools.App;

/// <summary>
/// WPF 应用入口，使用 Prism 负责容器注册与模块装配。
/// </summary>
/// <remarks>
/// 生命周期约定：
/// 1. <see cref="OnStartup"/> 中先初始化日志，再做单实例判定，保证异常路径也有日志可查；
/// 2. 单实例失败时以对话框提示后退出，不进入 Prism 容器初始化；
/// 3. 所有核心服务在容器中注册为单例，由 Prism 管理生命周期；
/// 4. <see cref="OnExit"/> 中按注册逆序释放，确保消息泵与托盘图标等资源被正确清理。
/// </remarks>
public partial class App : PrismApplication
{
    private ILogger _logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// 命令行参数解析结果。
    /// </summary>
    public static HostOptions Options { get; private set; } = HostOptions.Interactive;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        // 日志必须先于一切初始化，否则启动期异常无处可查
        _logger = AppLogFactory.Initialize();

        // 桥接库层 Trace 输出到统一日志文件
        Trace.Listeners.Add(TraceLog.Instance);

        Options = HostOptions.Parse(e.Args);

        _logger.Info("GameTools 启动，宿主模式: {Mode}", Options.Mode);
        _logger.Info("系统信息: {OsInfo}", OSVersionHelper.GetOsDescription());

        // 单实例判定在容器初始化之前：冲突时无需承担容器与钩子资源开销
        var singleInstance = new SingleInstanceLock("GameTools_Core_Host");
        if (!singleInstance.IsOnlyInstance)
        {
            _logger.Warn("检测到已有实例运行，本次启动终止");
            MessageBox.Show(
                "检测到已有 GameTools 进程在后台运行，请勿重复启动。",
                "GameTools",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown(1);
            return;
        }

        base.OnStartup(e);

        _logger.Info("Prism 容器与主窗口初始化完成");
    }

    /// <inheritdoc />
    protected override Window CreateShell() => Container.Resolve<Views.MainWindow>();

    /// <inheritdoc />
    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        // 宿主基础设施
        containerRegistry.RegisterSingleton<IBackgroundMessagePump, BackgroundMessagePump>();
        containerRegistry.RegisterInstance(Options);

        // 能力实现
        containerRegistry.RegisterSingleton<IScreenCapture, GdiScreenCapture>();
        containerRegistry.RegisterSingleton<IHotkeyManager, Win32HotkeyManager>();
        containerRegistry.RegisterSingleton<ITextInputSimulator, WindowsInputSimulator>();
        containerRegistry.RegisterSingleton<ILowLevelHookManager, Infrastructure.Hooks.LowLevelHookManager>();
        containerRegistry.RegisterSingleton<IWinEventHookManager, Infrastructure.Hooks.WinEventHookManager>();

        // 版本探测：注册为实例以便替换为注入固定版本的实现
        containerRegistry.RegisterInstance<IOsVersionProvider>(OSVersionHelper.Provider);
    }

    /// <inheritdoc />
    protected override void ConfigureModuleCatalog(IModuleCatalog moduleCatalog)
    {
        // 单模块应用，无需额外模块装配
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        _logger.Info("GameTools 退出中，开始释放资源");

        try
        {
            // Prism 已释放容器内单例；此处仅处理未纳入容器的宿主资源
            base.OnExit(e);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "退出阶段发生异常");
        }
        finally
        {
            Trace.Listeners.Remove(TraceLog.Instance);
            _logger.Info("GameTools 已退出");
            AppLogFactory.Shutdown();
        }
    }
}
