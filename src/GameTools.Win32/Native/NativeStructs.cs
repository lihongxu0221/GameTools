using System.Runtime.InteropServices;

namespace GameTools.Win32.Native;

[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X;
    public int Y;

    public POINT(int x, int y)
    {
        X = x;
        Y = y;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public int Width => Right - Left;
    public int Height => Bottom - Top;

    /// <summary>
    /// 转换为 GDI+ 矩形。仅在基础设施层需要位图尺寸时使用。
    /// </summary>
    public System.Drawing.Rectangle ToRectangle() =>
        new(Left, Top, Width, Height);

    /// <summary>
    /// 转换为平台无关的矩形值对象。
    /// </summary>
    public GameTools.Core.Models.CaptureBounds ToCaptureBounds() =>
        new(Left, Top, Width, Height);
}

[StructLayout(LayoutKind.Sequential)]
public struct INPUT
{
    public uint Type;
    public InputUnion Data;
}

[StructLayout(LayoutKind.Explicit)]
public struct InputUnion
{
    [FieldOffset(0)]
    public MOUSEINPUT Mouse;

    [FieldOffset(0)]
    public KEYBDINPUT Keyboard;

    [FieldOffset(0)]
    public HARDWAREINPUT Hardware;
}

[StructLayout(LayoutKind.Sequential)]
public struct KEYBDINPUT
{
    public ushort Vk;
    public ushort Scan;
    public uint Flags;
    public uint Time;
    public UIntPtr ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct MOUSEINPUT
{
    public int Dx;
    public int Dy;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public UIntPtr ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct HARDWAREINPUT
{
    public uint Msg;
    public ushort ParamL;
    public ushort ParamH;
}

[StructLayout(LayoutKind.Sequential)]
public struct KBDLLHOOKSTRUCT
{
    public uint VkCode;
    public uint ScanCode;
    public uint Flags;
    public uint Time;
    public UIntPtr ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct MSLLHOOKSTRUCT
{
    public POINT Pt;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public UIntPtr ExtraInfo;
}

/// <summary>
/// RTL_OSVERSIONINFOEX 结构，用于调用 ntdll 的 RtlGetVersion 获取未经 shim 截断的真实版本信息。
/// 与 <see cref="Environment.OSVersion"/> 不同，该结构不受应用清单 supportedOS 声明缺失的影响。
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct RTL_OSVERSIONINFOEX
{
    /// <summary>结构长度，必须为 sizeof(OSVERSIONINFOEXW)。</summary>
    public uint dwOSVersionInfoSize;

    /// <summary>主版本号。</summary>
    public uint dwMajorVersion;

    /// <summary>次版本号。</summary>
    public uint dwMinorVersion;

    /// <summary>构建号。</summary>
    public uint dwBuildNumber;

    /// <summary>平台标识，VER_PLATFORM_WIN32_NT 为 2。</summary>
    public uint dwPlatformId;

    /// <summary>服务包名称（预留字段，固定为空字符串）。</summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string szCSDVersion;
}

/// <summary>
/// 原生 MSG 结构，对应 Win32 MSG。
/// </summary>
/// <remarks>
/// 不得使用 <c>System.Windows.Forms.Message</c> 替代：前者仅含 hWnd、msg、wParam、lParam、result 五个成员，
/// 缺少 time、pt、lPrivate 字段，作为 P/Invoke 参数会导致结构体大小不符与内存越界读取。
/// </remarks>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
public struct MSG
{
    /// <summary>窗口句柄。</summary>
    public IntPtr hwnd;

    /// <summary>消息标识。</summary>
    public uint message;

    /// <summary>附加消息信息。</summary>
    public IntPtr wParam;

    /// <summary>附加消息信息。</summary>
    public IntPtr lParam;

    /// <summary>消息入队时间。</summary>
    public uint time;

    /// <summary>消息入队时的光标位置。</summary>
    public POINT pt;

    /// <summary>内部使用。</summary>
    public uint lPrivate;
}
