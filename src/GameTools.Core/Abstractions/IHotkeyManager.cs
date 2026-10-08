using GameTools.Core.Enums;
using GameTools.Core.Events;

namespace GameTools.Core.Abstractions;

/// <summary>
/// 全局快捷键管理器接口。
/// </summary>
public interface IHotkeyManager : IDisposable
{
    /// <summary>
    /// 当任意已注册的快捷键被触发时引发。
    /// </summary>
    event EventHandler<HotkeyEventArgs>? HotkeyTriggered;

    /// <summary>
    /// 注册一个全局快捷键。
    /// </summary>
    /// <param name="key">虚拟按键。</param>
    /// <param name="modifiers">修饰键组。</param>
    /// <param name="callback">可选的专属异步回调。</param>
    /// <returns>分配的热键唯一标识。</returns>
    int RegisterHotkey(VirtualKey key, KeyModifiers modifiers, Action? callback = null);

    /// <summary>
    /// 注销指定 ID 的快捷键。
    /// </summary>
    /// <param name="hotkeyId">快捷键 ID。</param>
    /// <returns>是否注销成功。</returns>
    bool UnregisterHotkey(int hotkeyId);

    /// <summary>
    /// 注销所有已注册的快捷键。
    /// </summary>
    void UnregisterAll();
}