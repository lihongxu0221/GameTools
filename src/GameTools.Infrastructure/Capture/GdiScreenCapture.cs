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
public sealed class GdiScreenCapture : IScreenCapture, ICaptureSourceProbe
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

    /// <summary>
    /// 单色判定时允许的采样点色差上限。
    /// </summary>
    /// <remarks>
    /// 取一个很小的容差而非要求完全相等：缩放与颜色转换会引入极小的数值抖动，
    /// 要求严格相等会把可用画面误判为单色。
    /// </remarks>
    private const int MonochromeTolerance = 2;

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

    /// <inheritdoc />
    public CaptureSourceReport Probe(IntPtr windowHandle)
    {
        var stopwatch = Stopwatch.StartNew();
        var attempts = new List<CaptureSourceAttempt>();

        if (windowHandle == IntPtr.Zero || !User32.IsWindow(windowHandle))
        {
            return new CaptureSourceReport
            {
                Message = "窗口句柄无效或窗口已关闭。",
                Elapsed = stopwatch.Elapsed
            };
        }

        foreach (CaptureSourceKind source in BuildProbeSequence())
        {
            CaptureResult result = Capture(windowHandle, source);
            bool usable = result.Success && result.Frame != null;

            attempts.Add(new CaptureSourceAttempt
            {
                Source = source,
                Usable = usable,
                Message = usable
                    ? $"可用（{result.Frame!.Width}x{result.Frame.Height}）。"
                    : result.ErrorMessage ?? "未取得可用画面。"
            });

            if (usable)
            {
                return new CaptureSourceReport
                {
                    Source = source,
                    Attempts = attempts,
                    Message = $"已探测到可用捕获源：{Describe(source)}。",
                    Elapsed = stopwatch.Elapsed
                };
            }
        }

        return new CaptureSourceReport
        {
            Attempts = attempts,
            Message = "全部捕获源均不可用。若目标为硬件加速窗口，请确认其未被最小化或完全遮挡后重试。",
            Elapsed = stopwatch.Elapsed
        };
    }

    /// <inheritdoc />
    public CaptureResult Capture(IntPtr windowHandle, CaptureSourceKind source)
    {
        var stopwatch = Stopwatch.StartNew();

        if (windowHandle == IntPtr.Zero || !User32.IsWindow(windowHandle))
        {
            return Failure(
                CaptureBounds.Empty, CaptureMode.PrintWindow, stopwatch, "窗口句柄无效或窗口已关闭。");
        }

        if (source == CaptureSourceKind.ScreenBitBlt)
        {
            return CaptureScreenRegion(windowHandle, stopwatch);
        }

        if (!TryGetPrintWindowFlags(source, out uint flags))
        {
            return Failure(
                CaptureBounds.Empty,
                CaptureMode.PrintWindow,
                stopwatch,
                $"未知的捕获源：{source}。");
        }

        if (flags == PwRenderFullContent && !_osVersion.IsWindows81OrGreater)
        {
            return Failure(
                CaptureBounds.Empty,
                CaptureMode.PrintWindow,
                stopwatch,
                "PW_RENDERFULLCONTENT 自 Windows 8.1 起才受支持，当前系统不可用。");
        }

        return CaptureWindowWithFlag(windowHandle, flags, stopwatch);
    }

    /// <summary>
    /// 按优先级列出应当尝试的捕获源。
    /// </summary>
    /// <remarks>
    /// 顺序即实测得出的可靠度：先尝试能覆盖 DWM 与硬件加速渲染的标志，
    /// 最后才退到屏幕区域捕获——后者取到的是当前桌面像素，
    /// 窗口被遮挡时会连遮挡物一起截入。
    /// </remarks>
    private IEnumerable<CaptureSourceKind> BuildProbeSequence()
    {
        if (_osVersion.IsWindows81OrGreater)
        {
            yield return CaptureSourceKind.PrintWindowRenderFullContent;
        }

        yield return CaptureSourceKind.PrintWindowDefault;
        yield return CaptureSourceKind.PrintWindowClientOnly;
        yield return CaptureSourceKind.ScreenBitBlt;
    }

    /// <summary>
    /// 把捕获源映射为 <c>PrintWindow</c> 的标志值。
    /// </summary>
    /// <param name="source">捕获源。</param>
    /// <param name="flags">输出标志值。</param>
    /// <returns>是否支持该捕获源。</returns>
    private static bool TryGetPrintWindowFlags(CaptureSourceKind source, out uint flags)
    {
        switch (source)
        {
            case CaptureSourceKind.PrintWindowRenderFullContent:
                flags = PwRenderFullContent;
                return true;
            case CaptureSourceKind.PrintWindowDefault:
                flags = PwDefault;
                return true;
            case CaptureSourceKind.PrintWindowClientOnly:
                flags = PwClientOnly;
                return true;
            default:
                flags = 0;
                return false;
        }
    }

    /// <summary>
    /// 以指定标志捕获窗口，供探测与显式来源捕获使用。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="CaptureWindow"/> 的自动降级序列分开：本方法只使用给定标志，
    /// 失败即返回失败，从而让调用方确切知道画面是否来自预期的那一种方式。
    /// 两条路径共用相同的可用性判定（未写入像素、全黑、单色）。
    /// </remarks>
    private CaptureResult CaptureWindowWithFlag(IntPtr hWnd, uint flags, Stopwatch sw)
    {
        if (User32.IsIconic(hWnd))
        {
            return Failure(
                new CaptureBounds(0, 0, 0, 0), CaptureMode.PrintWindow, sw,
                "窗口已最小化，没有可绘制的客户区。");
        }

        (int width, int height) = WindowHelper.GetPrintWindowSize(hWnd);
        if (width <= 0 || height <= 0)
        {
            return Failure(
                CaptureBounds.Empty, CaptureMode.PrintWindow, sw, "窗口尺寸无效。");
        }

        IntPtr hWindowDc = User32.GetWindowDC(hWnd);
        if (hWindowDc == IntPtr.Zero)
        {
            return Failure(
                new CaptureBounds(0, 0, width, height), CaptureMode.PrintWindow, sw,
                $"获取窗口 DC 失败，错误码: {Marshal.GetLastWin32Error()}");
        }

        using var windowDc = new SafeWindowDcHandle(hWnd, hWindowDc);
        using var memDc = Gdi32.CreateCompatibleDC(windowDc.DangerousGetHandle());

        if (memDc.IsInvalid)
        {
            return Failure(
                new CaptureBounds(0, 0, width, height), CaptureMode.PrintWindow, sw,
                $"创建内存 DC 失败，错误码: {Marshal.GetLastWin32Error()}");
        }

        using var hBitmap = Gdi32.CreateCompatibleBitmap(windowDc.DangerousGetHandle(), width, height);
        if (hBitmap.IsInvalid)
        {
            return Failure(
                new CaptureBounds(0, 0, width, height), CaptureMode.PrintWindow, sw,
                $"创建位图句柄失败，错误码: {Marshal.GetLastWin32Error()}");
        }

        IntPtr hOldBmp = Gdi32.SelectObject(memDc, hBitmap);
        try
        {
            // CreateCompatibleBitmap 不保证清零，先记录初始内容作为基线，
            // 用于识别「PrintWindow 返回 true 但未写入任何像素」的情形。
            int[] baseline = SamplePixels(hBitmap.DangerousGetHandle());

            if (!User32.PrintWindow(hWnd, memDc.DangerousGetHandle(), flags))
            {
                return Failure(
                    new CaptureBounds(0, 0, width, height), CaptureMode.PrintWindow, sw,
                    $"PrintWindow(flags=0x{flags:X}) 返回 false，错误码 {Marshal.GetLastWin32Error()}");
            }

            if (IsUnchangedFromBaseline(hBitmap.DangerousGetHandle(), baseline))
            {
                return Failure(
                    new CaptureBounds(0, 0, width, height), CaptureMode.PrintWindow, sw,
                    $"PrintWindow(flags=0x{flags:X}) 返回成功但未绘制任何像素");
            }

            if (IsAllBlack(hBitmap.DangerousGetHandle()))
            {
                return Failure(
                    new CaptureBounds(0, 0, width, height), CaptureMode.PrintWindow, sw,
                    $"PrintWindow(flags=0x{flags:X}) 返回成功但内容全黑");
            }

            if (IsMonochrome(hBitmap.DangerousGetHandle()))
            {
                return Failure(
                    new CaptureBounds(0, 0, width, height), CaptureMode.PrintWindow, sw,
                    $"PrintWindow(flags=0x{flags:X}) 返回成功但内容为单一颜色，无可用纹理");
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
        finally
        {
            // 还原选入的旧对象，避免位图被删除后 DC 引用失效
            Gdi32.SelectObject(memDc.DangerousGetHandle(), hOldBmp);
        }
    }

    /// <summary>
    /// 通过屏幕区域捕获取得窗口画面。
    /// </summary>
    /// <remarks>
    /// 这是硬件加速窗口唯一可行的方式，但取到的是当前桌面像素：
    /// 窗口被遮挡时会连遮挡物一起截入，最小化时也没有内容。
    /// 因此必须先校验可见性与尺寸，并在结果中说明这一局限。
    /// </remarks>
    private CaptureResult CaptureScreenRegion(IntPtr hWnd, Stopwatch sw)
    {
        if (!User32.IsWindowVisible(hWnd) || User32.IsIconic(hWnd))
        {
            return Failure(
                CaptureBounds.Empty, CaptureMode.DesktopBitBlt, sw,
                "窗口不可见或已最小化，屏幕区域捕获取不到内容。");
        }

        // 捕获范围与 PrintWindow 保持一致：都取窗口外框（含不可见缩放边框），
        // 这样两种来源产生的帧坐标系原点相同，识图命中坐标可以共用同一套换算。
        CaptureBounds windowBounds = WindowHelper.GetOuterFrameBounds(hWnd);
        if (windowBounds.IsEmpty)
        {
            return Failure(
                CaptureBounds.Empty, CaptureMode.DesktopBitBlt, sw, "无法取得窗口在屏幕上的位置。");
        }

        CaptureResult region = CaptureRegionInternal(windowBounds, sw);
        if (!region.Success)
        {
            return Failure(
                windowBounds, CaptureMode.DesktopBitBlt, sw,
                region.ErrorMessage ?? "屏幕区域捕获失败。");
        }

        return new CaptureResult
        {
            Success = true,
            Frame = region.Frame,
            Bounds = windowBounds,
            Mode = CaptureMode.DesktopBitBlt,
            Elapsed = sw.Elapsed,
            ErrorMessage = "画面取自屏幕像素，窗口被遮挡时截入的将是遮挡内容。"
        };
    }

    /// <summary>
    /// 判断位图内容是否为单一颜色。
    /// </summary>
    /// <remarks>
    /// 比「非全黑」更严格：全黑窗口、纯色背景的空白窗口都会返回 true，
    /// 这类画面虽非全黑却没有任何纹理，无法用于特征匹配。
    /// </remarks>
    private static bool IsMonochrome(IntPtr hBitmap)
    {
        int[] samples = SamplePixels(hBitmap);
        if (samples.Length < 2)
        {
            // 采样点不足一个像素时无法判定，交由上层按可用处理
            return false;
        }

        int first = samples[0];

        for (int i = 1; i < samples.Length; i++)
        {
            int current = samples[i];

            if (Math.Abs((current >> 16 & 0xFF) - (first >> 16 & 0xFF)) > MonochromeTolerance ||
                Math.Abs((current >> 8 & 0xFF) - (first >> 8 & 0xFF)) > MonochromeTolerance ||
                Math.Abs((current & 0xFF) - (first & 0xFF)) > MonochromeTolerance)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 生成捕获源的中文描述，用于结果说明。
    /// </summary>
    private static string Describe(CaptureSourceKind source) => source switch
    {
        CaptureSourceKind.PrintWindowRenderFullContent => "PrintWindow + PW_RENDERFULLCONTENT",
        CaptureSourceKind.PrintWindowDefault => "PrintWindow 默认标志",
        CaptureSourceKind.PrintWindowClientOnly => "PrintWindow + PW_CLIENTONLY",
        CaptureSourceKind.ScreenBitBlt => "屏幕区域 BitBlt",
        _ => "未知来源"
    };

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
