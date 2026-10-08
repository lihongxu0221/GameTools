namespace GameTools.Core.Enums;

/// <summary>
/// 截图捕获方式
/// </summary>
public enum CaptureMode
{
    /// <summary>
    /// 全屏/区域桌面抓取 (BitBlt)
    /// </summary>
    DesktopBitBlt = 0,

    /// <summary>
    /// 窗口后台绘制抓取 (PrintWindow)
    /// </summary>
    PrintWindow = 1,

    /// <summary>
    /// 桌面区域裁剪回退 (Fallback)
    /// </summary>
    DesktopCropFallback = 2
}
