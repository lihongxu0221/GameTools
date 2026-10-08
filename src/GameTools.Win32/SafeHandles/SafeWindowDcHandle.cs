using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GameTools.Win32.SafeHandles;

/// <summary>
/// 窗口或屏幕设备上下文句柄（HDC），释放时自动调用 User32.ReleaseDC(hWnd, hdc)
/// </summary>
public sealed class SafeWindowDcHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly IntPtr _hWnd;

    private SafeWindowDcHandle() : base(true)
    {
    }

    public SafeWindowDcHandle(IntPtr hWnd, IntPtr hdc, bool ownsHandle = true) : base(ownsHandle)
    {
        _hWnd = hWnd;
        SetHandle(hdc);
    }

    protected override bool ReleaseHandle()
    {
        return ReleaseDC(_hWnd, handle) == 1;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
}
