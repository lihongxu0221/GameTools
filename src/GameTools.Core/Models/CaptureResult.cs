using System.Drawing;
using GameTools.Core.Enums;

namespace GameTools.Core.Models;

/// <summary>
/// 截图捕获结果
/// </summary>
public sealed class CaptureResult : IDisposable
{
    public bool Success { get; init; }
    public Bitmap? Image { get; init; }
    public Rectangle Bounds { get; init; }
    public CaptureMode Mode { get; init; }
    public TimeSpan Elapsed { get; init; }
    public string? ErrorMessage { get; init; }

    public void Dispose()
    {
        Image?.Dispose();
    }
}
