namespace GameTools.Win32.Helpers;

/// <summary>
/// 操作系统版本与能力探测契约。
/// 抽象此类目的是使版本判定逻辑可注入、可单元测试：真实系统版本与各能力分支
/// 不再依赖运行环境的实际版本，从而可以在单测中覆盖 Windows 7/8/8.1/10/11 全部分支。
/// </summary>
public interface IOsVersionProvider
{
    /// <summary>
    /// 获取当前运行系统的真实版本号（不受应用清单 shim 截断影响）。
    /// </summary>
    Version GetVersion();

    /// <summary>
    /// 获取当前运行系统的构建号（用于区分同版本号的不同修订）。
    /// </summary>
    uint GetBuildNumber();

    /// <summary>
    /// 是否为 Windows 8.1 或更高版本。
    /// Windows 8.1 引入 <c>PW_RENDERFULLCONTENT</c>，是截图降级策略的分界点。
    /// </summary>
    bool IsWindows81OrGreater { get; }

    /// <summary>
    /// 是否为 Windows 10 或更高版本。
    /// </summary>
    bool IsWindows10OrGreater { get; }

    /// <summary>
    /// 获取用于 PrintWindow 的推荐标志位。
    /// </summary>
    /// <param name="clientAreaOnly">是否仅抓取客户区。</param>
    uint GetRecommendedPrintWindowFlags(bool clientAreaOnly = false);

    /// <summary>
    /// 获取系统描述文本，供日志与诊断使用。
    /// </summary>
    string GetOsDescription();
}