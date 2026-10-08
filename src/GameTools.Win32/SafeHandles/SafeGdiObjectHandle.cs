using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GameTools.Win32.SafeHandles;

/// <summary>
/// GDI 对象句柄（如 HBITMAP, HFONT, HBRUSH），在释放时自动调用 DeleteObject
/// </summary>
public sealed class SafeGdiObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeGdiObjectHandle() : base(true)
    {
    }

    public SafeGdiObjectHandle(IntPtr handle, bool ownsHandle = true) : base(ownsHandle)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        return DeleteObject(handle);
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);
}
