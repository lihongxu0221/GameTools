using GameTools.Core.Enums;

namespace GameTools.Core.Events;

/// <summary>
/// 快捷键触发事件参数。
/// </summary>
public sealed class HotkeyEventArgs : EventArgs
{
    /// <summary>快捷键 ID。</summary>
    public int HotkeyId { get; }

    /// <summary>触发的虚拟按键。</summary>
    public VirtualKey Key { get; }

    /// <summary>触发时的修饰键组。</summary>
    public KeyModifiers Modifiers { get; }

    /// <summary>触发时间。</summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// 初始化事件参数。
    /// </summary>
    /// <param name="hotkeyId">快捷键 ID。</param>
    /// <param name="key">触发的虚拟按键。</param>
    /// <param name="modifiers">修饰键组。</param>
    public HotkeyEventArgs(int hotkeyId, VirtualKey key, KeyModifiers modifiers)
    {
        HotkeyId = hotkeyId;
        Key = key;
        Modifiers = modifiers;
        Timestamp = DateTime.Now;
    }
}