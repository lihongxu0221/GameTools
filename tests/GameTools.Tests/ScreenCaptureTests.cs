using System.Drawing;
using GameTools.Core.Enums;
using GameTools.Infrastructure.Capture;
using Xunit;

namespace GameTools.Tests;

public class ScreenCaptureTests
{
    [Fact]
    public void CaptureFullScreen_ShouldReturnValidImage()
    {
        var capture = new GdiScreenCapture();
        using var result = capture.CaptureFullScreen(allMonitors: false);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Image);
        Assert.True(result.Image.Width > 0);
        Assert.True(result.Image.Height > 0);
        Assert.Equal(CaptureMode.DesktopBitBlt, result.Mode);
    }

    [Fact]
    public void CaptureRegion_ShouldReturnExactDimensions()
    {
        var capture = new GdiScreenCapture();
        var region = new Rectangle(0, 0, 100, 80);

        using var result = capture.CaptureRegion(region);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Image);
        Assert.Equal(100, result.Image.Width);
        Assert.Equal(80, result.Image.Height);
    }

    [Fact]
    public void CaptureRegion_InvalidRegion_ShouldReturnFailure()
    {
        var capture = new GdiScreenCapture();
        var invalidRegion = new Rectangle(0, 0, 0, 0);

        using var result = capture.CaptureRegion(invalidRegion);

        Assert.False(result.Success);
        Assert.Null(result.Image);
    }
}
