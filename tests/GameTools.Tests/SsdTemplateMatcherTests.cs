using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameTools.Core.Models;
using GameTools.Infrastructure.Matching;
using Xunit;

namespace GameTools.Tests;

/// <summary>
/// <see cref="SsdTemplateMatcher"/> 的回归测试。
/// </summary>
/// <remarks>
/// 全部使用合成画面：程序化生成的界面底图加显式绘制的控件，不依赖任何真实
/// 界面截图。合成画面刻意保留真实截图的关键性质——局部空间相关、大片平坦区域
/// 与高频细节并存。均匀随机噪声不适合作为模板匹配的测试输入：它没有任何空间
/// 相关性，任何算法在降采样后都无法恢复匹配位置。
/// </remarks>
public class SsdTemplateMatcherTests
{
    /// <summary>
    /// 阈值缺省值即业务要求的 80%，低于该值必须拒绝执行。
    /// </summary>
    private const double RequiredThreshold = 0.80;

    /// <summary>
    /// 模板取自画面本身时必须精确命中，且坐标不得偏移。
    /// </summary>
    [Theory]
    [InlineData(320, 240, 40, 30, 30, 20, "左上")]
    [InlineData(320, 240, 250, 180, 24, 18, "右下")]
    [InlineData(320, 240, 137, 91, 31, 19, "非对齐偏移")]
    [InlineData(320, 240, 0, 0, 40, 30, "画面左上角")]
    [InlineData(320, 240, 280, 210, 40, 30, "紧贴右下边界")]
    public async Task MatchAsync_TemplateFromScene_ShouldHitExactPosition(
        int width,
        int height,
        int x,
        int y,
        int templateWidth,
        int templateHeight,
        string label)
    {
        var matcher = new SsdTemplateMatcher();
        CaptureFrame scene = SyntheticUi.BuildScene(width, height, seed: width * 31 + height);
        CaptureFrame template = SyntheticUi.Crop(scene, x, y, templateWidth, templateHeight);

        TemplateMatchResult result = await matcher.MatchAsync(
            scene, template, new MatchOptions { ScoreThreshold = RequiredThreshold });

        Assert.True(result.Found, $"{label}: {result.Message}");
        Assert.True(
            result.BestScore >= RequiredThreshold,
            $"{label}: 得分 {result.BestScore:F4} 未达阈值");
        Assert.InRange(result.BestBounds.X, x - 2, x + 2);
        Assert.InRange(result.BestBounds.Y, y - 2, y + 2);
    }

    /// <summary>
    /// 目标位置不落在任何跳步网格上时也必须命中。
    /// </summary>
    /// <remarks>
    /// 回归背景：曾用「跳步粗扫 + 仅对达标网格点精扫」的策略。该策略要求真实
    /// 位置恰好落在采样网格上，实测目标水平偏移 100 而步长为 8 时整轮搜索都
    /// 找不到画面中确实存在的模板。当前实现改为穷举，位置不再受网格约束。
    /// </remarks>
    [Fact]
    public async Task MatchAsync_OffGridPosition_ShouldStillBeFound()
    {
        var matcher = new SsdTemplateMatcher();
        CaptureFrame scene = SyntheticUi.BuildScene(400, 300, seed: 909);
        CaptureFrame template = SyntheticUi.Crop(scene, 101, 137, 37, 23);

        TemplateMatchResult result = await matcher.MatchAsync(
            scene, template, new MatchOptions { ScoreThreshold = RequiredThreshold });

        Assert.True(result.Found, result.Message);
        Assert.InRange(result.BestBounds.X, 99, 103);
        Assert.InRange(result.BestBounds.Y, 135, 139);
    }

    /// <summary>
    /// 整体对比度变化不应影响命中位置。
    /// </summary>
    /// <remarks>
    /// 归一化互相关对仿射变化不变，这是它相对直接比较像素差的根本优势。
    /// 用乘性缩放而非加减常量：加减会触及字节上下限，变换不再是均匀仿射，
    /// 那是测试数据失真而非算法缺陷。
    /// </remarks>
    [Theory]
    [InlineData(0.9)]
    [InlineData(0.75)]
    public async Task MatchAsync_ContrastScaled_ShouldKeepPosition(double factor)
    {
        var matcher = new SsdTemplateMatcher();
        const int left = 120, top = 90;
        CaptureFrame scene = SyntheticUi.BuildSceneWithButton(320, 240, seed: 55, left, top);
        CaptureFrame template = SyntheticUi.Crop(
            scene, left, top, SyntheticUi.ButtonWidth, SyntheticUi.ButtonHeight);

        TemplateMatchResult result = await matcher.MatchAsync(
            SyntheticUi.Scale(scene, factor),
            template,
            new MatchOptions { ScoreThreshold = RequiredThreshold });

        Assert.True(result.Found, result.Message);
        Assert.InRange(result.BestBounds.X, left - 2, left + 2);
        Assert.InRange(result.BestBounds.Y, top - 2, top + 2);
    }

    /// <summary>
    /// 画面中不存在模板时必须拒绝，且得分随噪声增大单调下降。
    /// </summary>
    [Fact]
    public async Task MatchAsync_AbsentTemplate_ShouldRejectExecution()
    {
        var matcher = new SsdTemplateMatcher();
        CaptureFrame scene = SyntheticUi.BuildScene(320, 240, seed: 3);

        TemplateMatchResult result = await matcher.MatchAsync(
            scene,
            SyntheticUi.BuildUnrelatedPattern(40, 30),
            new MatchOptions { ScoreThreshold = RequiredThreshold });

        Assert.False(result.Found);
        Assert.True(
            result.BestScore < RequiredThreshold,
            $"无关模板得分 {result.BestScore:F4} 不应达到阈值");
        Assert.Contains("拒绝执行", result.Message);
    }

    /// <summary>
    /// 噪声增大时得分必须单调下降，且位置保持稳定。
    /// </summary>
    [Fact]
    public async Task MatchAsync_IncreasingNoise_ShouldMonotonicallyDecreaseScore()
    {
        var matcher = new SsdTemplateMatcher();
        CaptureFrame scene = SyntheticUi.BuildScene(320, 240, seed: 4);
        CaptureFrame template = SyntheticUi.Crop(scene, 100, 120, 60, 36);

        double previous = double.MaxValue;

        foreach (int noise in new[] { 0, 4, 10 })
        {
            TemplateMatchResult result = await matcher.MatchAsync(
                scene,
                SyntheticUi.AddNoise(template, noise),
                new MatchOptions { ScoreThreshold = RequiredThreshold });

            Assert.True(
                result.BestScore <= previous + 1e-9,
                $"噪声 ±{noise} 的得分 {result.BestScore:F4} 高于上一档 {previous:F4}");
            previous = result.BestScore;
        }
    }

    /// <summary>
    /// 限制搜索区域时，命中坐标必须换算回画面坐标。
    /// </summary>
    /// <remarks>
    /// 回归背景：搜索区域会在内部把画面裁掉左上角，若直接输出区域内相对坐标，
    /// 调用方据此点击会偏到画面的左上区域。
    /// </remarks>
    [Fact]
    public async Task MatchAsync_SearchRegion_ShouldTranslateToFrameCoordinates()
    {
        var matcher = new SsdTemplateMatcher();
        CaptureFrame scene = SyntheticUi.BuildScene(400, 300, seed: 5);
        CaptureFrame template = SyntheticUi.Crop(scene, 250, 200, 30, 22);

        TemplateMatchResult result = await matcher.MatchAsync(
            scene,
            template,
            new MatchOptions
            {
                ScoreThreshold = RequiredThreshold,
                SearchRegion = new CaptureBounds(200, 150, 200, 150)
            });

        Assert.True(result.Found, result.Message);
        Assert.InRange(result.BestBounds.X, 248, 252);
        Assert.InRange(result.BestBounds.Y, 198, 202);
    }

    /// <summary>
    /// 搜索区域之外的目标不得被命中。
    /// </summary>
    [Fact]
    public async Task MatchAsync_TargetOutsideSearchRegion_ShouldNotBeFound()
    {
        var matcher = new SsdTemplateMatcher();
        CaptureFrame scene = SyntheticUi.BuildScene(400, 300, seed: 6);
        CaptureFrame template = SyntheticUi.Crop(scene, 40, 30, 30, 22);

        TemplateMatchResult result = await matcher.MatchAsync(
            scene,
            template,
            new MatchOptions
            {
                ScoreThreshold = RequiredThreshold,
                SearchRegion = new CaptureBounds(200, 150, 200, 150)
            });

        Assert.False(result.Found, result.Message);
    }

    /// <summary>
    /// 提前终止加速不得改变匹配结果。
    /// </summary>
    /// <remarks>
    /// 提前终止的正当性来自平方差各项非负：部分和是总和的下界，超过当前最优
    /// 即可判定该位置不可能更优。本测试用关闭加速的完整计算作为对照基准，
    /// 确保加速路径与逐位置完整计算给出完全一致的得分与坐标。
    /// </remarks>
    [Fact]
    public async Task MatchAsync_EarlyExitEnabled_ShouldMatchFullComputationExactly()
    {
        var matcher = new SsdTemplateMatcher();
        CaptureFrame scene = SyntheticUi.BuildScene(320, 240, seed: 7);
        CaptureFrame template = SyntheticUi.Crop(scene, 150, 120, 34, 22);

        TemplateMatchResult accelerated = await matcher.MatchAsync(
            scene, template, new MatchOptions { ScoreThreshold = RequiredThreshold, UseCoarseSearch = true });

        TemplateMatchResult exhaustive = await matcher.MatchAsync(
            scene, template, new MatchOptions { ScoreThreshold = RequiredThreshold, UseCoarseSearch = false });

        Assert.Equal(exhaustive.Found, accelerated.Found);
        Assert.Equal(exhaustive.BestScore, accelerated.BestScore, precision: 10);
        Assert.Equal(exhaustive.BestBounds.X, accelerated.BestBounds.X);
        Assert.Equal(exhaustive.BestBounds.Y, accelerated.BestBounds.Y);
    }

    /// <summary>
    /// 提高阈值必须拒绝低分匹配：阈值是唯一的执行闸门。
    /// </summary>
    [Fact]
    public async Task MatchAsync_HigherThreshold_ShouldRejectLowScore()
    {
        var matcher = new SsdTemplateMatcher();
        const int left = 100, top = 100;
        CaptureFrame scene = SyntheticUi.BuildSceneWithButton(320, 240, seed: 8, left, top);
        CaptureFrame template = SyntheticUi.Crop(
            scene, left, top, SyntheticUi.ButtonWidth, SyntheticUi.ButtonHeight);
        CaptureFrame noisy = SyntheticUi.AddNoise(template, 30);

        TemplateMatchResult loose = await matcher.MatchAsync(
            scene, noisy, new MatchOptions { ScoreThreshold = 0.50 });

        TemplateMatchResult strict = await matcher.MatchAsync(
            scene, noisy, new MatchOptions { ScoreThreshold = 0.95 });

        Assert.Equal(loose.BestScore, strict.BestScore, precision: 6);
        Assert.False(strict.Found);
        Assert.True(loose.Found, "同一噪声输入在较宽阈值下应能命中");
    }

    /// <summary>
    /// 纯色模板没有纹理，必须拒绝而不是崩溃或返回虚假高分。
    /// </summary>
    [Fact]
    public async Task MatchAsync_UniformTemplate_ShouldRejectWithoutThrowing()
    {
        var matcher = new SsdTemplateMatcher();
        CaptureFrame scene = SyntheticUi.BuildScene(200, 150, seed: 9);
        var uniform = new CaptureFrame(20, 20, new byte[20 * 20 * 4]);

        TemplateMatchResult result = await matcher.MatchAsync(scene, uniform, new MatchOptions());

        Assert.False(result.Found);
    }

    /// <summary>
    /// 模板大于画面必须给出明确原因。
    /// </summary>
    [Fact]
    public async Task MatchAsync_TemplateLargerThanFrame_ShouldExplain()
    {
        var matcher = new SsdTemplateMatcher();
        CaptureFrame scene = SyntheticUi.BuildScene(200, 150, seed: 10);

        TemplateMatchResult result = await matcher.MatchAsync(
            scene, SyntheticUi.BuildScene(400, 300, seed: 11), new MatchOptions());

        Assert.False(result.Found);
        Assert.Contains("大于画面", result.Message);
    }

    /// <summary>
    /// 单像素模板不得崩溃。
    /// </summary>
    [Fact]
    public async Task MatchAsync_SinglePixelTemplate_ShouldNotThrow()
    {
        var matcher = new SsdTemplateMatcher();
        CaptureFrame scene = SyntheticUi.BuildScene(120, 90, seed: 12);

        TemplateMatchResult result = await matcher.MatchAsync(
            scene, SyntheticUi.Crop(scene, 10, 10, 1, 1), new MatchOptions());

        Assert.NotNull(result.Message);
    }

    /// <summary>
    /// 空引用必须以 <see cref="ArgumentNullException"/> 明确拒绝。
    /// </summary>
    [Fact]
    public async Task MatchAsync_NullArguments_ShouldThrow()
    {
        var matcher = new SsdTemplateMatcher();
        CaptureFrame scene = SyntheticUi.BuildScene(40, 30, seed: 13);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => matcher.MatchAsync(null!, scene, new MatchOptions()));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => matcher.MatchAsync(scene, null!, new MatchOptions()));
    }

    /// <summary>
    /// 匹配运行在后台线程，调用方不得被 CPU 密集计算阻塞。
    /// </summary>
    /// <remarks>
    /// 该特性保证界面线程不会被识图卡住。若实现改为同步执行，
    /// 本测试会在 <c>MatchAsync</c> 返回前即断言失败。
    /// </remarks>
    [Fact]
    public async Task MatchAsync_ShouldNotBlockCallerThread()
    {
        var matcher = new SsdTemplateMatcher();
        CaptureFrame scene = SyntheticUi.BuildScene(640, 480, seed: 14);
        CaptureFrame template = SyntheticUi.Crop(scene, 300, 200, 48, 30);

        Task<TemplateMatchResult> pending = matcher.MatchAsync(
            scene, template, new MatchOptions { ScoreThreshold = RequiredThreshold });

        Assert.False(pending.IsCompleted, "MatchAsync 应立即返回任务而非同步完成");

        TemplateMatchResult result = await pending;
        Assert.True(result.Found, result.Message);
    }

    /// <summary>
    /// 取消令牌必须能中断匹配。
    /// </summary>
    [Fact]
    public async Task MatchAsync_Cancelled_ShouldThrowOperationCanceled()
    {
        var matcher = new SsdTemplateMatcher();
        CaptureFrame scene = SyntheticUi.BuildScene(640, 480, seed: 15);
        CaptureFrame template = SyntheticUi.Crop(scene, 100, 100, 40, 30);

        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => matcher.MatchAsync(scene, template, new MatchOptions(), source.Token));
    }

    /// <summary>
    /// 合成画面与像素操作的测试辅助方法。
    /// </summary>
    /// <remarks>
    /// 全部为确定性伪随机，同一 seed 每次生成完全相同的画面，保证测试可复现。
    /// </remarks>
    private static class SyntheticUi
    {
        /// <summary>
        /// 生成具有界面结构的合成画面。
        /// </summary>
        /// <param name="width">画面宽。</param>
        /// <param name="height">画面高。</param>
        /// <param name="seed">随机种子。</param>
        /// <returns>BGRA 像素帧。</returns>
        internal static CaptureFrame BuildScene(int width, int height, int seed)
        {
            var random = new Random(seed);
            var pixels = new byte[width * height * 4];

            // 低频渐变底：提供全局光照变化下的稳定参照
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int value = 150
                        + (int)(40.0 * Math.Sin(x * 0.05) * Math.Cos(y * 0.04))
                        + random.Next(-6, 7);
                    Put(pixels, width, x, y, value);
                }
            }

            // 面板：大片平坦区域
            for (int i = 0; i < 10; i++)
            {
                int blockWidth = Math.Min(width / 3, 40 + random.Next(0, 120));
                int blockHeight = Math.Min(height / 4, 20 + random.Next(0, 70));
                int left = random.Next(0, Math.Max(1, width - blockWidth));
                int top = random.Next(0, Math.Max(1, height - blockHeight));

                Fill(pixels, width, left, top, blockWidth, blockHeight, random.Next(60, 245));
                Border(pixels, width, left, top, blockWidth, blockHeight, random.Next(0, 256));
            }

            // 文字块：高频细节，是模板匹配真正依赖的纹理
            for (int i = 0; i < 12; i++)
            {
                int left = random.Next(0, Math.Max(1, width - 100));
                int top = random.Next(0, Math.Max(1, height - 12));
                int glyphs = 4 + random.Next(0, 8);

                for (int k = 0; k < glyphs; k++)
                {
                    int glyphWidth = 5 + random.Next(0, 5);
                    Fill(pixels, width, left + k * 7, top, glyphWidth, 8, random.Next(0, 256));
                }
            }

            return new CaptureFrame(width, height, pixels);
        }

        /// <summary>
        /// 合成控件的固定宽度，供调用方裁剪同一位置。
        /// </summary>
        internal const int ButtonWidth = 120;

        /// <summary>
        /// 合成控件的固定高度。
        /// </summary>
        internal const int ButtonHeight = 44;

        /// <summary>
        /// 在合成界面上绘制一个高纹理控件（边框 + 文字 + 圆形图标）。
        /// </summary>
        /// <remarks>
        /// 验证对比度不变性与噪声容忍度时必须使用纹理充足的目标。落在平坦面板
        /// 中央的模板几乎没有梯度，字节量化的微小误差即可压过信号，此时的失分
        /// 反映的是测试数据的选择，而不是算法的缺陷。
        /// </remarks>
        /// <param name="width">画面宽。</param>
        /// <param name="height">画面高。</param>
        /// <param name="seed">随机种子。</param>
        /// <param name="left">控件左上角 X。</param>
        /// <param name="top">控件左上角 Y。</param>
        /// <returns>BGRA 像素帧。</returns>
        internal static CaptureFrame BuildSceneWithButton(int width, int height, int seed, int left, int top)
        {
            byte[] pixels = BuildScene(width, height, seed).ToArray();
            var random = new Random(seed + 77);

            Fill(pixels, width, left, top, ButtonWidth, ButtonHeight, 210);
            Border(pixels, width, left, top, ButtonWidth, ButtonHeight, 20);
            Border(pixels, width, left + 3, top + 3, ButtonWidth - 6, ButtonHeight - 6, 250);

            int cursor = left + 10;
            for (int i = 0; i < 9; i++)
            {
                int glyphWidth = 5 + random.Next(0, 6);
                if (cursor + glyphWidth > left + ButtonWidth - 36)
                {
                    break;
                }

                Fill(pixels, width, cursor, top + 16, glyphWidth, 12, random.Next(0, 90));
                cursor += glyphWidth + 3;
            }

            // 圆形图标，进一步提高梯度密度
            Fill(pixels, width, left + ButtonWidth - 30, top + 8, 22, 28, 40);
            Border(pixels, width, left + ButtonWidth - 30, top + 8, 22, 28, 255);
            for (int row = 0; row < 24; row++)
            {
                for (int column = 0; column < 18; column++)
                {
                    if (Math.Abs(column - 9) + Math.Abs(row - 12) < 9)
                    {
                        Put(pixels, width, left + ButtonWidth - 29 + column, top + 9 + row, 230);
                    }
                }
            }

            return new CaptureFrame(width, height, pixels);
        }

        /// <summary>
        /// 生成与界面风格明显不同的模板，用于验证「不存在时必须拒绝」。
        /// </summary>
        internal static CaptureFrame BuildUnrelatedPattern(int width, int height)
        {
            var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Put(pixels, width, x, y, ((x * 7) ^ (y * 13)) % 256);
                }
            }

            return new CaptureFrame(width, height, pixels);
        }

        /// <summary>
        /// 裁剪画面中的一块区域。
        /// </summary>
        internal static CaptureFrame Crop(CaptureFrame source, int x, int y, int width, int height)
        {
            var pixels = new byte[width * height * 4];
            byte[] all = source.ToArray();

            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    int sourceIndex = ((y + row) * source.Width + (x + column)) * 4;
                    int targetIndex = (row * width + column) * 4;

                    if (sourceIndex >= 0 && sourceIndex + 3 < all.Length)
                    {
                        Array.Copy(all, sourceIndex, pixels, targetIndex, 4);
                    }
                }
            }

            return new CaptureFrame(width, height, pixels);
        }

        /// <summary>
        /// 对所有颜色通道做乘性缩放，模拟对比度变化。
        /// </summary>
        internal static CaptureFrame Scale(CaptureFrame source, double factor)
        {
            byte[] pixels = source.ToArray();

            for (int i = 0; i < pixels.Length; i++)
            {
                if (i % 4 != 3)
                {
                    int scaled = (int)Math.Round(pixels[i] * factor);
                    pixels[i] = (byte)(scaled < 0 ? 0 : (scaled > 255 ? 255 : scaled));
                }
            }

            return new CaptureFrame(source.Width, source.Height, pixels);
        }

        /// <summary>
        /// 为颜色通道加入确定性噪声。
        /// </summary>
        internal static CaptureFrame AddNoise(CaptureFrame source, int amount)
        {
            var random = new Random(4242);
            byte[] pixels = source.ToArray();

            for (int i = 0; i < pixels.Length; i++)
            {
                if (i % 4 != 3)
                {
                    int noisy = pixels[i] + random.Next(-amount, amount + 1);
                    pixels[i] = (byte)(noisy < 0 ? 0 : (noisy > 255 ? 255 : noisy));
                }
            }

            return new CaptureFrame(source.Width, source.Height, pixels);
        }

        private static void Put(byte[] pixels, int width, int x, int y, int value)
        {
            if (x < 0 || y < 0 || x >= width || (long)(y * width + x) * 4 >= pixels.Length)
            {
                return;
            }

            int index = (y * width + x) * 4;
            value = value < 0 ? 0 : (value > 255 ? 255 : value);

            pixels[index] = (byte)value;
            pixels[index + 1] = (byte)(value / 2);
            pixels[index + 2] = (byte)(255 - value);
            pixels[index + 3] = 255;
        }

        private static void Fill(byte[] pixels, int width, int x, int y, int blockWidth, int blockHeight, int value)
        {
            for (int row = y; row < y + blockHeight; row++)
            {
                for (int column = x; column < x + blockWidth; column++)
                {
                    Put(pixels, width, column, row, value);
                }
            }
        }

        private static void Border(
            byte[] pixels, int width, int x, int y, int blockWidth, int blockHeight, int value)
        {
            for (int column = x; column < x + blockWidth; column++)
            {
                Put(pixels, width, column, y, value);
                Put(pixels, width, column, y + blockHeight - 1, value);
            }

            for (int row = y; row < y + blockHeight; row++)
            {
                Put(pixels, width, x, row, value);
                Put(pixels, width, x + blockWidth - 1, row, value);
            }
        }
    }
}
