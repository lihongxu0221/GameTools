using System.Runtime.InteropServices;

namespace GameTools.Win32.Native;

public static class DwmApi
{
    private const string LibraryName = "dwmapi.dll";

    [DllImport(LibraryName, SetLastError = true)]
    public static extern int DwmGetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        out RECT pvAttribute,
        int cbAttribute);
}
