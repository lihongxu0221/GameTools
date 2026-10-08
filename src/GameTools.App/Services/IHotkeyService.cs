using GameTools.Core.Enums;

namespace GameTools.App.Services;

/// <summary>
/// 快捷键能力的服务接口。
/// </summary>
public interface IHotkeyService
{
    /// <summary>
    /// 注册全局快捷键。
    /// </summary>
    /// <param name="key">主键。</param>
    /// <param name="modifiers">修饰键。</param>
    /// <param name="action">触发后要执行的命令文本。</param>
    HotkeyRegistrationResult Register(VirtualKey key, KeyModifiers modifiers, string action);

    /// <summary>
    /// 注销指定快捷键。
    /// </summary>
    /// <param name="id">快捷键 ID。</param>
    /// <returns>是否注销成功。</returns>
    bool Unregister(int id);

    /// <summary>
    /// 注销全部快捷键。
    /// </summary>
    void UnregisterAll();
}

/// <summary>
/// 快捷键注册结果。
/// </summary>
public sealed record HotkeyRegistrationResult
{
    /// <summary>是否注册成功。</summary>
    public required bool Success { get; init; }

    /// <summary>分配到的快捷键 ID。</summary>
    public int Id { get; init; }

    /// <summary>失败原因。</summary>
    public string? ErrorMessage { get; init; }
}
