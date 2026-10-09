using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GameTools.Core.Abstractions;
using GameTools.Core.Enums;
using GameTools.Core.Models;
using GameTools.Win32.Helpers;
using GameTools.Win32.Native;
using GameTools.Win32.SafeHandles;

namespace GameTools.Infrastructure.Capture;

/// <summary>
/// 基于 GDI 与 Win32 的屏幕与窗口截图实现。
/// 全面兼容 Windows 7 SP1 至 Windows 11，具备版本自适应降级与资源防泄漏保护。
/// </summary>
/// <remarks>
/// 关键设计：
/// 1. 位图尺寸一律按 <see cref="WindowHelper.GetPrintWindowSize"/> 返回的完整外框分配。
///    DWM 扩展边界排除了不可见调整边框，而 <c>PrintWindow</c> 按完整外框绘制，
///    若用 DWM 边界分配位图会导致右侧与底部被裁切；
/// 2. <c>PrintWindow</c> 返回 true 但未真正绘制任何像素时按失败处理并降级。
///    判定同时依据「采样点亮度接近纯黑」与「内容与调用前完全一致」两条独立线索：
///    仅靠亮度阈值无法覆盖最小化窗口——<c>CreateCompatibleBitmap</c> 不保证清零，
///    最小化或受保护的窗口会让 <c>PrintWindow</c> 直接返回 true 而不绘制，
///    此时位图保留未初始化内容（实测为 32,32,32），亮度高于阈值会被误判为有效画面；
/// 3. 最小化窗口没有可绘制的客户区，直接按失败返回并给出可操作的提示，
///    避免产出尺寸退化为 160x28 图标矩形的无效图片；
/// 4. 空白判定采用抽样，兼顾准确性与性能；
/// 5. 所有 GDI 对象以安全句柄包裹，<c>SelectObject</c> 的原对象在 finally 中还原；
/// 6. 契约层以 <see cref="CaptureFrame"/> 承载 BGRA 像素，位图仅在本层临时构造并立即释放。
/// </remarks>
public sealed class GdiScreenCapture : IScreenCapture
{
    /// <summary>
    /// 空白判定阈值：采样点亮度低于该值即视为黑。
    /// 取 8 而非 0，用于容忍压缩与缩放引入的轻微噪声。
    /// </summary>
    private const int BlackThreshold = 8;

    /// <summary>
    /// 空白判定采样步长：每 step 个像素采样一次，兼顾准确性与性能。
    /// </summary>
    private const int BlackSampleStep = 4;

    private const uint PwClientOnly = 0x00000001u;
    private const uint PwDefault = 0x00000000u;
    private const uint PwRenderFullContent = 0x00000002u;

    private readonly IOsVersionProvider _osVersion;

    /// <summary>
    /// 使用真实系统版本探测器初始化。
    /// </summary>
    public GdiScreenCapture()
        : this(OSVersionHelper.Provider)
    {
    }

    /// <summary>
    /// 使用指定版本探测器初始化，供单元测试注入固定版本。
    /// </summary>
    /// <param name="osVersion">版本与能力探测器。</param>
    public GdiScreenCapture(IOsVersionProvider osVersion)
    {
        _osVersion = osVersion ?? throw new ArgumentNullException(nameof(osVersion));
    }

    /// <inheritdoc />
    public CaptureResult CaptureFullScreen(bool allMonitors = true)
    {
        var sw = Stopwatch.StartNew();

        CaptureBounds bounds = allMonitors
            ? ToCaptureBounds(SystemInformation.VirtualScreen)
            : ToCaptureBounds(Screen.PrimaryScreen?.Bounds);

        if (bounds.IsEmpty)
        {
            return Failure(bounds, CaptureMode.DesktopBitBlt, sw, "未检测到有效的屏幕显示区域。");
        }

        return CaptureRegionInternal(bounds, sw);
    }

    /// <inheritdoc />
    public CaptureResult CaptureRegion(CaptureBounds region)
    {
        var sw = Stopwatch.StartNew();

        if (region.IsEmpty)
        {
            return Failure(region, CaptureMode.DesktopBitBlt, sw, "指定的截图区域宽度或高度必须大于 0。");
        }

        return CaptureRegionInternal(region, sw);
    }

    /// <inheritdoc />
    public CaptureResult CaptureWindow(IntPtr hWnd, bool allowFallbackToDesktop = true)
    {
        var sw = Stopwatch.StartNew();

        if (hWnd == IntPtr.Zero)
        {
            return Failure(CaptureBounds.Empty, CaptureMode.PrintWindow, sw, "窗口句柄无效 (IntPtr.Zero)。");
        }

        if (!User32.IsWindow(hWnd))
        {
            return Failure(CaptureBounds.Empty, CaptureMode.PrintWindow, sw, "窗口句柄已失效。");
        }

        // 最小化窗口没有可绘制的客户区：GetWindowRect 返回 (-32000,-32000) 处的
        // 160x28 图标矩形，PrintWindow 会返回 true 却完全不写入像素。
        // 不在此处拦截的话，最终只会产出一张尺寸退化、颜色为位图未初始化内容的无效图片。
        if (User32.IsIconic(hWnd))
        {
            return Failure(
                WindowHelper.GetWindowBounds(hWnd),
                CaptureMode.PrintWindow,
                sw,
                "目标窗口当前处于最小化状态。最小化窗口没有可截图的客户区，" +
                "请先还原该窗口（后台截图同样要求窗口已还原）后再试。");
        }

        (int width, int height) = WindowHelper.GetPrintWindowSize(hWnd);
        if (width <= 0 || height <= 0)
        {
            return Failure(
                WindowHelper.GetWindowBounds(hWnd),
                CaptureMode.PrintWindow,
                sw,
                "无法获取目标窗口的有效外接矩形大小。");
        }

        CaptureResult result = CaptureWindowViaPrintWindow(hWnd, width, height, sw);
        if (result.Success)
        {
            return result;
        }

        // 桌面裁剪回退：捕获的是当前桌面像素，被遮挡内容仍会遮挡，
        // 因此仅作为「拿到画面」的兜底，不代表后台无遮挡截图
        if (allowFallbackToDesktop && User32.IsWindowVisible(hWnd) && !User32.IsIconic(hWnd))
        {
            CaptureBounds windowBounds = WindowHelper.GetWindowBounds(hWnd);

            if (!windowBounds.IsEmpty)
            {
                CaptureResult fallback = CaptureRegionInternal(windowBounds, sw);
                if (fallback.Success && fallback.Frame != null)
                {
                    return new CaptureResult
                    {
                        Success = true,
                        Frame = fallback.Frame,
                        Bounds = windowBounds,
                        Mode = CaptureMode.DesktopCropFallback,
                        Elapsed = sw.Elapsed,
                        ErrorMessage = string.IsNullOrEmpty(result.ErrorMessage)
                            ? "已回退到桌面裁剪。"
                            : $"已回退到桌面裁剪（原因：{result.ErrorMessage}）。"
                    };
                }
            }
        }

        return result;
    }

    /// <inheritdoc />
    public byte[]? CaptureWindowAsPng(IntPtr hWnd, bool allowFallbackToDesktop = true)
    {
        using CaptureResult result = CaptureWindow(hWnd, allowFallbackToDesktop);

        if (!result.Success || result.Frame == null)
        {
            return null;
        }

        using Bitmap bitmap = CaptureFrameConverter.ToBitmap(result.Frame);
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <inheritdoc />
    public byte[]? CaptureWindowAsBgra(IntPtr hWnd, bool allowFallbackToDesktop = true)
    {
        using CaptureResult result = CaptureWindow(hWnd, allowFallbackToDesktop);

        if (!result.Success || result.Frame == null)
        {
            return null;
        }

        return result.Frame.ToArray();
    }

    private static CaptureBounds ToCaptureBounds(Rectangle bounds)
        => new(bounds.X, bounds.Y, bounds.Width, bounds.Height);

    private static CaptureBounds ToCaptureBounds(Rectangle? bounds)
        => bounds.HasValue ? new CaptureBounds(bounds.Value.X, bounds.Value.Y, bounds.Value.Width, bounds.Value.Height) : CaptureBounds.Empty;

    private static CaptureResult CaptureRegionInternal(CaptureBounds region, Stopwatch sw)
    {
        IntPtr hDesktopDc = User32.GetDC(IntPtr.Zero);
        if (hDesktopDc == IntPtr.Zero)
        {
            return Failure(region, CaptureMode.DesktopBitBlt, sw, $"获取桌面 DC 失败，错误码: {Marshal.GetLastWin32Error()}");
        }

        using var desktopDc = new SafeWindowDcHandle(IntPtr.Zero, hDesktopDc);
        using var memDc = Gdi32.CreateCompatibleDC(desktopDc.DangerousGetHandle());

        if (memDc.IsInvalid)
        {
            return Failure(region, CaptureMode.DesktopBitBlt, sw, $"创建兼容内存 DC 失败，错误码: {Marshal.GetLastWin32Error()}");
        }

        using var hBitmap = Gdi32.CreateCompatibleBitmap(desktopDc.DangerousGetHandle(), region.Width, region.Height);
        if (hBitmap.IsInvalid)
        {
            return Failure(region, CaptureMode.DesktopBitBlt, sw, $"创建兼容位图失败，错误码: {Marshal.GetLastWin32Error()}");
        }

        IntPtr hOldBmp = Gdi32.SelectObject(memDc, hBitmap);
        try
        {
            // CAPTUREBLT 确保包含分层窗口（半透明与 Aero 窗口）
            bool success = Gdi32.BitBlt(
                memDc,
                0,
                0,
                region.Width,
                region.Height,
                desktopDc.DangerousGetHandle(),
                region.X,
                region.Y,
                NativeConstants.SRCCOPY | NativeConstants.CAPTUREBLT);

            if (!success)
            {
                return Failure(region, CaptureMode.DesktopBitBlt, sw, $"BitBlt 拷贝像素失败，错误码: {Marshal.GetLastWin32Error()}");
            }

            using Bitmap managed = CreateManagedBitmap(hBitmap.DangerousGetHandle());

            return new CaptureResult
            {
                Success = true,
                Frame = CaptureFrameConverter.ToFrame(managed),
                Bounds = region,
                Mode = CaptureMode.DesktopBitBlt,
                Elapsed = sw.Elapsed
            };
        }
        finally
        {
            // 还原选入的旧对象，避免位图被删除后 DC 引用失效
            Gdi32.SelectObject(memDc.DangerousGetHandle(), hOldBmp);
        }
    }

    private CaptureResult CaptureWindowViaPrintWindow(IntPtr hWnd, int width, int height, Stopwatch sw)
    {
        IntPtr hWindowDc = User32.GetWindowDC(hWnd);
        if (hWindowDc == IntPtr.Zero)
        {
            return Failure(CaptureBounds.Empty, CaptureMode.PrintWindow, sw, $"获取窗口 DC 失败，错误码: {Marshal.GetLastWin32Error()}");
        }

        using var windowDc = new SafeWindowDcHandle(hWnd, hWindowDc);
        using var memDc = Gdi32.CreateCompatibleDC(windowDc.DangerousGetHandle());

        if (memDc.IsInvalid)
        {
            return Failure(CaptureBounds.Empty, CaptureMode.PrintWindow, sw, $"创建内存 DC 失败，错误码: {Marshal.GetLastWin32Error()}");
        }

        using var hBitmap = Gdi32.CreateCompatibleBitmap(windowDc.DangerousGetHandle(), width, height);
        if (hBitmap.IsInvalid)
        {
            return Failure(CaptureBounds.Empty, CaptureMode.PrintWindow, sw, $"创建位图句柄失败，错误码: {Marshal.GetLastWin32Error()}");
        }

        IntPtr hOldBmp = Gdi32.SelectObject(memDc, hBitmap);
        try
        {
            string? firstFailure = null;

            // CreateCompatibleBitmap 不保证清零，先记录初始内容作为基线，
            // 用于识别「PrintWindow 返回 true 但未写入任何像素」的情形。
            int[] baseline = SamplePixels(hBitmap.DangerousGetHandle());

            foreach (uint flags in BuildFlagSequence())
            {
                bool printed = User32.PrintWindow(hWnd, memDc.DangerousGetHandle(), flags);

                if (!printed)
                {
                    firstFailure ??= $"PrintWindow(flags=0x{flags:X}) 返回 false，错误码 {Marshal.GetLastWin32Error()}";
                    continue;
                }

                if (IsUnchangedFromBaseline(hBitmap.DangerousGetHandle(), baseline))
                {
                    firstFailure ??= $"PrintWindow(flags=0x{flags:X}) 返回成功但未绘制任何像素";
                    continue;
                }

                if (IsAllBlack(hBitmap.DangerousGetHandle()))
                {
                    firstFailure ??= $"PrintWindow(flags=0x{flags:X}) 返回成功但内容全黑";
                    continue;
                }

                using Bitmap managed = CreateManagedBitmap(hBitmap.DangerousGetHandle());

                return new CaptureResult
                {
                    Success = true,
                    Frame = CaptureFrameConverter.ToFrame(managed),
                    Bounds = new CaptureBounds(0, 0, width, height),
                    Mode = CaptureMode.PrintWindow,
                    Elapsed = sw.Elapsed
                };
            }

            return Failure(
                new CaptureBounds(0, 0, width, height),
                CaptureMode.PrintWindow,
                sw,
                firstFailure ?? "PrintWindow 调用失败。");
        }
        finally
        {
            Gdi32.SelectObject(memDc.DangerousGetHandle(), hOldBmp);
        }
    }

    /// <summary>
    /// 按固定步长采样位图像素，返回各采样点的 ARGB 值。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="IsAllBlack"/> 共用采样网格，保证基线与结果的采样点一一对应。
    /// </remarks>
    /// <param name="hBitmap">原生位图句柄。</param>
    /// <returns>采样点 ARGB 序列；位图不可读时返回空数组。</returns>
    private static int[] SamplePixels(IntPtr hBitmap)
    {
        try
        {
            using Image temp = Image.FromHbitmap(hBitmap);
            using var probe = new Bitmap(temp);

            var samples = new List<int>();

            for (int y = 0; y < probe.Height; y += BlackSampleStep)
            {
                for (int x = 0; x < probe.Width; x += BlackSampleStep)
                {
                    samples.Add(probe.GetPixel(x, y).ToArgb());
                }
            }

            return samples.ToArray();
        }
        catch (Exception ex)
        {
            // 位图不可读时不参与空白判定，交由后续亮度判定与降级路径处理
            System.Diagnostics.Trace.WriteLine($"采样位图像素失败: {ex.Message}");
            return Array.Empty<int>();
        }
    }

    /// <summary>
    /// 判断位图内容是否与调用 <c>PrintWindow</c> 之前完全一致。
    /// </summary>
    /// <remarks>
    /// <c>CreateCompatibleBitmap</c> 创建的位图内容未初始化。当目标窗口最小化、
    /// 被 DWM 遮挡或自身拒绝绘制时，<c>PrintWindow</c> 会返回 true 却完全不写入像素，
    /// 此时位图仍是未初始化的随机内容，仅凭亮度阈值无法与真实画面区分。
    /// 与调用前快照逐点比对可以确定性地识别「成功但未绘制」这一情形。
    /// </remarks>
    /// <param name="hBitmap">原生位图句柄。</param>
    /// <param name="baseline">调用 <c>PrintWindow</c> 之前的采样快照。</param>
    /// <returns>内容与基线完全一致时返回 <c>true</c>。</returns>
    private static bool IsUnchangedFromBaseline(IntPtr hBitmap, int[] baseline)
    {
        // 基线为空说明快照不可用，此时不做判定，避免把无法采样误判为空白
        if (baseline.Length == 0)
        {
            return false;
        }

        int[] current = SamplePixels(hBitmap);

        // 尺寸不一致说明位图被替换，同样不做判定
        if (current.Length != baseline.Length)
        {
            return false;
        }

        for (int i = 0; i < current.Length; i++)
        {
            if (current[i] != baseline[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 构造 PrintWindow 标志位尝试序列。
    /// Windows 8.1 及以上优先 <c>PW_RENDERFULLCONTENT</c> 以捕获硬件加速与 DWM 渲染内容，
    /// 随后退回 <c>PW_DEFAULT</c>，最后尝试 <c>PW_CLIENTONLY</c>。
    /// </summary>
    private IEnumerable<uint> BuildFlagSequence()
    {
        if (_osVersion.IsWindows81OrGreater)
        {
            yield return PwRenderFullContent;
        }

        yield return _osVersion.GetRecommendedPrintWindowFlags(clientAreaOnly: false);
        yield return PwDefault;
        yield return PwClientOnly;
    }

    /// <summary>
    /// 判定位图内容是否全黑。采用抽样而非全像素扫描，把开销控制在可接受范围。
    /// </summary>
    private static bool IsAllBlack(IntPtr hBitmap)
    {
        using Image temp = Image.FromHbitmap(hBitmap);
        using var probe = new Bitmap(temp);

        for (int y = 0; y < probe.Height; y += BlackSampleStep)
        {
            for (int x = 0; x < probe.Width; x += BlackSampleStep)
            {
                Color pixel = probe.GetPixel(x, y);

                if (pixel.R > BlackThreshold || pixel.G > BlackThreshold || pixel.B > BlackThreshold)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static Bitmap CreateManagedBitmap(IntPtr hBitmap)
    {
        using Image temp = Image.FromHbitmap(hBitmap);

        // 复制为独立托管位图，使原生 HBITMAP 可以立即释放
        return new Bitmap(temp);
    }

    private static CaptureResult Failure(CaptureBounds bounds, CaptureMode mode, Stopwatch sw, string message)
    {
        return new CaptureResult
        {
            Success = false,
            Bounds = bounds,
            Mode = mode,
            Elapsed = sw.Elapsed,
            ErrorMessage = message
        };
    }
}