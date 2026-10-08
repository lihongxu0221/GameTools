using GameTools.Core.Enums;

namespace GameTools.Core.Models;

/// <summary>
/// 目标窗口详细信息。
/// </summary>
/// <param name="Handle">窗口句柄。</param>
/// <param name="Title">窗口标题。</param>
/// <param name="ClassName">窗口类名。</param>
/// <param name="Bounds">窗口边界矩形。</param>
/// <param name="ProcessId">所属进程 ID。</param>
/// <param name="ProcessName">所属进程名。</param>
/// <param name="IsVisible">窗口是否可见。</param>
/// <param name="IsMinimized">窗口是否最小化。</param>
public sealed record WindowInfo(
    IntPtr Handle,
    string Title,
    string ClassName,
    CaptureBounds Bounds,
    int ProcessId,
    string ProcessName,
    bool IsVisible,
    bool IsMinimized);