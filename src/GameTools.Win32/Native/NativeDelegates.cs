using System.Runtime.InteropServices;

namespace GameTools.Win32.Native;

/// <summary>
/// Windows 钩子底层回调函数委托
/// </summary>
public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

/// <summary>
/// Windows 事件钩子 (SetWinEventHook) 底层回调函数委托
/// </summary>
public delegate void WinEventDelegate(
    IntPtr hWinEventHook,
    uint eventType,
    IntPtr hWnd,
    int idObject,
    int idChild,
    uint dwEventThread,
    uint dwmsEventTime);

/// <summary>
/// 窗口枚举回调函数委托
/// </summary>
public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
