using System.Runtime.InteropServices;
using System.Text;

namespace GameTools.Win32.Native;

public static class User32
{
    private const string LibraryName = "user32.dll";

    [DllImport(LibraryName, SetLastError = true)]
    public static extern IntPtr GetDesktopWindow();

    [DllImport(LibraryName, SetLastError = true)]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport(LibraryName, SetLastError = true)]
    public static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport(LibraryName, SetLastError = true)]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBmp, uint nFlags);

    [DllImport(LibraryName, SetLastError = true)]
    public static extern IntPtr GetForegroundWindow();

    [DllImport(LibraryName, SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    /// <summary>
    /// 枚举指定窗口的全部子窗口。
    /// </summary>
    /// <remarks>
    /// 传统 Win32 控件的发现方式。现代应用基本不适用：实测 Chromium 内核窗口
    /// 仅有 1 个 <c>Intermediate D3D Window</c> 子窗口，WPF 控件则没有独立 HWND。
    /// </remarks>
    /// <param name="hWndParent">父窗口句柄。</param>
    /// <param name="lpEnumFunc">枚举回调。</param>
    /// <param name="lParam">回调参数。</param>
    /// <returns>枚举是否成功。</returns>
    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    /// <summary>
    /// 判断窗口是否处于可用状态。
    /// </summary>
    /// <param name="hWnd">窗口句柄。</param>
    /// <returns>可用时返回 true。</returns>
    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowEnabled(IntPtr hWnd);

    /// <summary>
    /// 获取控件在对话框中的标识。
    /// </summary>
    /// <param name="hWnd">控件句柄。</param>
    /// <returns>控件 ID；无 ID 时返回 0。</returns>
    [DllImport(LibraryName, SetLastError = true)]
    public static extern int GetDlgCtrlID(IntPtr hWnd);

    /// <summary>
    /// 获取窗口的祖先窗口。
    /// </summary>
    /// <param name="hWnd">窗口句柄。</param>
    /// <param name="uFlags">祖先类型，取值见 <see cref="NativeConstants.GA_ROOT"/>。</param>
    /// <returns>祖先窗口句柄；失败时返回 0。</returns>
    [DllImport(LibraryName, SetLastError = true)]
    public static extern IntPtr GetAncestor(IntPtr hWnd, uint uFlags);

    /// <summary>
    /// 获取窗口的父窗口或拥有者窗口。
    /// </summary>
    /// <param name="hWnd">窗口句柄。</param>
    /// <returns>父窗口句柄；无父窗口时返回 0。</returns>
    [DllImport(LibraryName, SetLastError = true)]
    public static extern IntPtr GetParent(IntPtr hWnd);

    /// <summary>
    /// 命中测试：返回指定点上最深的子窗口。
    /// </summary>
    /// <remarks>
    /// 特征识图定位的落点由此解析为句柄。传入坐标为父窗口客户区坐标。
    /// </remarks>
    /// <param name="hWndParent">父窗口句柄。</param>
    /// <param name="pt">客户区坐标。</param>
    /// <param name="uFlags">跳过规则，见 <c>CWP_*</c> 常量。</param>
    /// <returns>命中的子窗口句柄；未命中返回 0。</returns>
    [DllImport(LibraryName, SetLastError = true)]
    public static extern IntPtr ChildWindowFromPointEx(IntPtr hWndParent, POINT pt, uint uFlags);

    /// <summary>
    /// 返回指定屏幕坐标处的窗口句柄。
    /// </summary>
    /// <param name="point">屏幕坐标。</param>
    /// <returns>命中的窗口句柄。</returns>
    [DllImport(LibraryName, SetLastError = true)]
    public static extern IntPtr WindowFromPoint(POINT point);

    // 鼠标状态 APIs

    /// <summary>
    /// 读取当前鼠标光标在屏幕上的位置。
    /// </summary>
    /// <remarks>
    /// 用于校验后台操作是否真的没有影响实体鼠标：消息投递路径不应改变该值。
    /// </remarks>
    /// <param name="point">输出光标位置。</param>
    /// <returns>是否读取成功。</returns>
    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT point);

    /// <summary>
    /// 移动鼠标光标到指定屏幕位置。
    /// </summary>
    /// <remarks>
    /// 会真实改变实体鼠标的位置，进而影响用户正在操作的窗口。
    /// 仅在明确需要光标实际就位的场景使用，且必须成对还原。
    /// </remarks>
    /// <param name="x">目标屏幕横坐标。</param>
    /// <param name="y">目标屏幕纵坐标。</param>
    /// <returns>是否移动成功。</returns>
    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCursorPos(int x, int y);

    /// <summary>
    /// 返回当前捕获鼠标消息的窗口句柄。
    /// </summary>
    /// <remarks>
    /// 目标程序收到鼠标按下消息后可能调用 <c>SetCapture</c> 把自己设为捕获者。
    /// 若不处理，实体鼠标会被困在该窗口：光标移出后点击仍被其接收。
    /// </remarks>
    /// <returns>捕获窗口句柄；无捕获时返回 <see cref="IntPtr.Zero"/>。</returns>
    [DllImport(LibraryName)]
    public static extern IntPtr GetCapture();

    /// <summary>
    /// 释放当前线程的鼠标捕获。
    /// </summary>
    /// <remarks>
    /// 只有捕获该鼠标的线程才能释放。仅当目标程序因我们的点击而取得捕获时调用。
    /// </remarks>
    /// <returns>是否释放成功。</returns>
    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReleaseCapture();

    /// <summary>
    /// 在两个窗口坐标系之间转换点。
    /// </summary>
    /// <remarks>
    /// 特征识图命中点位位于捕获帧坐标系，需经此转换为目标窗口客户区坐标，
    /// 才能交给 <see cref="ChildWindowFromPointEx"/> 做命中测试。
    /// </remarks>
    /// <param name="hWndFrom">源窗口句柄；为 0 表示屏幕坐标。</param>
    /// <param name="hWndTo">目标窗口句柄；为 0 表示屏幕坐标。</param>
    /// <param name="lpPoint">待转换的坐标点。</param>
    /// <param name="cPoints">坐标点数量。</param>
    /// <returns>成功转换的坐标点数量。</returns>
    [DllImport(LibraryName, SetLastError = true)]
    public static extern int MapWindowPoints(IntPtr hWndFrom, IntPtr hWndTo, ref POINT lpPoint, uint cPoints);

    /// <summary>
    /// 将窗口客户区坐标转换为屏幕坐标。
    /// </summary>
    /// <param name="hWnd">窗口句柄。</param>
    /// <param name="lpPoint">待转换的坐标点。</param>
    /// <returns>转换是否成功。</returns>
    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    /// <summary>
    /// 将屏幕坐标转换为窗口客户区坐标。
    /// </summary>
    /// <param name="hWnd">窗口句柄。</param>
    /// <param name="lpPoint">待转换的坐标点。</param>
    /// <returns>转换是否成功。</returns>
    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport(LibraryName, SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport(LibraryName, SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport(LibraryName, SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport(LibraryName, SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport(LibraryName, SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowTitle);

    // Hotkey APIs
    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // SendInput API
    [DllImport(LibraryName, SetLastError = true)]
    public static extern uint SendInput(uint nInputs, [MarshalAs(UnmanagedType.LPArray), In] INPUT[] pInputs, int cbSize);

    // Messages APIs
    [DllImport(LibraryName, SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport(LibraryName, SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport(LibraryName, SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, [MarshalAs(UnmanagedType.LPWStr)] string lParam);

    // Hook APIs
    [DllImport(LibraryName, SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport(LibraryName, SetLastError = true)]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    // WinEvent APIs
    /// <summary>
    /// 注册窗口事件钩子。
    /// 传入空的 <c>lpfnWinEventProc</c> 与全零范围可解除该事件类型的订阅。
    /// </summary>
    /// <remarks>
    /// <c>WINEVENT_OUTOFCONTEXT</c> 事件派发到创建该钩子的线程，
    /// 因此调用必须发生在运行消息循环的线程上。
    /// </remarks>
    [DllImport(LibraryName, SetLastError = true)]
    public static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate? lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    /// <summary>
    /// 发送带超时的同步消息。跨进程调用必须使用本方法：目标进程挂起时
    /// 同步 SendMessage 会让调用方无限阻塞。
    /// </summary>
    [DllImport(LibraryName, SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        [MarshalAs(UnmanagedType.LPWStr)] string lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    /// <summary>
    /// 发送带超时的同步消息（数值参数版本）。
    /// </summary>
    [DllImport(LibraryName, SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    // Message loop APIs

    /// <summary>
    /// 检索队列中的消息。
    /// </summary>
    /// <remarks>
    /// 使用原生 <see cref="MSG"/> 结构与 BOOL 返回值。
    /// 历史缺陷：此前误用 WinForms 的 <c>System.Windows.Forms.Message</c> 作为 MSG 参数、
    /// 且返回类型声明为 <c>sbyte</c>，两者均与 Win32 契约不符，会导致结构越界与返回判断错误。
    /// </remarks>
    /// <returns>非零表示取到消息；0 表示收到 WM_QUIT；-1 表示出错（见 Marshal.GetLastWin32Error）。</returns>
    [DllImport(LibraryName, SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    /// <summary>
    /// 转换消息。
    /// </summary>
    [DllImport(LibraryName, SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    /// <summary>
    /// 向窗口过程派发消息。
    /// </summary>
    [DllImport(LibraryName, SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    /// <summary>
    /// 向消息队列投递 WM_QUIT。
    /// </summary>
    [DllImport(LibraryName, SetLastError = true)]
    public static extern void PostQuitMessage(int nExitCode);

    /// <summary>
    /// 判断窗口句柄是否仍指向有效窗口。
    /// </summary>
    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

}
