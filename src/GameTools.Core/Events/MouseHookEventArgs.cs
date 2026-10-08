using GameTools.Core.Models;

namespace GameTools.Core.Events;

/// <summary>
/// 低级鼠标钩子事件参数。
/// </summary>
public sealed class MouseHookEventArgs : EventArgs
{
    /// <summary>原生鼠标消息标识。</summary>
    public uint Message { get; }

    /// <summary>事件发生时鼠标所在屏幕坐标。</summary>
    public CaptureBounds Location { get; }

    /// <summary>滚轮增量或按键号等附加数据。</summary>
    public int MouseData { get; }

    /// <summary>事件发生时的前台窗口句柄。</summary>
    public IntPtr ForegroundWindow { get; }

    /// <summary>事件发生时前台窗口所属进程 ID。</summary>
    public int ProcessId { get; }

    /// <summary>事件发生时前台窗口所属进程名。</summary>
    public string ProcessName { get; }

    /// <summary>是否命中配置的过滤目标。</summary>
    public bool IsTargetProcess { get; }

    /// <summary>事件时间戳。</summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// 初始化事件参数。
    /// </summary>
    /// <param name="message">原生鼠标消息标识。</param>
    /// <param name="location">鼠标屏幕坐标。</param>
    /// <param name="mouseData">附加数据。</param>
    /// <param name="foregroundWindow">前台窗口句柄。</param>
    /// <param name="processId">前台进程 ID。</param>
    /// <param name="processName">前台进程名。</param>
    /// <param name="isTargetProcess">是否命中过滤目标。</param>
    public MouseHookEventArgs(
        uint message,
        CaptureBounds location,
        int mouseData,
        IntPtr foregroundWindow,
        int processId,
        string processName,
        bool isTargetProcess)
    {
        Message = message;
        Location = location;
        MouseData = mouseData;
        ForegroundWindow = foregroundWindow;
        ProcessId = processId;
        ProcessName = processName;
        IsTargetProcess = isTargetProcess;
        Timestamp = DateTime.Now;
    }
}