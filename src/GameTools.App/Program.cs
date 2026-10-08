using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using GameTools.Core.Enums;
using GameTools.Infrastructure.Capture;
using GameTools.Infrastructure.Hooks;
using GameTools.Infrastructure.Host;
using GameTools.Infrastructure.Hotkeys;
using GameTools.Infrastructure.Input;
using GameTools.Win32.Helpers;

namespace GameTools.App;

internal static class Program
{
    private static BackgroundMessagePump? _messagePump;
    private static Win32HotkeyManager? _hotkeyManager;
    private static GdiScreenCapture? _screenCapture;
    private static WindowsInputSimulator? _inputSimulator;
    private static LowLevelHookManager? _hookManager;
    private static WinEventHookManager? _winEventHookManager;
    private static NotifyIcon? _trayIcon;

    private static bool _isHookActive;
    private static volatile bool _running = true;

    [STAThread]
    private static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("==================================================");
        Console.WriteLine("    GameTools 底层工具框架 (.NET 8 / Win7+ 平台)    ");
        Console.WriteLine("==================================================");
        Console.WriteLine($"[系统信息] {OSVersionHelper.GetOsDescription()}");
        Console.WriteLine($"[平台检测] IsWin7: {OSVersionHelper.IsWindows7}, IsWin8.1+: {OSVersionHelper.IsWindows81OrGreater}, IsWin10+: {OSVersionHelper.IsWindows10OrGreater}");
        Console.WriteLine($"[PrintWindow 推荐标志位] 0x{OSVersionHelper.GetRecommendedPrintWindowFlags():X8}");
        Console.WriteLine("--------------------------------------------------");

        // 1. 单实例互斥保护
        using var singleLock = new SingleInstanceLock("GameTools_Core_Host");
        if (!singleLock.IsOnlyInstance)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[警告] 检测到已有 GameTools 进程在后台运行，请勿重复启动。");
            Console.ResetColor();
            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
            return;
        }

        // 2. 初始化核心基础设施
        _messagePump = new BackgroundMessagePump();
        _messagePump.Start();
        Console.WriteLine($"[后台消息泵] 已在独立 STA 线程启动，句柄: 0x{_messagePump.MessageWindowHandle.ToInt64():X}");

        _hotkeyManager = new Win32HotkeyManager(_messagePump);
        _screenCapture = new GdiScreenCapture();
        _inputSimulator = new WindowsInputSimulator();
        _hookManager = new LowLevelHookManager();
        _winEventHookManager = new WinEventHookManager();

        // 3. 设置键盘鼠标钩子回调
        _hookManager.KeyDown += (_, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[钩子-键盘按下] 按键: {e.Key} (VK: {e.KeyCode}) | 窗口进程: [{e.ProcessName}:{e.ProcessId}]");
            Console.ResetColor();
        };

        _hookManager.MouseEvent += (_, e) =>
        {
            if (e.Message is 0x0201 or 0x0204) // 左键或右键按下
            {
                string btn = e.Message == 0x0201 ? "左键" : "右键";
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine($"[钩子-鼠标点击] {btn} @ ({e.Location.X}, {e.Location.Y}) | 窗口进程: [{e.ProcessName}:{e.ProcessId}]");
                Console.ResetColor();
            }
        };

        _winEventHookManager.WinEventReceived += (_, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"[窗口事件] 事件: {e.EventName} (0x{e.EventType:X4}) | HWND: 0x{e.WindowHandle.ToInt64():X} | PID: {e.ProcessId}");
            Console.ResetColor();
        };

        // 4. 注册全局快捷键
        RegisterDemoHotkeys();

        // 5. 初始化托盘图标
        InitTrayIcon();

        // 6. 控制台交互循环
        RunConsoleMenu();

        // 7. 清理并退出
        Cleanup();
        Console.WriteLine("[系统] GameTools 进程已平稳退出。");
    }

    private static void RegisterDemoHotkeys()
    {
        if (_hotkeyManager == null) return;

        try
        {
            // Ctrl + Shift + S: 截屏
            _hotkeyManager.RegisterHotkey(Keys.S, KeyModifiers.Control | KeyModifiers.Shift, () =>
            {
                Console.WriteLine("\n[快捷键响应] 触发截图 (Ctrl + Shift + S)...");
                PerformScreenCapture();
            });

            // Ctrl + Shift + I: 前台模拟输入文本
            _hotkeyManager.RegisterHotkey(Keys.I, KeyModifiers.Control | KeyModifiers.Shift, () =>
            {
                Console.WriteLine("\n[快捷键响应] 触发前台模拟输入 (Ctrl + Shift + I)...");
                _inputSimulator?.SendText("Hello GameTools! 自动化测试输入成功！🎉\n", delayBetweenCharsMs: 15);
            });

            // Ctrl + Shift + H: 启动/停止钩子
            _hotkeyManager.RegisterHotkey(Keys.H, KeyModifiers.Control | KeyModifiers.Shift, () =>
            {
                ToggleHook();
            });

            // Ctrl + Shift + Q: 退出
            _hotkeyManager.RegisterHotkey(Keys.Q, KeyModifiers.Control | KeyModifiers.Shift, () =>
            {
                Console.WriteLine("\n[快捷键响应] 触发退出 (Ctrl + Shift + Q)...");
                _running = false;
            });

            Console.WriteLine("[快捷键已注册]");
            Console.WriteLine("  - Ctrl + Shift + S : 全屏截图并保存");
            Console.WriteLine("  - Ctrl + Shift + I : 向当前焦点窗口输入 Unicode 文本");
            Console.WriteLine("  - Ctrl + Shift + H : 切换开启/关闭低级键盘鼠标钩子");
            Console.WriteLine("  - Ctrl + Shift + Q : 退出后台程序");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[快捷键注册警告] {ex.Message}");
            Console.ResetColor();
        }
    }

    private static void InitTrayIcon()
    {
        _messagePump?.PostAction(() =>
        {
            _trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "GameTools 后台服务",
                Visible = true
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("全屏截图", null, (_, _) => PerformScreenCapture());
            contextMenu.Items.Add("切换钩子监听", null, (_, _) => ToggleHook());
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("退出", null, (_, _) => { _running = false; });

            _trayIcon.ContextMenuStrip = contextMenu;
        });
    }

    private static void RunConsoleMenu()
    {
        while (_running)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("============= [控制台交互指令] =============");
            Console.WriteLine(" [1] 立即截取全屏并保存为 PNG");
            Console.WriteLine(" [2] 列出当前所有窗口并执行指定窗口后台截图");
            Console.WriteLine(" [3] 向指定窗口后台投递文本 (PostMessage WM_CHAR)");
            Console.WriteLine(" [4] 前台模拟 SendInput 输入 Unicode 文本");
            Console.WriteLine(" [5] 开启/关闭全局键盘鼠标钩子 (当前状态: " + (_isHookActive ? "开启" : "关闭") + ")");
            Console.WriteLine(" [6] 监听指定进程的窗口生命周期事件 (WinEventHook)");
            Console.WriteLine(" [0] 退出程序");
            Console.WriteLine("============================================");
            Console.ResetColor();
            Console.Write("请输入选项序号: ");

            var keyInfo = Console.ReadKey();
            Console.WriteLine();

            switch (keyInfo.KeyChar)
            {
                case '1':
                    PerformScreenCapture();
                    break;
                case '2':
                    PerformWindowCaptureMenu();
                    break;
                case '3':
                    PerformPostTextMessageMenu();
                    break;
                case '4':
                    Console.WriteLine("请在 3 秒内将光标切换到目标输入框...");
                    Thread.Sleep(3000);
                    _inputSimulator?.SendText("这是由 GameTools SendInput 模拟输入的中文测试内容！\n");
                    Console.WriteLine("[完成] 已成功模拟输入。");
                    break;
                case '5':
                    ToggleHook();
                    break;
                case '6':
                    StartProcessWinEventMonitor();
                    break;
                case '0':
                    _running = false;
                    break;
            }
        }
    }

    private static void PerformScreenCapture()
    {
        if (_screenCapture == null) return;

        string outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Screenshots");
        Directory.CreateDirectory(outDir);

        using var result = _screenCapture.CaptureFullScreen(allMonitors: true);
        if (result.Success && result.Image != null)
        {
            string fileName = Path.Combine(outDir, $"Capture_Fullscreen_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            result.Image.Save(fileName, ImageFormat.Png);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[截图成功] 保存到: {fileName} | 尺寸: {result.Image.Width}x{result.Image.Height} | 耗时: {result.Elapsed.TotalMilliseconds:F1}ms");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[截图失败] {result.ErrorMessage}");
            Console.ResetColor();
        }
    }

    private static void PerformWindowCaptureMenu()
    {
        var windows = WindowHelper.FindTopLevelWindows();
        Console.WriteLine($"\n找到 {windows.Count} 个顶级可见窗口:");

        for (int i = 0; i < Math.Min(windows.Count, 15); i++)
        {
            var w = windows[i];
            Console.WriteLine($" [{i}] HWND: 0x{w.Handle.ToInt64():X8} | PID: {w.ProcessId} ({w.ProcessName}) | 尺寸: {w.Bounds.Width}x{w.Bounds.Height} | 标题: {w.Title}");
        }

        Console.Write("请输入要截取的窗口序号: ");
        string? input = Console.ReadLine();
        if (int.TryParse(input, out int idx) && idx >= 0 && idx < windows.Count)
        {
            var target = windows[idx];
            Console.WriteLine($"正在尝试后台截图 HWND: 0x{target.Handle.ToInt64():X8}...");

            using var res = _screenCapture?.CaptureWindow(target.Handle, allowFallbackToDesktop: true);
            if (res != null && res.Success && res.Image != null)
            {
                string outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Screenshots");
                Directory.CreateDirectory(outDir);
                string fileName = Path.Combine(outDir, $"Window_{target.ProcessName}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                res.Image.Save(fileName, ImageFormat.Png);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[窗口截图成功] 模式: {res.Mode} | 文件: {fileName} | 尺寸: {res.Image.Width}x{res.Image.Height}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[窗口截图失败] {res?.ErrorMessage}");
                Console.ResetColor();
            }
        }
    }

    private static void PerformPostTextMessageMenu()
    {
        var windows = WindowHelper.FindTopLevelWindows();
        Console.WriteLine("\n请选择要后台投递文本的目标窗口:");
        for (int i = 0; i < Math.Min(windows.Count, 10); i++)
        {
            var w = windows[i];
            Console.WriteLine($" [{i}] PID: {w.ProcessId} ({w.ProcessName}) | 标题: {w.Title}");
        }

        Console.Write("请输入窗口序号: ");
        if (int.TryParse(Console.ReadLine(), out int idx) && idx >= 0 && idx < windows.Count)
        {
            var target = windows[idx];
            Console.Write("请输入要投递的文本内容: ");
            string? text = Console.ReadLine();
            if (!string.IsNullOrEmpty(text))
            {
                _inputSimulator?.PostTextToWindow(target.Handle, text + "\r\n");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[完成] 已向窗口 0x{target.Handle.ToInt64():X} 投递字符。");
                Console.ResetColor();
            }
        }
    }

    private static void ToggleHook()
    {
        if (_hookManager == null) return;

        if (_isHookActive)
        {
            _hookManager.StopKeyboardHook();
            _hookManager.StopMouseHook();
            _isHookActive = false;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n[钩子状态] 低级键盘与鼠标钩子已停止。");
            Console.ResetColor();
        }
        else
        {
            _hookManager.StartKeyboardHook();
            _hookManager.StartMouseHook();
            _isHookActive = true;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n[钩子状态] 全局低级键盘与鼠标钩子已启动！请随意按键或点击鼠标进行测试。");
            Console.ResetColor();
        }
    }

    private static void StartProcessWinEventMonitor()
    {
        Console.Write("\n请输入要监听的目标进程 PID: ");
        if (int.TryParse(Console.ReadLine(), out int pid))
        {
            try
            {
                _winEventHookManager?.Stop();
                _winEventHookManager?.Start(pid);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[完成] 已开始监听 PID={pid} 的窗口事件，请在该进程中移动/切换窗口进行测试。");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[失败] {ex.Message}");
                Console.ResetColor();
            }
        }
    }

    private static void Cleanup()
    {
        _trayIcon?.Dispose();
        _hookManager?.Dispose();
        _winEventHookManager?.Dispose();
        _hotkeyManager?.Dispose();
        _messagePump?.Dispose();
    }
}
