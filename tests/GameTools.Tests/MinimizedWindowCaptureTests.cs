using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using GameTools.Core.Abstractions;
using GameTools.Core.Models;
using GameTools.Infrastructure.Capture;
using GameTools.Win32.Native;
using Xunit;

namespace GameTools.Tests;

/// <summary>
/// 窗口截图对最小化目标与空白画面的处理回归测试。
/// </summary>
/// <remarks>
/// 回归背景：目标窗口最小化时 <c>GetWindowRect</c> 返回 <c>(-32000,-32000)</c> 处的
/// 160x28 图标矩形，<c>PrintWindow</c> 会返回 true 却只写入极少的像素，
/// 而 <c>CreateCompatibleBitmap</c> 创建的位图内容未初始化（实测为 32,32,32），
/// 高于原「接近纯黑」阈值 8 的判定，于是把一张无效图片当作成功结果保存。
/// </remarks>
[Collection(nameof(WindowCaptureCollection))]
public class MinimizedWindowCaptureTests
{
    /// <summary>
    /// 最小化窗口必须被直接拒绝，并给出可操作的提示，而不是产出无效图片。
    /// </summary>
    [Fact]
    public void CaptureWindow_MinimizedWindow_ShouldFailWithActionableMessage()
    {
        RunSta(() =>
        {
            using var form = new Form
            {
                Text = "GameTools 最小化截图回归测试",
                ClientSize = new Size(420, 260)
            };

            form.Show();
            form.WindowState = FormWindowState.Minimized;
            Application.DoEvents();

            Assert.True(User32.IsIconic(form.Handle), "测试前置条件失败：窗口未进入最小化状态。");

            GdiScreenCapture capture = CreateCapture();
            using CaptureResult result = capture.CaptureWindow(form.Handle);

            Assert.False(result.Success, "最小化窗口不应返回成功。");
            Assert.Null(result.Frame);
            Assert.Contains("最小化", result.ErrorMessage);
            Assert.Contains("还原", result.ErrorMessage);
        });
    }

    /// <summary>
    /// 最小化窗口被拒绝后，还原窗口应立即可正常截图。
    /// </summary>
    /// <remarks>
    /// 失败提示引导用户「先还原窗口」，本测试锁定该恢复路径确实成立，
    /// 避免拦截生效却让用户陷入无法截图的状态。
    /// </remarks>
    [Fact]
    public void CaptureWindow_MinimizedThenRestored_ShouldSucceed()
    {
        RunSta(() =>
        {
            using var form = new Form
            {
                Text = "GameTools 还原后截图回归测试",
                ClientSize = new Size(520, 340),
                BackColor = Color.White
            };

            form.Controls.Add(new Panel
            {
                BackColor = Color.SeaGreen,
                Location = new Point(24, 24),
                Size = new Size(200, 140)
            });

            form.Show();
            form.WindowState = FormWindowState.Minimized;
            Application.DoEvents();

            Assert.True(User32.IsIconic(form.Handle), "测试前置条件失败：窗口未进入最小化状态。");

            GdiScreenCapture capture = CreateCapture();

            using (CaptureResult rejected = capture.CaptureWindow(form.Handle))
            {
                Assert.False(rejected.Success, "最小化状态下应被拒绝。");
            }

            form.WindowState = FormWindowState.Normal;
            form.BringToFront();
            Application.DoEvents();
            Thread.Sleep(150);
            Application.DoEvents();

            Assert.False(User32.IsIconic(form.Handle), "窗口未能从最小化状态还原。");

            using CaptureResult accepted = capture.CaptureWindow(form.Handle);

            Assert.True(accepted.Success, accepted.ErrorMessage ?? "还原后截图应成功。");
            Assert.NotNull(accepted.Frame);

            CaptureFrameValues frame = CaptureFrameValues.From(accepted.Frame!);
            Assert.True(
                frame.DistinctColorCount > 1,
                $"还原后截图仍为单色（{frame.DistinctColorCount} 种颜色），说明位图未真正绘制。");
        });
    }

    /// <summary>
    /// 正常窗口截图必须成功，且内容不是单色。
    /// </summary>
    /// <remarks>
    /// 单色画面说明位图未被真正绘制，属于必须拦截的无效结果。
    /// 断言选用「颜色种类大于 1」而非具体数值，避免随系统主题变化而失效。
    /// </remarks>
    [Fact]
    public void CaptureWindow_NormalWindow_ShouldReturnNonUniformContent()
    {
        RunSta(() =>
        {
            using var form = new Form
            {
                Text = "GameTools 正常截图回归测试",
                ClientSize = new Size(500, 320),
                BackColor = Color.White
            };

            // 放置一个彩色控件，确保画面不是单色
            var panel = new Panel
            {
                BackColor = Color.Crimson,
                Location = new Point(20, 20),
                Size = new Size(180, 120)
            };
            form.Controls.Add(panel);

            form.Show();
            form.WindowState = FormWindowState.Normal;
            form.BringToFront();
            Application.DoEvents();

            GdiScreenCapture capture = CreateCapture();
            using CaptureResult result = capture.CaptureWindow(form.Handle);

            Assert.True(result.Success, result.ErrorMessage ?? "正常窗口截图应成功。");
            Assert.NotNull(result.Frame);

            CaptureFrameValues frame = CaptureFrameValues.From(result.Frame!);
            Assert.True(frame.Width > 100, $"截图宽度异常: {frame.Width}");
            Assert.True(frame.Height > 100, $"截图高度异常: {frame.Height}");
            Assert.True(
                frame.DistinctColorCount > 1,
                $"截图内容为单色（{frame.DistinctColorCount} 种颜色），说明位图未真正绘制。");
        });
    }

    /// <summary>
    /// 无效句柄仍应返回失败，且不得抛出异常。
    /// </summary>
    [Fact]
    public void CaptureWindow_InvalidHandle_ShouldFailWithoutThrowing()
    {
        GdiScreenCapture capture = CreateCapture();
        using CaptureResult result = capture.CaptureWindow(IntPtr.Zero);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    /// <summary>
    /// 创建使用真实系统版本探测器的截图实现。
    /// </summary>
    private static GdiScreenCapture CreateCapture() => new();

    /// <summary>
    /// 在独立 STA 线程上执行 WinForms 操作并等待完成。
    /// </summary>
    /// <remarks>
    /// WinForms 要求 STA，而 xUnit 默认在 MTA 上执行测试，因此必须自行开线程。
    /// 未处理的异常会回传并在此处重新抛出，避免测试被静默跳过。
    /// </remarks>
    /// <param name="action">需要执行的测试逻辑。</param>
    private static void RunSta(Action action)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
            Name = "MinWindowCaptureTests-STA"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA 测试线程执行超时。");

        if (failure != null)
        {
            throw new InvalidOperationException("STA 测试线程内发生异常。", failure);
        }
    }
}

/// <summary>
/// 使涉及真实窗口的测试串行执行，避免多个测试同时创建与操作窗口互相干扰。
/// </summary>
[CollectionDefinition(nameof(WindowCaptureCollection), DisableParallelization = true)]
public sealed class WindowCaptureCollection
{
}

/// <summary>
/// <see cref="CaptureFrame"/> 的像素统计结果。
/// </summary>
internal readonly struct CaptureFrameValues
{
    private CaptureFrameValues(int width, int height, int distinctColorCount)
    {
        Width = width;
        Height = height;
        DistinctColorCount = distinctColorCount;
    }

    /// <summary>
    /// 帧宽度。
    /// </summary>
    internal int Width { get; }

    /// <summary>
    /// 帧高度。
    /// </summary>
    internal int Height { get; }

    /// <summary>
    /// 采样得到的不同颜色数量。
    /// </summary>
    internal int DistinctColorCount { get; }

    /// <summary>
    /// 按固定步长采样帧像素并统计不同颜色数量。
    /// </summary>
    /// <param name="frame">待统计的帧。</param>
    /// <returns>统计结果。</returns>
    internal static CaptureFrameValues From(GameTools.Core.Models.CaptureFrame frame)
    {
        byte[] pixels = frame.ToArray();
        var colors = new System.Collections.Generic.HashSet<uint>();

        // BGRA 每像素 4 字节；跳过 alpha 分量，只比较颜色
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            byte b = pixels[i];
            byte g = pixels[i + 1];
            byte r = pixels[i + 2];
            colors.Add(((uint)r << 16) | ((uint)g << 8) | b);
        }

        return new CaptureFrameValues(frame.Width, frame.Height, colors.Count);
    }
}
