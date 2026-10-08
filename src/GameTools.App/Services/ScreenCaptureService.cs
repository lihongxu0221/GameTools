using System.Diagnostics;
using System.IO;
using GameTools.Core.Abstractions;
using GameTools.Core.Models;
using GameTools.Infrastructure.Capture;
using GameTools.Win32.Helpers;

namespace GameTools.App.Services;

/// <summary>
/// 基于 <see cref="IScreenCapture"/> 的截图服务实现。
/// </summary>
/// <remarks>
/// 职责边界：库层只负责产出 <see cref="CaptureFrame"/> 与失败原因；
/// 保存路径、文件名与目录管理属于应用层职责，由本类负责，
/// 从而保持 Infrastructure 不感知文件系统布局。
/// </remarks>
public sealed class ScreenCaptureService : IScreenCaptureService
{
    private const string ScreenshotFolderName = "Screenshots";

    private readonly IScreenCapture _capture;

    /// <summary>
    /// 初始化截图服务。
    /// </summary>
    /// <param name="capture">底层截图实现。</param>
    public ScreenCaptureService(IScreenCapture capture)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
    }

    /// <inheritdoc />
    public CaptureOutcome CaptureFullScreen(bool allMonitors)
    {
        using CaptureResult result = _capture.CaptureFullScreen(allMonitors);
        return Persist(result);
    }

    /// <inheritdoc />
    public CaptureOutcome CaptureRegion(int x, int y, int width, int height)
    {
        using CaptureResult result = _capture.CaptureRegion(new CaptureBounds(x, y, width, height));
        return Persist(result);
    }

    /// <inheritdoc />
    public CaptureOutcome CaptureWindow(IntPtr hWnd)
    {
        using CaptureResult result = _capture.CaptureWindow(hWnd);
        return Persist(result);
    }

    /// <inheritdoc />
    public IReadOnlyList<WindowInfo> ListTopLevelWindows() => WindowHelper.FindTopLevelWindows();

    private static CaptureOutcome Persist(CaptureResult result)
    {
        if (!result.Success || result.Frame == null)
        {
            return new CaptureOutcome
            {
                Success = false,
                Elapsed = result.Elapsed,
                Mode = result.Mode,
                ErrorMessage = string.IsNullOrEmpty(result.ErrorMessage) ? "未返回画面帧" : result.ErrorMessage
            };
        }

        try
        {
            string directory = Path.Combine(AppContext.BaseDirectory, ScreenshotFolderName);
            Directory.CreateDirectory(directory);

            string fileName = $"Capture_{result.Mode}_{DateTime.Now:yyyyMMdd_HHmmssfff}.png";
            string path = Path.Combine(directory, fileName);

            using System.Drawing.Bitmap bitmap = CaptureFrameConverter.ToBitmap(result.Frame);
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);

            return new CaptureOutcome
            {
                Success = true,
                FilePath = path,
                Width = result.Frame.Width,
                Height = result.Frame.Height,
                Elapsed = result.Elapsed,
                Mode = result.Mode,
                ErrorMessage = result.ErrorMessage
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            // 磁盘写入失败需转换为可展示的失败结果，避免异常穿透到 UI
            return new CaptureOutcome
            {
                Success = false,
                Elapsed = result.Elapsed,
                Mode = result.Mode,
                ErrorMessage = $"保存失败：{ex.Message}"
            };
        }
    }
}