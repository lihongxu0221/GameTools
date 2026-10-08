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
/// 2. <c>PrintWindow</c> 返回 true 但内容全黑时按失败处理并降级——硬件加速窗口在
///    旧系统与部分驱动组合下会出现该现象；
/// 3. 全黑判定采用抽样并带可配置阈值，避免把合法纯黑画面误判为失败；
/// 4. 所有 GDI 对象以安全句柄包裹，<c>SelectObject</c> 的原对象在 finally 中还原；
/// 5. 契约层以 <see cref="CaptureFrame"/> 承载 BGRA 像素，位图仅在本层临时构造并立即释放。
/// </remarks>
public sealed class GdiScreenCapture : IScreenCapture
{
    /// <summary>
    /// 全黑判定阈值：采样点亮度低于该值即视为黑。
    /// 取 8 而非 0，用于容忍压缩与缩放引入的轻微噪声。
    /// </summary>
    private const int BlackThreshold = 8;

    /// <summary>
    /// 全黑判定采样步长：每 step 个像素采样一次，兼顾准确性与性能。
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

            foreach (uint flags in BuildFlagSequence())
            {
                bool printed = User32.PrintWindow(hWnd, memDc.DangerousGetHandle(), flags);

                if (!printed)
                {
                    firstFailure ??= $"PrintWindow(flags=0x{flags:X}) 返回 false，错误码 {Marshal.GetLastWin32Error()}";
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