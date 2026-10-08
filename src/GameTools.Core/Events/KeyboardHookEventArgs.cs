using System.Windows.Forms;

namespace GameTools.Core.Events;

/// <summary>
/// 低级键盘钩子事件参数
/// </summary>
public sealed class KeyboardHookEventArgs : EventArgs
{
    public Keys Key { get; }
    public int KeyCode { get; }
    public bool IsKeyDown { get; }
    public IntPtr ForegroundWindow { get; }
    public int ProcessId { get; }
    public string ProcessName { get; }
    public bool IsTargetProcess { get; }
    public bool Handled { get; set; }
    public DateTime Timestamp { get; }

    public KeyboardHookEventArgs(
        Keys key,
        int keyCode,
        bool isKeyDown,
        IntPtr foregroundWindow,
        int processId,
        string processName,
        bool isTargetProcess)
    {
        Key = key;
        KeyCode = keyCode;
        IsKeyDown = isKeyDown;
        ForegroundWindow = foregroundWindow;
        ProcessId = processId;
        ProcessName = processName;
        IsTargetProcess = isTargetProcess;
        Timestamp = DateTime.Now;
    }
}
