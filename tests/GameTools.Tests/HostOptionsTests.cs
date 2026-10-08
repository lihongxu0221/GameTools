using GameTools.App.Services;
using Xunit;

namespace GameTools.Tests;

/// <summary>
/// <see cref="HostOptions"/> 的命令行解析测试。
/// </summary>
public sealed class HostOptionsTests
{
    [Fact]
    public void Parse_WithNoArguments_ShouldReturnInteractive()
    {
        Assert.Equal(HostMode.Interactive, HostOptions.Parse(Array.Empty<string>()).Mode);
    }

    [Theory]
    [InlineData("--tray")]
    [InlineData("--TRAY")]
    [InlineData("  --tray  ")]
    public void Parse_WithTrayFlag_ShouldReturnTrayMode(string arg)
    {
        Assert.Equal(HostMode.Tray, HostOptions.Parse(new[] { arg }).Mode);
    }

    [Theory]
    [InlineData("--headless")]
    [InlineData("--HEADLESS")]
    public void Parse_WithHeadlessFlag_ShouldReturnHeadlessMode(string arg)
    {
        Assert.Equal(HostMode.Headless, HostOptions.Parse(new[] { arg }).Mode);
    }

    [Fact]
    public void Parse_WithUnknownArgument_ShouldKeepInteractive()
    {
        // 未知参数不得让程序静默进入无头模式，否则用户会以为程序未启动
        Assert.Equal(HostMode.Interactive, HostOptions.Parse(new[] { "--unknown" }).Mode);
    }

    [Fact]
    public void Parse_WithBlankArgument_ShouldKeepInteractive()
    {
        Assert.Equal(HostMode.Interactive, HostOptions.Parse(new[] { "   " }).Mode);
    }

    [Fact]
    public void Parse_WithFlagAndTrailingValue_ShouldMatchFlag()
    {
        // 支持 `--flag=value` 形式
        Assert.Equal(HostMode.Tray, HostOptions.Parse(new[] { "--tray=true" }).Mode);
    }

    [Fact]
    public void Parse_WithNullArray_ShouldReturnInteractive()
    {
        Assert.Equal(HostMode.Interactive, HostOptions.Parse(null!).Mode);
    }

    [Fact]
    public void Parse_WithBothFlags_ShouldPreferHeadlessRegardlessOfOrder()
    {
        // 无界面意图优先级最高，与参数顺序无关
        Assert.Equal(HostMode.Headless, HostOptions.Parse(new[] { "--headless", "--tray" }).Mode);
        Assert.Equal(HostMode.Headless, HostOptions.Parse(new[] { "--tray", "--headless" }).Mode);
    }
}