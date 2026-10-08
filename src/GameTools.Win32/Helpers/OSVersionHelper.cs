using System.Runtime.InteropServices;

namespace GameTools.Win32.Helpers;

/// <summary>
/// 操作系统版本检测与平台能力适配辅助
/// 专用于识别 Windows 7 及以上各版本特性差异
/// </summary>
public static class OSVersionHelper
{
    private static readonly Version _version = Environment.OSVersion.Version;

    /// <summary>
    /// 是否为 Windows 7（内部版本 6.1）
    /// </summary>
    public static bool IsWindows7 => _version.Major == 6 && _version.Minor == 1;

    /// <summary>
    /// 是否为 Windows 8（内部版本 6.2）
    /// </summary>
    public static bool IsWindows8 => _version.Major == 6 && _version.Minor == 2;

    /// <summary>
    /// 是否为 Windows 8.1 或更高版本（>= 6.3）
    /// </summary>
    public static bool IsWindows81OrGreater =>
        _version.Major > 6 || (_version.Major == 6 && _version.Minor >= 3);

    /// <summary>
    /// 是否为 Windows 10 或更高版本（>= 10.0）
    /// </summary>
    public static bool IsWindows10OrGreater => _version.Major >= 10;

    /// <summary>
    /// 获取当前系统下 PrintWindow 的推荐标志位
    /// - Windows 7 / 8: 不支持 PW_RENDERFULLCONTENT (0x02)，必须使用 0 (PW_DEFAULT) 或 1 (PW_CLIENTONLY)
    /// - Windows 8.1+: 推荐优先使用 PW_RENDERFULLCONTENT (0x02)，以便正确抓取硬件加速/DWM 渲染窗口
    /// </summary>
    public static uint GetRecommendedPrintWindowFlags(bool clientAreaOnly = false)
    {
        if (clientAreaOnly)
        {
            return 0x00000001u; // PW_CLIENTONLY
        }

        // Win8.1+ 支持 PW_RENDERFULLCONTENT = 2
        return IsWindows81OrGreater ? 0x00000002u : 0x00000000u;
    }

    /// <summary>
    /// 格式化系统描述信息
    /// </summary>
    public static string GetOsDescription()
    {
        return $"{RuntimeInformation.OSDescription} (Version: {_version})";
    }
}
