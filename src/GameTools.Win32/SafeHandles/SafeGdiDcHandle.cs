using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GameTools.Win32.SafeHandles;

/// <summary>
/// 内存设备上下文（HDC），释放时自动调用 DeleteDC
/// </summary>
public sealed class SafeGdiDcHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeGdiDcHandle() : base(true)
    {
    }

    public SafeGdiDcHandle(IntPtr handle, bool ownsHandle = true) : base(ownsHandle)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        return DeleteDC(handle);
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);
}
