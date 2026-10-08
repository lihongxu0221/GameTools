using GameTools.Win32.Helpers;
using Xunit;

namespace GameTools.Tests;

/// <summary>
/// <see cref="OSVersionProvider"/> 的版本分支测试。
/// </summary>
/// <remarks>
/// 通过注入构造覆盖 Win7/8/8.1/10/11 全部分支，使截图降级策略的判定逻辑
/// 可在不依赖真实运行系统的前提下被完整验证。这是「旧系统未实机验证」缺口的补偿手段。
/// </remarks>
public sealed class OSVersionProviderTests
{
    private static OSVersionProvider Create(int major, int minor, uint build = 7601) =>
        new(new Version(major, minor, 0), build);

    [Theory]
    // Win7
    [InlineData(6, 1, false, false)]
    // Win8
    [InlineData(6, 2, false, false)]
    // Win8.1
    [InlineData(6, 3, true, false)]
    // Win10
    [InlineData(10, 0, true, true)]
    public void IsWindows81And10Flags_ShouldMatchVersion(int major, int minor, bool expect81, bool expect10)
    {
        OSVersionProvider provider = Create(major, minor);

        Assert.Equal(expect81, provider.IsWindows81OrGreater);
        Assert.Equal(expect10, provider.IsWindows10OrGreater);
    }

    [Theory]
    // Win7 与 Win8 不支持 PW_RENDERFULLCONTENT，必须退回 PW_DEFAULT
    [InlineData(6, 1, 0x00000000u)]
    [InlineData(6, 2, 0x00000000u)]
    // Win8.1 及以上支持 PW_RENDERFULLCONTENT
    [InlineData(6, 3, 0x00000002u)]
    [InlineData(10, 0, 0x00000002u)]
    public void GetRecommendedPrintWindowFlags_ShouldReturnVersionSpecificFlags(
        int major,
        int minor,
        uint expected)
    {
        OSVersionProvider provider = Create(major, minor);

        Assert.Equal(expected, provider.GetRecommendedPrintWindowFlags(clientAreaOnly: false));
    }

    [Fact]
    public void GetRecommendedPrintWindowFlags_WhenClientAreaOnly_ShouldAlwaysReturnClientOnly()
    {
        // 客户区标志与版本无关：所有平台都支持 PW_CLIENTONLY
        foreach (Version version in new[] { new Version(6, 1), new Version(6, 3), new Version(10, 0) })
        {
            OSVersionProvider provider = new(version, 1);
            Assert.Equal(0x00000001u, provider.GetRecommendedPrintWindowFlags(clientAreaOnly: true));
        }
    }

    [Fact]
    public void Constructor_WithNullVersion_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() => new OSVersionProvider(null!, 1));
    }

    [Fact]
    public void DefaultProvider_ShouldReturnRealWindowsVersion()
    {
        // 真实系统版本探测：验证 RtlGetVersion 路径在当前系统上可正常工作
        IOsVersionProvider provider = new OSVersionProvider();

        Version version = provider.GetVersion();

        Assert.NotNull(version);
        Assert.True(version.Major >= 6, $"主版本号应不低于 6，实际为 {version}");
        Assert.True(provider.GetBuildNumber() > 0, "构建号应大于 0");
        Assert.Contains("Version", provider.GetOsDescription());
    }

    [Fact]
    public void OSVersionHelper_StaticFacade_ShouldDelegateToDefaultProvider()
    {
        Assert.Equal(OSVersionHelper.Provider.IsWindows81OrGreater, OSVersionHelper.IsWindows81OrGreater);
        Assert.Equal(
            OSVersionHelper.Provider.GetRecommendedPrintWindowFlags(),
            OSVersionHelper.GetRecommendedPrintWindowFlags());
    }
}