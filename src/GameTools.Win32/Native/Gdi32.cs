using System.Runtime.InteropServices;
using GameTools.Win32.SafeHandles;

namespace GameTools.Win32.Native;

public static class Gdi32
{
    private const string LibraryName = "gdi32.dll";

    [DllImport(LibraryName, SetLastError = true)]
    public static extern SafeGdiDcHandle CreateCompatibleDC(IntPtr hdc);

    [DllImport(LibraryName, SetLastError = true)]
    public static extern SafeGdiObjectHandle CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [DllImport(LibraryName, SetLastError = true)]
    public static extern IntPtr SelectObject(SafeGdiDcHandle hdc, SafeGdiObjectHandle h);

    [DllImport(LibraryName, SetLastError = true)]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BitBlt(
        SafeGdiDcHandle hdcDest,
        int xDest,
        int yDest,
        int wDest,
        int hDest,
        IntPtr hdcSource,
        int xSrc,
        int ySrc,
        int rop);

    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BitBlt(
        IntPtr hdcDest,
        int xDest,
        int yDest,
        int wDest,
        int hDest,
        IntPtr hdcSource,
        int xSrc,
        int ySrc,
        int rop);

    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);
}
