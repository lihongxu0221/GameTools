using System.Runtime.InteropServices;

namespace GameTools.App.Services;

/// <summary>
/// 进程入口点。
/// </summary>
/// <remarks>
/// 拆分为独立类的原因：WPF 的 <c>App.xaml</c> 会自动生成带 <c>Main</c> 的入口，
/// 但 <c>--headless</c> 模式需要在不加载任何 WPF 资源的前提下启动，
/// 因此必须自行控制入口，并在需要时才进入 WPF 消息循环。
/// 同时避免 <c>STAThread</c> 与 <c>UseWindowsForms</c> 隐式引入的入口冲突。
/// </remarks>
public static class EntryPoint
{
    /// <summary>
    /// 进程入口。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    [STAThread]
    public static int Main(string[] args)
    {
        // 冲突模式优先级高于参数顺序：--headless 与 --tray 同时出现时以 --headless 为准
        HostOptions options = HostOptions.Parse(args);

        if (options.Mode == HostMode.Headless)
        {
            return HeadlessHost.Run(args);
        }

        // 交互与托盘模式均通过 Prism 应用承载
        var app = new App();
        app.InitializeComponent();

        // 无头模式由 App.OnStartup 内部处理（单实例冲突时直接退出），
        // 这里仅在交互模式启动主窗口，托盘模式由 App 自行决定隐藏窗口。
        return app.Run();
    }
}