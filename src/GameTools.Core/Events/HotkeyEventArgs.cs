using System.Windows.Forms;
using GameTools.Core.Enums;

namespace GameTools.Core.Events;

/// <summary>
/// 快捷键触发事件参数
/// </summary>
public sealed class HotkeyEventArgs : EventArgs
{
    public int HotkeyId { get; }
    public Keys Key { get; }
    public KeyModifiers Modifiers { get; }
    public DateTime Timestamp { get; }

    public HotkeyEventArgs(int hotkeyId, Keys key, KeyModifiers modifiers)
    {
        HotkeyId = hotkeyId;
        Key = key;
        Modifiers = modifiers;
        Timestamp = DateTime.Now;
    }
}
