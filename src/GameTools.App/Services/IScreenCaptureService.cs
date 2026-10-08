using GameTools.Core.Enums;
using GameTools.Core.Models;

namespace GameTools.App.Services;

/// <summary>
/// 截图能力的服务接口。
/// </summary>
/// <remarks>
/// 引入接口的目的：让 <see cref="ViewModels.MainViewModel"/> 依赖抽象而非具体实现，
/// 从而可以在无 UI、无桌面交互的环境中用替身实现做单元测试。
/// </remarks>
public interface IScreenCaptureService
{
    /// <summary>
    /// 抓取全屏或虚拟桌面并保存为 PNG。
    /// </summary>
    /// <param name="allMonitors">是否包含全部显示器。</param>
    CaptureOutcome CaptureFullScreen(bool allMonitors);

    /// <summary>
    /// 抓取指定矩形区域并保存为 PNG。
    /// </summary>
    /// <param name="x">起点 X。</param>
    /// <param name="y">起点 Y。</param>
    /// <param name="width">宽度。</param>
    /// <param name="height">高度。</param>
    CaptureOutcome CaptureRegion(int x, int y, int width, int height);

    /// <summary>
    /// 抓取指定窗口画面并保存为 PNG。
    /// </summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    CaptureOutcome CaptureWindow(IntPtr hWnd);

    /// <summary>
    /// 枚举可见的顶级窗口。
    /// </summary>
    IReadOnlyList<WindowInfo> ListTopLevelWindows();
}

/// <summary>
/// 截图结果。
/// </summary>
public sealed record CaptureOutcome
{
    /// <summary>是否成功。</summary>
    public required bool Success { get; init; }

    /// <summary>保存的文件路径。</summary>
    public string? FilePath { get; init; }

    /// <summary>位图宽度。</summary>
    public int Width { get; init; }

    /// <summary>位图高度。</summary>
    public int Height { get; init; }

    /// <summary>耗时。</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>实际使用的捕获模式。</summary>
    public CaptureMode Mode { get; init; }

    /// <summary>失败原因。</summary>
    public string? ErrorMessage { get; init; }
}