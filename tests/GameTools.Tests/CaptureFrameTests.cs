using GameTools.Core.Models;
using Xunit;

namespace GameTools.Tests;

/// <summary>
/// <see cref="CaptureFrame"/> 与 <see cref="CaptureBounds"/> 的值对象测试。
/// </summary>
/// <remarks>
/// 这些类型不依赖 WinForms、GDI+ 或任何 Win32 调用，可在任意平台运行，
/// 是契约层平台无关性的直接验证。
/// </remarks>
public sealed class CaptureFrameTests
{
    [Fact]
    public void Constructor_WithMatchingLength_ShouldExposeDimensions()
    {
        var frame = new CaptureFrame(2, 3, new byte[2 * 3 * 4]);

        Assert.Equal(2, frame.Width);
        Assert.Equal(3, frame.Height);
        Assert.Equal(24, frame.ByteLength);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void Constructor_WithNonPositiveDimension_ShouldThrow(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureFrame(width, height, Array.Empty<byte>()));
    }

    [Fact]
    public void Constructor_WithNullPixels_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() => new CaptureFrame(1, 1, null!));
    }

    [Fact]
    public void Constructor_WithMismatchedLength_ShouldThrow()
    {
        // 2x2 期望 16 字节，实际仅给 8 字节
        Assert.Throws<ArgumentException>(() => new CaptureFrame(2, 2, new byte[8]));
    }

    [Fact]
    public void ToArray_ShouldReturnIndependentCopy()
    {
        var source = new byte[4];
        var frame = new CaptureFrame(1, 1, source);

        byte[] first = frame.ToArray();
        byte[] second = frame.ToArray();

        Assert.NotSame(first, second);
        Assert.NotSame(first, source);
        Assert.Equal(source, first);
    }

    [Fact]
    public void Pixels_ShouldExposeReadOnlyView()
    {
        var frame = new CaptureFrame(1, 1, new byte[4]);

        IReadOnlyList<byte> pixels = frame.Pixels;

        Assert.Equal(4, pixels.Count);
        Assert.Throws<NotSupportedException>(() => ((IList<byte>)pixels)[0] = 1);
    }

    [Fact]
    public void EmptyBounds_ShouldReportIsEmpty()
    {
        Assert.True(CaptureBounds.Empty.IsEmpty);
        Assert.True(new CaptureBounds(10, 10, 0, 100).IsEmpty);
        Assert.False(new CaptureBounds(0, 0, 1, 1).IsEmpty);
    }

    [Fact]
    public void Bounds_ShouldComputeRightAndBottom()
    {
        var bounds = new CaptureBounds(10, 20, 100, 200);

        Assert.Equal(110, bounds.Right);
        Assert.Equal(220, bounds.Bottom);
    }

    [Fact]
    public void Bounds_ShouldSupportNegativeOriginForSecondaryDisplays()
    {
        // 副屏位于主屏左侧时坐标为负
        var bounds = new CaptureBounds(-1920, 0, 1920, 1080);

        Assert.Equal(-1920, bounds.X);
        Assert.Equal(0, bounds.Right);
        Assert.False(bounds.IsEmpty);
    }
}