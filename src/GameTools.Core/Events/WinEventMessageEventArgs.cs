namespace GameTools.Core.Events;

/// <summary>
/// 目标进程 WinEvent 窗口状态事件参数
/// </summary>
public sealed class WinEventMessageEventArgs : EventArgs
{
    public uint EventType { get; }
    public string EventName { get; }
    public IntPtr WindowHandle { get; }
    public int ProcessId { get; }
    public int ThreadId { get; }
    public long ObjectId { get; }
    public long ChildId { get; }
    public DateTime Timestamp { get; }

    public WinEventMessageEventArgs(
        uint eventType,
        string eventName,
        IntPtr windowHandle,
        int processId,
        int threadId,
        long objectId,
        long childId)
    {
        EventType = eventType;
        EventName = eventName;
        WindowHandle = windowHandle;
        ProcessId = processId;
        ThreadId = threadId;
        ObjectId = objectId;
        ChildId = childId;
        Timestamp = DateTime.Now;
    }
}
