using GameTools.App.Services.Logging;
using GameTools.Core.Abstractions;
using GameTools.Infrastructure.Host;
using GameTools.Win32.Helpers;
using NLog;

namespace GameTools.App.Services;

/// <summary>
/// 无界面后台宿主。
/// </summary>
/// <remarks>
/// 适用于以服务方式常驻的场景：不加载 WPF 资源，不创建任何窗口，
/// 仅维持消息泵、单实例互斥与已注册的热键，使进程可由热键驱动执行自动化任务。
/// 生命周期由 <see cref="System.Threading.ManualResetEventSlim"/> 阻塞，
/// 收到退出信号后按注册逆序释放资源。
/// </remarks>
public static class HeadlessHost
{
    /// <summary>
    /// 运行无界面宿主直到收到退出信号。
    /// </summary>
    /// <param name="args">命令行参数，用于日志与诊断。</param>
    /// <returns>进程退出码，0 表示正常退出。</returns>
    public static int Run(string[] args)
    {
        ILogger logger = AppLogFactory.Initialize();
        System.Diagnostics.Trace.Listeners.Add(TraceLog.Instance);

        logger.Info("无界面宿主启动，参数: {Args}", string.Join(" ", args));

        using var singleInstance = new SingleInstanceLock("GameTools_Core_Host");
        if (!singleInstance.IsOnlyInstance)
        {
            logger.Warn("检测到已有实例运行，无界面宿主终止");
            AppLogFactory.Shutdown();
            return 1;
        }

        using var messagePump = new BackgroundMessagePump();
        using var shutdownSignal = new System.Threading.ManualResetEventSlim(false);

        int exitCode = 0;

        try
        {
            messagePump.Start();
            logger.Info("后台消息泵已启动，句柄: 0x{Handle:X}", messagePump.MessageWindowHandle.ToInt64());
            logger.Info("系统信息: {OsInfo}", OSVersionHelper.GetOsDescription());

            // 注册退出处理：Ctrl+C 与系统关闭均需走统一的清理路径
            global::System.Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                shutdownSignal.Set();
            };

            AppDomain.CurrentDomain.ProcessExit += (_, _) => shutdownSignal.Set();

            logger.Info("无界面宿主进入等待状态，按 Ctrl+C 退出");

            // 阻塞等待退出信号；不使用无限循环以便响应 Ctrl+C
            shutdownSignal.Wait();

            logger.Info("收到退出信号，无界面宿主开始清理");
        }
        catch (Exception ex)
        {
            logger.Error(ex, "无界面宿主异常终止");
            exitCode = 2;
        }
        finally
        {
            // 逆序释放：消息泵先停，再释放单实例锁
            messagePump.Stop();
            logger.Info("无界面宿主已退出");
            System.Diagnostics.Trace.Listeners.Remove(TraceLog.Instance);
            AppLogFactory.Shutdown();
        }

        return exitCode;
    }
}