using System.Runtime.InteropServices;
using GameTools.Win32.Native;

namespace GameTools.Win32.Helpers;

/// <summary>
/// 操作系统版本与能力探测的默认实现。
/// </summary>
/// <remarks>
/// 版本信息统一通过 <c>ntdll!RtlGetVersion</c> 获取，而不是 <c>Environment.OSVersion</c>。
/// 原因：.NET Framework 目标在缺少应用清单 <c>supportedOS</c> 声明时，
/// <c>Environment.OSVersion</c> 会在 Windows 8.1 及以上被 shim 截断为 6.2，
/// 导致 <c>PW_RENDERFULLCONTENT</c> 分支在 Windows 10/11 上被错误禁用；
/// 使用 <c>RtlGetVersion</c> 可保证 net48 与 net8.0-windows 两个目标的判定结果一致。
/// </remarks>
public sealed class OSVersionProvider : IOsVersionProvider
{
    private readonly Version _version;
    private readonly uint _buildNumber;

    /// <summary>
    /// 使用真实系统版本初始化。
    /// </summary>
    public OSVersionProvider()
    {
        _version = QueryRealVersion();
        _buildNumber = QueryBuildNumber();
    }

    /// <summary>
    /// 使用指定版本初始化，供单元测试注入特定操作系统版本。
    /// </summary>
    /// <param name="version">目标版本号。</param>
    /// <param name="buildNumber">构建号。</param>
    public OSVersionProvider(Version version, uint buildNumber)
    {
        _version = version ?? throw new ArgumentNullException(nameof(version));
        _buildNumber = buildNumber;
    }

    /// <inheritdoc />
    public Version GetVersion() => _version;

    /// <inheritdoc />
    public uint GetBuildNumber() => _buildNumber;

    /// <inheritdoc />
    public bool IsWindows81OrGreater =>
        _version.Major > 6 || (_version.Major == 6 && _version.Minor >= 3);

    /// <inheritdoc />
    public bool IsWindows10OrGreater => _version.Major >= 10;

    /// <inheritdoc />
    public uint GetRecommendedPrintWindowFlags(bool clientAreaOnly = false)
    {
        if (clientAreaOnly)
        {
            return 0x00000001u; // PW_CLIENTONLY
        }

        // PW_RENDERFULLCONTENT 自 Windows 8.1 起支持，可正确抓取硬件加速与 DWM 渲染窗口
        return IsWindows81OrGreater ? 0x00000002u : 0x00000000u;
    }

    /// <inheritdoc />
    public string GetOsDescription()
    {
        return $"{RuntimeInformation.OSDescription} (Version: {_version}, Build: {_buildNumber})";
    }

    private static Version QueryRealVersion()
    {
        if (TryQueryRtlVersion(out RTL_OSVERSIONINFOEX info) && info.dwMajorVersion != 0)
        {
            return new Version(
                (int)info.dwMajorVersion,
                (int)info.dwMinorVersion,
                (int)info.dwBuildNumber);
        }

        // 兜底：极少数环境下 RtlGetVersion 不可用时退回 CLR 版本信息
        Version fallback = Environment.OSVersion.Version;
        return new Version(fallback.Major, fallback.Minor, 0);
    }

    private static uint QueryBuildNumber()
    {
        return TryQueryRtlVersion(out RTL_OSVERSIONINFOEX info) ? info.dwBuildNumber : 0u;
    }

    private static bool TryQueryRtlVersion(out RTL_OSVERSIONINFOEX info)
    {
        info = default;
        info.dwOSVersionInfoSize = (uint)Marshal.SizeOf(typeof(RTL_OSVERSIONINFOEX));

        try
        {
            return Ntdll.RtlGetVersion(ref info) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }
}

/// <summary>
/// 操作系统能力探测的静态入口。
/// </summary>
/// <remarks>
/// 内部持有单例 <see cref="OSVersionProvider"/> 以保持既有静态调用点可用；
/// 需要注入指定版本的单元测试应直接构造 <see cref="OSVersionProvider"/> 或
/// 通过构造函数注入 <see cref="IOsVersionProvider"/>，不要使用本类的静态成员。
/// </remarks>
public static class OSVersionHelper
{
    private static readonly OSVersionProvider _provider = new();

    /// <summary>
    /// 默认版本探测器实例。
    /// </summary>
    public static IOsVersionProvider Provider => _provider;

    /// <summary>
    /// 是否为 Windows 7（内部版本 6.1）。
    /// </summary>
    public static bool IsWindows7 => _provider.GetVersion().Major == 6 && _provider.GetVersion().Minor == 1;

    /// <summary>
    /// 是否为 Windows 8（内部版本 6.2）。
    /// </summary>
    public static bool IsWindows8 => _provider.GetVersion().Major == 6 && _provider.GetVersion().Minor == 2;

    /// <summary>
    /// 是否为 Windows 8.1 或更高版本（大于等于 6.3）。
    /// </summary>
    public static bool IsWindows81OrGreater => _provider.IsWindows81OrGreater;

    /// <summary>
    /// 是否为 Windows 10 或更高版本（大于等于 10.0）。
    /// </summary>
    public static bool IsWindows10OrGreater => _provider.IsWindows10OrGreater;

    /// <summary>
    /// 获取当前系统下 PrintWindow 的推荐标志位。
    /// </summary>
    /// <param name="clientAreaOnly">是否仅抓取客户区。</param>
    public static uint GetRecommendedPrintWindowFlags(bool clientAreaOnly = false)
        => _provider.GetRecommendedPrintWindowFlags(clientAreaOnly);

    /// <summary>
    /// 格式化系统描述信息。
    /// </summary>
    public static string GetOsDescription() => _provider.GetOsDescription();
}