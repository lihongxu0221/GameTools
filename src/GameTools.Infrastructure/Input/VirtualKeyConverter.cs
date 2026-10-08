using System.Windows.Forms;
using GameTools.Core.Enums;

namespace GameTools.Infrastructure.Input;

/// <summary>
/// <see cref="VirtualKey"/> 与 WinForms <see cref="Keys"/> 的映射。
/// </summary>
/// <remarks>
/// 两者的低位数值一致（均为 Win32 虚拟键码），但高位修饰位语义不同：
/// <see cref="Keys"/> 用 0x01000000 表示 Shift、0x02000000 表示 Control、0x04000000 表示 Alt。
/// 因此不能简单强转：必须先剥离高位，再与低位组合。
/// </remarks>
internal static class VirtualKeyConverter
{
    private const Keys ShiftBit = (Keys)0x00010000;
    private const Keys ControlBit = (Keys)0x00020000;
    private const Keys AltBit = (Keys)0x00040000;

    private const uint HighBitsMask = 0xFFFF0000u;

    /// <summary>
    /// 将平台无关虚拟键转换为 WinForms 键值。
    /// </summary>
    /// <param name="key">源虚拟键。</param>
    public static Keys ToKeys(VirtualKey key) => (Keys)(ushort)key;

    /// <summary>
    /// 将 WinForms 键值转换为平台无关虚拟键。
    /// </summary>
    /// <param name="keys">源键值；高位修饰位会被剥离。</param>
    public static VirtualKey ToVirtualKey(Keys keys)
    {
        int value = unchecked((int)keys) & unchecked((int)HighBitsMask);
        return (VirtualKey)(ushort)value;
    }

    /// <summary>
    /// 判断 WinForms 键值是否携带 Shift 修饰位。
    /// </summary>
    /// <param name="keys">键值。</param>
    public static bool HasShift(Keys keys) => (keys & ShiftBit) == ShiftBit;

    /// <summary>
    /// 判断 WinForms 键值是否携带 Control 修饰位。
    /// </summary>
    /// <param name="keys">键值。</param>
    public static bool HasControl(Keys keys) => (keys & ControlBit) == ControlBit;

    /// <summary>
    /// 判断 WinForms 键值是否携带 Alt 修饰位。
    /// </summary>
    /// <param name="keys">键值。</param>
    public static bool HasAlt(Keys keys) => (keys & AltBit) == AltBit;

    /// <summary>
    /// 剥离高位修饰位，仅保留低位虚拟键码。
    /// </summary>
    /// <param name="keys">键值。</param>
    public static Keys StripModifiers(Keys keys) => (Keys)((int)keys & ~HighBitsMask);
}