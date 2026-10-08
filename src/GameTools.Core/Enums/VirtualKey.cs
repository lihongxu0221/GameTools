namespace GameTools.Core.Enums;

/// <summary>
/// 平台无关的虚拟键码。
/// </summary>
/// <remarks>
/// 取值与 Win32 虚拟键码（VK_*）一致，但不使用 <c>System.Windows.Forms.Keys</c>，
/// 从而使契约层不依赖 WinForms。基础设施层负责与 <c>Keys</c> 互转。
/// </remarks>
public enum VirtualKey : ushort
{
    /// <summary>退格。</summary>
    Back = 0x08,

    /// <summary>Tab。</summary>
    Tab = 0x09,

    /// <summary>回车。</summary>
    Enter = 0x0D,

    /// <summary>Shift。</summary>
    Shift = 0x10,

    /// <summary>Control。</summary>
    Control = 0x11,

    /// <summary>Alt / Menu。</summary>
    Alt = 0x12,

    /// <summary>暂停。</summary>
    Pause = 0x13,

    /// <summary>大写锁定。</summary>
    CapsLock = 0x14,

    /// <summary>Esc。</summary>
    Escape = 0x1B,

    /// <summary>空格。</summary>
    Space = 0x20,

    /// <summary>页上键。</summary>
    PageUp = 0x21,

    /// <summary>页下键。</summary>
    PageDown = 0x22,

    /// <summary>End。</summary>
    End = 0x23,

    /// <summary>Home。</summary>
    Home = 0x24,

    /// <summary>左方向键。</summary>
    Left = 0x25,

    /// <summary>上方向键。</summary>
    Up = 0x26,

    /// <summary>右方向键。</summary>
    Right = 0x27,

    /// <summary>下方向键。</summary>
    Down = 0x28,

    /// <summary>数字键 0。</summary>
    D0 = 0x30,

    /// <summary>数字键 1。</summary>
    D1 = 0x31,

    /// <summary>数字键 2。</summary>
    D2 = 0x32,

    /// <summary>数字键 3。</summary>
    D3 = 0x33,

    /// <summary>数字键 4。</summary>
    D4 = 0x34,

    /// <summary>数字键 5。</summary>
    D5 = 0x35,

    /// <summary>数字键 6。</summary>
    D6 = 0x36,

    /// <summary>数字键 7。</summary>
    D7 = 0x37,

    /// <summary>数字键 8。</summary>
    D8 = 0x38,

    /// <summary>数字键 9。</summary>
    D9 = 0x39,

    /// <summary>字母 A。</summary>
    A = 0x41,

    /// <summary>字母 B。</summary>
    B = 0x42,

    /// <summary>字母 C。</summary>
    C = 0x43,

    /// <summary>字母 D。</summary>
    D = 0x44,

    /// <summary>字母 E。</summary>
    E = 0x45,

    /// <summary>字母 F。</summary>
    F = 0x46,

    /// <summary>字母 G。</summary>
    G = 0x47,

    /// <summary>字母 H。</summary>
    H = 0x48,

    /// <summary>字母 I。</summary>
    I = 0x49,

    /// <summary>字母 J。</summary>
    J = 0x4A,

    /// <summary>字母 K。</summary>
    K = 0x4B,

    /// <summary>字母 L。</summary>
    L = 0x4C,

    /// <summary>字母 M。</summary>
    M = 0x4D,

    /// <summary>字母 N。</summary>
    N = 0x4E,

    /// <summary>字母 O。</summary>
    O = 0x4F,

    /// <summary>字母 P。</summary>
    P = 0x50,

    /// <summary>字母 Q。</summary>
    Q = 0x51,

    /// <summary>字母 R。</summary>
    R = 0x52,

    /// <summary>字母 S。</summary>
    S = 0x53,

    /// <summary>字母 T。</summary>
    T = 0x54,

    /// <summary>字母 U。</summary>
    U = 0x55,

    /// <summary>字母 V。</summary>
    V = 0x56,

    /// <summary>字母 W。</summary>
    W = 0x57,

    /// <summary>字母 X。</summary>
    X = 0x58,

    /// <summary>字母 Y。</summary>
    Y = 0x59,

    /// <summary>字母 Z。</summary>
    Z = 0x5A,

    /// <summary>数字区 0。</summary>
    NumPad0 = 0x60,

    /// <summary>数字区 1。</summary>
    NumPad1 = 0x61,

    /// <summary>数字区 2。</summary>
    NumPad2 = 0x62,

    /// <summary>数字区 3。</summary>
    NumPad3 = 0x63,

    /// <summary>数字区 4。</summary>
    NumPad4 = 0x64,

    /// <summary>数字区 5。</summary>
    NumPad5 = 0x65,

    /// <summary>数字区 6。</summary>
    NumPad6 = 0x66,

    /// <summary>数字区 7。</summary>
    NumPad7 = 0x67,

    /// <summary>数字区 8。</summary>
    NumPad8 = 0x68,

    /// <summary>数字区 9。</summary>
    NumPad9 = 0x69,

    /// <summary>数字区乘号。</summary>
    Multiply = 0x6A,

    /// <summary>数字区加号。</summary>
    Add = 0x6B,

    /// <summary>数字区减号。</summary>
    Subtract = 0x6D,

    /// <summary>数字区小数点。</summary>
    Decimal = 0x6E,

    /// <summary>数字区分隔符。</summary>
    Divide = 0x6F,

    /// <summary>F1。</summary>
    F1 = 0x70,

    /// <summary>F2。</summary>
    F2 = 0x71,

    /// <summary>F3。</summary>
    F3 = 0x72,

    /// <summary>F4。</summary>
    F4 = 0x73,

    /// <summary>F5。</summary>
    F5 = 0x74,

    /// <summary>F6。</summary>
    F6 = 0x75,

    /// <summary>F7。</summary>
    F7 = 0x76,

    /// <summary>F8。</summary>
    F8 = 0x77,

    /// <summary>F9。</summary>
    F9 = 0x78,

    /// <summary>F10。</summary>
    F10 = 0x79,

    /// <summary>F11。</summary>
    F11 = 0x7A,

    /// <summary>F12。</summary>
    F12 = 0x7B,

    /// <summary>数字锁定。</summary>
    NumLock = 0x90,

    /// <summary>滚动锁定。</summary>
    Scroll = 0x91,

    /// <summary>左 Shift。</summary>
    LShift = 0xA0,

    /// <summary>右 Shift。</summary>
    RShift = 0xA1,

    /// <summary>左 Control。</summary>
    LControl = 0xA2,

    /// <summary>右 Control。</summary>
    RControl = 0xA3,

    /// <summary>左 Alt。</summary>
    LAlt = 0xA4,

    /// <summary>右 Alt。</summary>
    RAlt = 0xA5,

    /// <summary>左 Win。</summary>
    LWin = 0x5B,

    /// <summary>右 Win。</summary>
    RWin = 0x5C,

    /// <summary>PrintScreen。</summary>
    PrintScreen = 0x2C,

    /// <summary>Insert。</summary>
    Insert = 0x2D,

    /// <summary>Delete。</summary>
    Delete = 0x2E
}