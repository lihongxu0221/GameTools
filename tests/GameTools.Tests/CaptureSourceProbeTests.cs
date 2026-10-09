using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using GameTools.Core.Abstractions;
using GameTools.Core.Enums;
using GameTools.Core.Models;
using GameTools.Infrastructure.Capture;
using GameTools.Infrastructure.Matching;
using GameTools.Win32.Helpers;
using Xunit;

namespace GameTools.Tests;

/// <summary>
/// 捕获源运行时探测与点位解析的回归测试。
/// </summary>
/// <remarks>
/// 全部在进程内自绘窗口上验证，不依赖任何外部应用。
/// </remarks>
[Collection(UiAutomationCollection.Name)]
public class CaptureSourceProbeTests
{
    /// <summary>
    /// 正常窗口必须探测出可用的捕获源，并给出至少一条尝试记录。
    /// </summary>
    [Fact]
    public void Probe_VisibleWindow_ShouldFindUsableSource()
    {
        StaWait.RunSta(() =>
        {
            var capture = new GdiScreenCapture();

            using var form = CreateForm();
            form.Show();
            form.BringToFront();
            Pump();

            CaptureSourceReport report = capture.Probe(form.Handle);

            Assert.True(report.Usable, report.Message);
            Assert.NotEqual(CaptureSourceKind.Unknown, report.Source);
            Assert.NotEmpty(report.Attempts);
            Assert.All(report.Attempts, attempt => Assert.False(string.IsNullOrWhiteSpace(attempt.Message)));
        });
    }

    /// <summary>
    /// 探测必须给出实际可用的那一条，并把它排在首位。
    /// </summary>
    [Fact]
    public void Probe_UsableSource_ShouldAppearFirstWithUsableFlag()
    {
        StaWait.RunSta(() =>
        {
            var capture = new GdiScreenCapture();

            using var form = CreateForm();
            form.Show();
            form.BringToFront();
            Pump();

            CaptureSourceReport report = capture.Probe(form.Handle);

            Assert.True(report.Usable);
            Assert.Equal(report.Source, report.Attempts[0].Source);
            Assert.True(report.Attempts[0].Usable);
            Assert.All(report.Attempts.Skip(1), attempt => Assert.False(attempt.Usable));
        });
    }

    /// <summary>
    /// 最小化窗口没有任何可绘制内容，全部捕获源都应失败并给出原因。
    /// </summary>
    [Fact]
    public void Probe_MinimizedWindow_ShouldReportEverySourceFailed()
    {
        StaWait.RunSta(() =>
        {
            var capture = new GdiScreenCapture();

            using var form = CreateForm();
            form.Show();
            form.BringToFront();
            Pump();

            form.WindowState = FormWindowState.Minimized;
            Pump();

            CaptureSourceReport report = capture.Probe(form.Handle);

            Assert.False(report.Usable);
            Assert.NotEmpty(report.Attempts);
            Assert.All(report.Attempts, attempt => Assert.False(attempt.Usable));
            Assert.All(report.Attempts, attempt => Assert.False(string.IsNullOrWhiteSpace(attempt.Message)));
        });
    }

    /// <summary>
    /// 无效句柄必须被拒绝，不得抛出或崩溃。
    /// </summary>
    [Fact]
    public void Probe_InvalidHandle_ShouldFailWithReason()
    {
        var capture = new GdiScreenCapture();

        CaptureSourceReport report = capture.Probe(new IntPtr(0xFFFF));

        Assert.False(report.Usable);
        Assert.Contains("句柄", report.Message);
    }

    /// <summary>
    /// 未实现的捕获源必须明确拒绝。
    /// </summary>
    [Fact]
    public void Capture_UnknownSource_ShouldFailWithReason()
    {
        StaWait.RunSta(() =>
        {
            var capture = new GdiScreenCapture();

            using var form = CreateForm();
            form.Show();
            form.BringToFront();
            Pump();

            CaptureResult result = capture.Capture(form.Handle, (CaptureSourceKind)99);

            Assert.False(result.Success);
            Assert.Null(result.Frame);
            Assert.Contains("未知的捕获源", result.ErrorMessage);
        });
    }

    /// <summary>
    /// 指定来源的捕获必须返回与窗口尺寸一致的帧。
    /// </summary>
    [Fact]
    public void Capture_ExplicitUsableSource_ShouldReturnFrameWithExpectedSize()
    {
        StaWait.RunSta(() =>
        {
            var capture = new GdiScreenCapture();

            using var form = CreateForm();
            form.Show();
            form.BringToFront();
            Pump();

            CaptureSourceReport report = capture.Probe(form.Handle);
            Assert.True(report.Usable, report.Message);

            CaptureResult result = capture.Capture(form.Handle, report.Source);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotNull(result.Frame);

            // 捕获帧按窗口外框分配尺寸：PrintWindow 按完整外框绘制，
            // 用 DWM 扩展边界比对会因不可见缩放边框产生每侧约 7 像素的差异。
            CaptureBounds outerFrame = WindowHelper.GetOuterFrameBounds(form.Handle);
            Assert.Equal(outerFrame.Width, result.Frame!.Width);
            Assert.Equal(outerFrame.Height, result.Frame.Height);
        });
    }

    /// <summary>
    /// 识图命中区域中心点必须解析为该点下最深层的子窗口句柄。
    /// </summary>
    [Fact]
    public void Resolve_HitOverButton_ShouldReturnButtonHandle()
    {
        StaWait.RunSta(() =>
        {
            using var form = CreateForm();

            var button = new Button
            {
                Text = "确定",
                Location = new System.Drawing.Point(50, 40),
                Size = new System.Drawing.Size(150, 46)
            };
            form.Controls.Add(button);
            form.Show();
            form.BringToFront();
            Pump();

            CaptureBounds outerFrame = WindowHelper.GetOuterFrameBounds(form.Handle);
            CaptureBounds buttonScreen = WindowHelper.GetWindowBounds(button.Handle);

            // 把按钮屏幕位置换算回捕获帧坐标：帧原点即窗口外框左上角
            var frameBounds = new CaptureBounds(
                buttonScreen.X - outerFrame.X,
                buttonScreen.Y - outerFrame.Y,
                buttonScreen.Width,
                buttonScreen.Height);

            ResolvedTarget resolved = TemplatePointResolver.Resolve(
                form.Handle, frameBounds, CaptureSourceKind.PrintWindowRenderFullContent);

            Assert.True(resolved.Success, resolved.Message);
            Assert.Equal(button.Handle, resolved.WindowHandle);

            // 屏幕点应为按钮中心，精确断言可捕捉坐标系原点用错造成的系统性偏移
            Assert.Equal(buttonScreen.X + (buttonScreen.Width / 2), resolved.ScreenX);
            Assert.Equal(buttonScreen.Y + (buttonScreen.Height / 2), resolved.ScreenY);
        });
    }

    /// <summary>
    /// 空白区域没有子窗口时应退回根窗口，而不是返回无效句柄。
    /// </summary>
    [Fact]
    public void Resolve_HitOverBlankArea_ShouldFallBackToRootWindow()
    {
        StaWait.RunSta(() =>
        {
            using var form = CreateForm();
            form.Show();
            form.BringToFront();
            Pump();

            CaptureBounds outerFrame = WindowHelper.GetOuterFrameBounds(form.Handle);
            var blank = new CaptureBounds(10, 10, 20, 10);

            ResolvedTarget resolved = TemplatePointResolver.Resolve(
                form.Handle, blank, CaptureSourceKind.PrintWindowRenderFullContent);

            Assert.True(resolved.Success, resolved.Message);
            Assert.Equal(form.Handle, resolved.WindowHandle);
            Assert.Equal(outerFrame.X + 20, resolved.ScreenX);
            Assert.Equal(outerFrame.Y + 15, resolved.ScreenY);
        });
    }

    /// <summary>
    /// 识别图结果不得使用自绘控件的句柄，必须退回根窗口并说明原因。
    /// </summary>
    /// <remarks>
    /// 这不是缺陷而是事实：WPF、Chromium、WinUI 的控件本就没有独立 HWND，
    /// 识图只能定位到坐标。提示必须如实告知，否则调用方会误以为解析失败。
    /// </remarks>
    [Fact]
    public void Resolve_NoChildAtPoint_ShouldExplainCoordinateClickFallback()
    {
        StaWait.RunSta(() =>
        {
            using var form = CreateForm();
            form.Show();
            form.BringToFront();
            Pump();

            ResolvedTarget resolved = TemplatePointResolver.Resolve(
                form.Handle, new CaptureBounds(5, 5, 16, 16), CaptureSourceKind.ScreenBitBlt);

            Assert.True(resolved.Success);
            Assert.Contains("坐标消息", resolved.Message);
            Assert.Contains("屏幕区域 BitBlt", resolved.Message);
        });
    }

    /// <summary>
    /// 无效句柄与空命中区域都必须被拒绝。
    /// </summary>
    [Fact]
    public void Resolve_InvalidInput_ShouldFailWithReason()
    {
        Assert.False(TemplatePointResolver
            .Resolve(new IntPtr(0xFFFF), new CaptureBounds(0, 0, 10, 10), CaptureSourceKind.Unknown)
            .Success);

        StaWait.RunSta(() =>
        {
            using var form = CreateForm();
            form.Show();
            form.BringToFront();
            Pump();

            Assert.False(TemplatePointResolver
                .Resolve(form.Handle, CaptureBounds.Empty, CaptureSourceKind.Unknown)
                .Success);
        });
    }

    private static Form CreateForm() =>
        new() { Text = "GameTools 捕获源探测测试", ClientSize = new System.Drawing.Size(420, 260) };

    /// <summary>
    /// 让窗口完成布局与绘制。
    /// </summary>
    private static void Pump()
    {
        for (int i = 0; i < 3; i++)
        {
            Application.DoEvents();
            Thread.Sleep(120);
        }

        Application.DoEvents();
    }
}
