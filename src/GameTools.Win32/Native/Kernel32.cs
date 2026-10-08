using System.Runtime.InteropServices;

namespace GameTools.Win32.Native;

public static class Kernel32
{
    private const string LibraryName = "kernel32.dll";

    [DllImport(LibraryName, CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport(LibraryName)]
    public static extern uint GetCurrentThreadId();

    [DllImport(LibraryName)]
    public static extern uint GetCurrentProcessId();

    [DllImport(LibraryName)]
    public static extern uint GetLastError();
}
