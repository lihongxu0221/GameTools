using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
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
    /// 单实例互斥句柄。必须持有为字段：若仅存在于 <see cref="OnStartup"/> 的局部变量中，
    /// 方法返回后即具备被回收的条件，会提前释放互斥量而失去单实例保护。
    /// </summary>
    private SingleInstanceLock? _singleInstance;

    /// <summary>
    /// 后台 Win32 消息泵。热键、钩子与窗口事件均依赖其消息线程，退出时须显式停止。
    /// </summary>
    private IBackgroundMessagePump? _messagePump;

    /// <summary>
    /// 命令行参数解析结果。
    /// </summary>
    public static HostOptions Options { get; private set; } = HostOptions.Interactive;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        // 参数解析必须先于日志初始化：是否挂载内存目标取决于宿主模式
        Options = HostOptions.Parse(e.Args);

        // 日志必须先于一切初始化，否则启动期异常无处可查。
        // 交互式宿主挂载内存目标，使「运行日志」面板与文件日志来自同一管道。
        _logger = AppLogFactory.Initialize(
            attachMemoryTarget: Options.Mode != HostMode.Headless);

        // 桥接库层 Trace 输出到统一日志文件
        Trace.Listeners.Add(TraceLog.Instance);

        _logger.Info("GameTools 启动，宿主模式: {Mode}", Options.Mode);
        _logger.Info("系统信息: {OsInfo}", OSVersionHelper.GetOsDescription());

        // 单实例判定在容器初始化之前：冲突时无需承担容器与钩子资源开销
        _singleInstance = new SingleInstanceLock("GameTools_Core_Host");
        if (!_singleInstance.IsOnlyInstance)
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
    protected override Window CreateShell()
    {
        // 后台 Win32 消息泵是热键、低级钩子与窗口事件的前置条件：
        // RegisterHotKey 要求在创建 HWND 的同一线程执行，SetWinEventHook / UnhookWinEvent
        // 同样要求安装与卸载在同一线程，二者都经由本消息泵调度。
        // 因此必须在解析任何依赖服务之前启动，否则上述功能会报「消息泵尚未启动」。
        _messagePump = Container.Resolve<IBackgroundMessagePump>();
        StartMessagePump(_messagePump);

        var mainWindow = Container.Resolve<Views.MainWindow>();
        var viewModel = Container.Resolve<ViewModels.MainViewModel>();

        // DataContext 必须显式设置：MainWindow.xaml 中的全部绑定均依赖它
        mainWindow.DataContext = viewModel;

        // 把钩子与窗口事件桥接到界面事件流
        if (Container.Resolve<IHookService>() is HookService hookService)
        {
            hookService.SetHookEventSink(viewModel.AppendHookEvent);
            hookService.SetWinEventSink(viewModel.AppendWinEvent);
        }

        viewModel.ShutdownRequested += (_, _) => Shutdown();

        // 把统一日志管道接入界面「运行日志」面板。
        // 订阅失败（例如未挂载内存目标）时不阻断启动，面板将仅显示操作结果。
        AppLogFactory.TrySubscribe(viewModel.AppendLog);

        if (Options.Mode == HostMode.Tray)
        {
            mainWindow.WindowState = WindowState.Minimized;
        }

        return mainWindow;
    }

    /// <summary>
    /// 启动后台 Win32 消息泵，失败时降级而不中断启动。
    /// </summary>
    /// <remarks>
    /// 截图与输入模拟不依赖消息泵，消息泵失败时这些功能仍可用，
    /// 因此只提示而不终止进程，避免把局部故障升级为整体不可用。
    /// 失败原因（超时、线程创建失败等）已由 <see cref="BackgroundMessagePump.Start"/> 抛出并记录。
    /// </remarks>
    /// <param name="pump">待启动的消息泵。</param>
    private void StartMessagePump(IBackgroundMessagePump pump)
    {
        try
        {
            pump.Start();
            _logger.Info("后台消息泵已启动，消息窗口句柄: 0x{Handle:X}", pump.MessageWindowHandle.ToInt64());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "后台消息泵启动失败：全局快捷键、输入钩子与窗口事件将不可用");

            MessageBox.Show(
                "后台 Win32 消息泵启动失败，全局快捷键、输入钩子与窗口事件功能不可用。\r\n\r\n" +
                $"原因：{ex.Message}\r\n\r\n" +
                "截图与输入模拟功能仍可正常使用，详细信息见运行日志。",
                "GameTools",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <inheritdoc />
    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        // 宿主基础设施
        containerRegistry.RegisterSingleton<IBackgroundMessagePump, BackgroundMessagePump>();
        containerRegistry.RegisterInstance(Options);

        // UI 调度器：容器无法自动解析静态属性，须以工厂方式绑定到当前应用线程的调度器。
        // 延迟到首次解析时取值，确保 Application.Current 已完成初始化。
        containerRegistry.RegisterSingleton<Dispatcher>(factoryMethod: _ => Application.Current.Dispatcher);

        // 能力实现
        containerRegistry.RegisterSingleton<IScreenCapture, GdiScreenCapture>();
        containerRegistry.RegisterSingleton<IHotkeyManager, Win32HotkeyManager>();
        containerRegistry.RegisterSingleton<ITextInputSimulator, WindowsInputSimulator>();
        containerRegistry.RegisterSingleton<ILowLevelHookManager, Infrastructure.Hooks.LowLevelHookManager>();
        containerRegistry.RegisterSingleton<IWinEventHookManager, Infrastructure.Hooks.WinEventHookManager>();

        // 版本探测：注册为实例以便替换为注入固定版本的实现
        containerRegistry.RegisterInstance<IOsVersionProvider>(OSVersionHelper.Provider);

        // 视图模型
        containerRegistry.RegisterSingleton<ViewModels.MainViewModel>();

        // 应用层服务：包裹底层能力并处理线程切换与文件系统布局
        containerRegistry.RegisterSingleton<IScreenCaptureService, ScreenCaptureService>();
        containerRegistry.RegisterSingleton<IInputService, InputService>();
        containerRegistry.RegisterSingleton<IHookService, HookService>();
        containerRegistry.RegisterSingleton<IHotkeyService, HotkeyService>();
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
            // 消息泵线程为后台线程，不主动停止会导致进程退出前持续占用资源，
            // 且钩子卸载无法完成。此处必须在 Prism 释放容器单例之前停止，
            // 让仍在运行的钩子有机会在消息线程上完成 Unhook。
            StopMessagePump();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "停止后台消息泵时发生异常");
        }

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
            // 释放单实例互斥量，允许后续实例接管命名对象
            try
            {
                _singleInstance?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "释放单实例互斥量失败");
            }

            Trace.Listeners.Remove(TraceLog.Instance);
            _logger.Info("GameTools 已退出");
            AppLogFactory.Shutdown();
        }
    }

    /// <summary>
    /// 停止后台 Win32 消息泵。
    /// </summary>
    /// <remarks>
    /// Prism 的容器释放不会调用此对象的 <c>Stop</c>（它未实现 <see cref="IDisposable"/>），
    /// 因此必须由宿主显式停止。未启动成功时为空实现，无需处理。
    /// </remarks>
    private void StopMessagePump()
    {
        IBackgroundMessagePump? pump = _messagePump;
        _messagePump = null;

        if (pump is null)
        {
            return;
        }

        pump.Stop();
        _logger.Info("后台消息泵已停止");
    }
}
