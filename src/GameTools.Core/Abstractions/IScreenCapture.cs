using GameTools.Core.Models;

namespace GameTools.Core.Abstractions;

/// <summary>
/// 屏幕与窗口截图接口。
/// </summary>
public interface IScreenCapture
{
    /// <summary>
    /// 抓取主屏幕或多显示器完整虚拟桌面。
    /// </summary>
    /// <param name="allMonitors">是否包含全部显示器。</param>
    CaptureResult CaptureFullScreen(bool allMonitors = true);

    /// <summary>
    /// 抓取指定矩形屏幕区域。
    /// </summary>
    /// <param name="region">目标区域。</param>
    CaptureResult CaptureRegion(CaptureBounds region);

    /// <summary>
    /// 抓取指定窗口句柄画面（支持后台非激活窗口，Win7 上自动使用安全兼容参数）。
    /// </summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="allowFallbackToDesktop">失败时是否回退到桌面裁剪。</param>
    CaptureResult CaptureWindow(IntPtr hWnd, bool allowFallbackToDesktop = true);

    /// <summary>
    /// 截取指定窗口并直接导出为 PNG 格式字节数组。
    /// </summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="allowFallbackToDesktop">失败时是否回退到桌面裁剪。</param>
    byte[]? CaptureWindowAsPng(IntPtr hWnd, bool allowFallbackToDesktop = true);

    /// <summary>
    /// 截取指定窗口并直接导出为 BGRA 原始像素数据。
    /// </summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="allowFallbackToDesktop">失败时是否回退到桌面裁剪。</param>
    byte[]? CaptureWindowAsBgra(IntPtr hWnd, bool allowFallbackToDesktop = true);
}