using System.Drawing;

namespace GameTools.Core.Models;

/// <summary>
/// 目标窗口详细信息
/// </summary>
public record WindowInfo
{
    public required IntPtr Handle { get; init; }
    public required string Title { get; init; }
    public required string ClassName { get; init; }
    public required Rectangle Bounds { get; init; }
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public bool IsVisible { get; init; }
    public bool IsMinimized { get; init; }
}
