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
