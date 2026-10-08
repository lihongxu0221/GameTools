using System.Drawing;

namespace GameTools.Core.Events;

/// <summary>
/// 低级鼠标钩子事件参数
/// </summary>
public sealed class MouseHookEventArgs : EventArgs
{
    public uint Message { get; }
    public Point Location { get; }
    public int MouseData { get; }
    public IntPtr ForegroundWindow { get; }
    public int ProcessId { get; }
    public string ProcessName { get; }
    public bool IsTargetProcess { get; }
    public bool Handled { get; set; }
    public DateTime Timestamp { get; }

    public MouseHookEventArgs(
        uint message,
        Point location,
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
