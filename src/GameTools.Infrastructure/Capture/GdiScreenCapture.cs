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
/// 基于 GDI / Win32 的高性能屏幕与窗口截图实现
/// 全面兼容 Windows 7 SP1 ~ Windows 11，具备自动降级与资源防泄漏保护
/// </summary>
public sealed class GdiScreenCapture : IScreenCapture
{
    /// <summary>
    /// 抓取全屏或多显示器完整虚拟桌面
    /// </summary>
    public CaptureResult CaptureFullScreen(bool allMonitors = true)
    {
        var sw = Stopwatch.StartNew();
        Rectangle bounds = allMonitors ? SystemInformation.VirtualScreen : Screen.PrimaryScreen?.Bounds ?? Rectangle.Empty;

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return new CaptureResult
            {
                Success = false,
                Bounds = bounds,
                Mode = CaptureMode.DesktopBitBlt,
                Elapsed = sw.Elapsed,
                ErrorMessage = "未检测到有效的屏幕显示区域。"
            };
        }

        return CaptureRegionInternal(bounds, sw);
    }

    /// <summary>
    /// 抓取指定矩形屏幕区域
    /// </summary>
    public CaptureResult CaptureRegion(Rectangle region)
    {
        var sw = Stopwatch.StartNew();
        if (region.Width <= 0 || region.Height <= 0)
        {
            return new CaptureResult
            {
                Success = false,
                Bounds = region,
                Mode = CaptureMode.DesktopBitBlt,
                Elapsed = sw.Elapsed,
                ErrorMessage = "指定的截图区域宽度或高度必须大于 0。"
            };
        }

        return CaptureRegionInternal(region, sw);
    }

    /// <summary>
    /// 抓取指定窗口句柄画面（支持后台非激活窗口，在 Win7 上自动使用安全兼容参数）
    /// </summary>
    public CaptureResult CaptureWindow(IntPtr hWnd, bool allowFallbackToDesktop = true)
    {
        var sw = Stopwatch.StartNew();
        if (hWnd == IntPtr.Zero)
        {
            return new CaptureResult
            {
                Success = false,
                Bounds = Rectangle.Empty,
                Mode = CaptureMode.PrintWindow,
                Elapsed = sw.Elapsed,
                ErrorMessage = "窗口句柄无效 (IntPtr.Zero)。"
            };
        }

        var bounds = WindowHelper.GetWindowBounds(hWnd);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return new CaptureResult
            {
                Success = false,
                Bounds = bounds,
                Mode = CaptureMode.PrintWindow,
                Elapsed = sw.Elapsed,
                ErrorMessage = "无法获取目标窗口的有效外接矩形大小。"
            };
        }

        // 尝试使用 PrintWindow 进行后台无遮挡抓取
        var result = CaptureWindowViaPrintWindow(hWnd, bounds, sw);
        if (result.Success && result.Image != null)
        {
            return result;
        }

        // 如果 PrintWindow 失败且允许降级为桌面裁剪
        if (allowFallbackToDesktop && User32.IsWindowVisible(hWnd) && !User32.IsIconic(hWnd))
        {
            var fallbackResult = CaptureRegionInternal(bounds, sw);
            if (fallbackResult.Success)
            {
                return new CaptureResult
                {
                    Success = true,
                    Image = fallbackResult.Image,
                    Bounds = bounds,
                    Mode = CaptureMode.DesktopCropFallback,
                    Elapsed = sw.Elapsed
                };
            }
        }

        return result;
    }

    /// <summary>
    /// 截取指定窗口并直接导出为 PNG 格式字节数组
    /// </summary>
    public byte[]? CaptureWindowAsPng(IntPtr hWnd, bool allowFallbackToDesktop = true)
    {
        using var result = CaptureWindow(hWnd, allowFallbackToDesktop);
        if (!result.Success || result.Image == null)
        {
            return null;
        }

        using var ms = new MemoryStream();
        result.Image.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static CaptureResult CaptureRegionInternal(Rectangle region, Stopwatch sw)
    {
        IntPtr hDesktopDc = User32.GetDC(IntPtr.Zero);
        if (hDesktopDc == IntPtr.Zero)
        {
            return new CaptureResult
            {
                Success = false,
                Bounds = region,
                Mode = CaptureMode.DesktopBitBlt,
                Elapsed = sw.Elapsed,
                ErrorMessage = $"获取桌面 DC 失败，错误码: {Marshal.GetLastWin32Error()}"
            };
        }

        using var desktopDc = new SafeWindowDcHandle(IntPtr.Zero, hDesktopDc);
        using var memDc = Gdi32.CreateCompatibleDC(desktopDc.DangerousGetHandle());
        if (memDc.IsInvalid)
        {
            return new CaptureResult
            {
                Success = false,
                Bounds = region,
                Mode = CaptureMode.DesktopBitBlt,
                Elapsed = sw.Elapsed,
                ErrorMessage = $"创建兼容内存 DC 失败，错误码: {Marshal.GetLastWin32Error()}"
            };
        }

        using var hBitmap = Gdi32.CreateCompatibleBitmap(desktopDc.DangerousGetHandle(), region.Width, region.Height);
        if (hBitmap.IsInvalid)
        {
            return new CaptureResult
            {
                Success = false,
                Bounds = region,
                Mode = CaptureMode.DesktopBitBlt,
                Elapsed = sw.Elapsed,
                ErrorMessage = $"创建兼容位图失败，错误码: {Marshal.GetLastWin32Error()}"
            };
        }

        IntPtr hOldBmp = Gdi32.SelectObject(memDc, hBitmap);
        try
        {
            // CAPTUREBLT 确保包含分层窗口（半透明/Aero 窗口）
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
                return new CaptureResult
                {
                    Success = false,
                    Bounds = region,
                    Mode = CaptureMode.DesktopBitBlt,
                    Elapsed = sw.Elapsed,
                    ErrorMessage = $"BitBlt 拷贝像素失败，错误码: {Marshal.GetLastWin32Error()}"
                };
            }

            // 从 HBITMAP 创建托管 Bitmap（创建深拷贝，以便立即释放原生 HBITMAP）
            Bitmap? managedBitmap = null;
            using (var temp = Image.FromHbitmap(hBitmap.DangerousGetHandle()))
            {
                managedBitmap = new Bitmap(temp);
            }

            return new CaptureResult
            {
                Success = true,
                Image = managedBitmap,
                Bounds = region,
                Mode = CaptureMode.DesktopBitBlt,
                Elapsed = sw.Elapsed
            };
        }
        finally
        {
            Gdi32.SelectObject(memDc.DangerousGetHandle(), hOldBmp);
        }
    }

    private static CaptureResult CaptureWindowViaPrintWindow(IntPtr hWnd, Rectangle bounds, Stopwatch sw)
    {
        IntPtr hWindowDc = User32.GetWindowDC(hWnd);
        if (hWindowDc == IntPtr.Zero)
        {
            return new CaptureResult
            {
                Success = false,
                Bounds = bounds,
                Mode = CaptureMode.PrintWindow,
                Elapsed = sw.Elapsed,
                ErrorMessage = $"获取窗口 DC 失败，错误码: {Marshal.GetLastWin32Error()}"
            };
        }

        using var windowDc = new SafeWindowDcHandle(hWnd, hWindowDc);
        using var memDc = Gdi32.CreateCompatibleDC(windowDc.DangerousGetHandle());
        if (memDc.IsInvalid)
        {
            return new CaptureResult
            {
                Success = false,
                Bounds = bounds,
                Mode = CaptureMode.PrintWindow,
                Elapsed = sw.Elapsed,
                ErrorMessage = $"创建内存 DC 失败，错误码: {Marshal.GetLastWin32Error()}"
            };
        }

        using var hBitmap = Gdi32.CreateCompatibleBitmap(windowDc.DangerousGetHandle(), bounds.Width, bounds.Height);
        if (hBitmap.IsInvalid)
        {
            return new CaptureResult
            {
                Success = false,
                Bounds = bounds,
                Mode = CaptureMode.PrintWindow,
                Elapsed = sw.Elapsed,
                ErrorMessage = $"创建位图句柄失败，错误码: {Marshal.GetLastWin32Error()}"
            };
        }

        IntPtr hOldBmp = Gdi32.SelectObject(memDc, hBitmap);
        try
        {
            bool printed = false;

            // 1. 如果是 Windows 8.1+，先尝试 PW_RENDERFULLCONTENT (0x02)
            if (OSVersionHelper.IsWindows81OrGreater)
            {
                printed = User32.PrintWindow(hWnd, memDc.DangerousGetHandle(), NativeConstants.PW_RENDERFULLCONTENT);
            }

            // 2. 如果在 Windows 7 上，或者高版本系统下 PW_RENDERFULLCONTENT 失败，使用基础标志 0
            if (!printed)
            {
                printed = User32.PrintWindow(hWnd, memDc.DangerousGetHandle(), NativeConstants.PW_DEFAULT);
            }

            if (!printed)
            {
                return new CaptureResult
                {
                    Success = false,
                    Bounds = bounds,
                    Mode = CaptureMode.PrintWindow,
                    Elapsed = sw.Elapsed,
                    ErrorMessage = $"PrintWindow 调用失败，错误码: {Marshal.GetLastWin32Error()}"
                };
            }

            Bitmap managedBitmap;
            using (var temp = Image.FromHbitmap(hBitmap.DangerousGetHandle()))
            {
                managedBitmap = new Bitmap(temp);
            }

            return new CaptureResult
            {
                Success = true,
                Image = managedBitmap,
                Bounds = bounds,
                Mode = CaptureMode.PrintWindow,
                Elapsed = sw.Elapsed
            };
        }
        finally
        {
            Gdi32.SelectObject(memDc.DangerousGetHandle(), hOldBmp);
        }
    }
}
