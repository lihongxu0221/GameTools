using System.Runtime.InteropServices;

namespace GameTools.Win32.Native;

/// <summary>
/// ntdll 原生导出封装。
/// </summary>
public static class Ntdll
{
    private const string LibraryName = "ntdll.dll";

    /// <summary>
    /// 调用 RtlGetVersion 获取真实的操作系统版本信息。
    /// 该 API 不依赖应用清单中的 supportedOS 声明，因此在 .NET Framework 目标上不会像
    /// <c>Environment.OSVersion</c> 那样被 shim 截断，从而保证 net48 与 net8.0-windows
    /// 两个目标的能力判定结果一致。
    /// </summary>
    /// <param name="versionInfo">输出结构；调用前必须正确设置 <c>dwOSVersionInfoSize</c>。</param>
    /// <returns>NTSTATUS 值，0（STATUS_SUCCESS）表示成功。</returns>
    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern int RtlGetVersion(ref RTL_OSVERSIONINFOEX versionInfo);
}