using GameTools.Core.Enums;

namespace GameTools.Core.Events;

/// <summary>
/// 低级键盘钩子事件参数。
/// </summary>
public sealed class KeyboardHookEventArgs : EventArgs
{
    /// <summary>触发的虚拟按键。</summary>
    public VirtualKey Key { get; }

    /// <summary>虚拟键码原始数值。</summary>
    public int KeyCode { get; }

    /// <summary>是否为按下事件（false 表示抬起）。</summary>
    public bool IsKeyDown { get; }

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
    /// <param name="key">触发的虚拟按键。</param>
    /// <param name="keyCode">虚拟键码原始数值。</param>
    /// <param name="isKeyDown">是否为按下事件。</param>
    /// <param name="foregroundWindow">前台窗口句柄。</param>
    /// <param name="processId">前台进程 ID。</param>
    /// <param name="processName">前台进程名。</param>
    /// <param name="isTargetProcess">是否命中过滤目标。</param>
    public KeyboardHookEventArgs(
        VirtualKey key,
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