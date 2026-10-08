using System.Drawing;
using GameTools.Core.Models;

namespace GameTools.Core.Abstractions;

/// <summary>
/// 屏幕与窗口截图接口
/// </summary>
public interface IScreenCapture
{
    /// <summary>
    /// 抓取主屏幕或多显示器完整虚拟桌面
    /// </summary>
    CaptureResult CaptureFullScreen(bool allMonitors = true);

    /// <summary>
    /// 抓取指定矩形屏幕区域
    /// </summary>
    CaptureResult CaptureRegion(Rectangle region);

    /// <summary>
    /// 抓取指定窗口句柄画面（支持后台非激活窗口，在 Win7 上自动使用安全兼容参数）
    /// </summary>
    CaptureResult CaptureWindow(IntPtr hWnd, bool allowFallbackToDesktop = true);

    /// <summary>
    /// 截取指定窗口并直接导出为 PNG 格式字节数组
    /// </summary>
    byte[]? CaptureWindowAsPng(IntPtr hWnd, bool allowFallbackToDesktop = true);
}
