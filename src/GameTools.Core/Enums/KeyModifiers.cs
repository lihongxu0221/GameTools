namespace GameTools.Core.Enums;

/// <summary>
/// 快捷键修饰键枚举（兼容 Win32 MOD_* 定义）
/// </summary>
[Flags]
public enum KeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
    NoRepeat = 0x4000
}
