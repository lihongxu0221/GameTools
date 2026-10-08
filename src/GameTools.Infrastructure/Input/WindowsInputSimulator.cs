using System.Runtime.InteropServices;
using System.Windows.Forms;
using GameTools.Core.Enums;
using GameTools.Core.Abstractions;
using GameTools.Win32.Native;

namespace GameTools.Infrastructure.Input;

/// <summary>
/// 文本与按键模拟输入实现。
/// 包含前台 SendInput（支持 Unicode 与代理对）与后台定向 PostMessage / SendMessage。
/// </summary>
/// <remarks>
/// 关键约束：
/// 1. <c>KEYEVENTF_UNICODE</c> 的 <c>wScan</c> 是 UTF-16 码元，非 BMP 字符（Emoji）必须
///    以「低代理项 Down、高代理项 Down、低代理项 Up、高代理项 Up」成组投递，且组内不得插入延迟，
///    否则目标应用会丢弃孤立代理项并输出替换字符；
/// 2. 所有 <c>SendInput</c> / <c>PostMessage</c> 均检查返回值：返回 0 表示被 UIPI 拒绝或数组被锁定，
///    静默忽略会让上层误判为成功；
/// 3. 后台按键补齐 <c>lParam</c> 的重复计数与按下状态位，并额外发送 <c>WM_CHAR</c>，
///    否则多数控件无法产生正确按键序列；
/// 4. 跨进程写文本改用 <c>SendMessageTimeout</c>，避免目标进程挂起导致调用方永久阻塞。
/// </remarks>
public sealed class WindowsInputSimulator : ITextInputSimulator
{
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const uint KeyEventExtendedKey = 0x0001;

    private const int SendMessageTimeoutMs = 2000;

    // Random.Shared 在 .NET Framework 上不可用；Random 实例非线程安全，故用 ThreadLocal 包装
    private static readonly ThreadLocal<Random> _randomHolder = new(() => new Random(Guid.NewGuid().GetHashCode()));

    /// <inheritdoc />
    public void SendText(string text, int delayBetweenCharsMs = 10, int jitterMs = 0, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (delayBetweenCharsMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delayBetweenCharsMs), delayBetweenCharsMs, "字符间隔不能为负数。");
        }

        if (jitterMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(jitterMs), jitterMs, "随机抖动不能为负数。");
        }

        int index = 0;
        while (index < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 识别代理对：必须与后续码元成组投递
            if (char.IsHighSurrogate(text[index]) &&
                index + 1 < text.Length &&
                char.IsLowSurrogate(text[index + 1]))
            {
                SendSurrogatePair(text[index], text[index + 1]);
                index += 2;
            }
            else
            {
                SendCharUnicode(text[index]);
                index++;
            }

            // 组间才插入延迟，组内绝不延迟
            if (index >= text.Length || delayBetweenCharsMs <= 0)
            {
                continue;
            }

            int delay = ApplyJitter(delayBetweenCharsMs, jitterMs);
            if (delay > 0)
            {
                if (cancellationToken.WaitHandle.WaitOne(delay))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }
    }

    /// <inheritdoc />
    public void SendKeyPress(VirtualKey key, KeyModifiers modifiers = KeyModifiers.None)
    {
        List<Keys> modifierKeys = GetModifierKeyList(modifiers);

        // 修饰键按下失败时必须补偿已按下的键，避免修饰键处于粘连状态
        var pressed = new List<Keys>();
        try
        {
            foreach (Keys modifierKey in modifierKeys)
            {
                SendSingleKey(modifierKey, isKeyUp: false);
                pressed.Add(modifierKey);
            }

            SendSingleKey(VirtualKeyConverter.ToKeys(key), isKeyUp: false);
            Thread.Sleep(5);
            SendSingleKey(VirtualKeyConverter.ToKeys(key), isKeyUp: true);
        }
        finally
        {
            // 逆序释放已按下的修饰键
            for (int i = pressed.Count - 1; i >= 0; i--)
            {
                SendSingleKey(pressed[i], isKeyUp: true);
            }
        }
    }

    /// <inheritdoc />
    public void PostTextToWindow(IntPtr hWnd, string text, int delayBetweenCharsMs = 10, CancellationToken cancellationToken = default)
    {
        if (hWnd == IntPtr.Zero || string.IsNullOrEmpty(text))
        {
            return;
        }

        int index = 0;
        while (index < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (char.IsHighSurrogate(text[index]) &&
                index + 1 < text.Length &&
                char.IsLowSurrogate(text[index + 1]))
            {
                // 代理对必须成组投递，否则目标窗口收到孤立代理项
                PostCharWithKeySequence(hWnd, text[index], isUnicode: true);
                PostCharWithKeySequence(hWnd, text[index + 1], isUnicode: true);
                index += 2;
            }
            else
            {
                PostCharWithKeySequence(hWnd, text[index], isUnicode: text[index] >= 0x80);
                index++;
            }

            if (index >= text.Length || delayBetweenCharsMs <= 0)
            {
                continue;
            }

            if (cancellationToken.WaitHandle.WaitOne(delayBetweenCharsMs))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    /// <inheritdoc />
    public bool SetWindowText(IntPtr hWnd, string text)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        // 跨进程同步调用必须设超时：目标进程挂起时不得让调用方无限等待
        IntPtr result = User32.SendMessageTimeout(
            hWnd,
            NativeConstants.WM_SETTEXT,
            IntPtr.Zero,
            text ?? string.Empty,
            NativeConstants.SMTO_ABORTIFHUNG | NativeConstants.SMTO_BLOCK,
            SendMessageTimeoutMs,
            out _);

        return result != IntPtr.Zero;
    }

    /// <inheritdoc />
    public void PostKeyPressToWindow(IntPtr hWnd, VirtualKey key)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        // 构造符合 Win32 约定的 lParam：重复次数 1，第 30 位标记按下，第 31 位标记扩展键
        uint vk = (uint)VirtualKeyConverter.ToKeys(key);
        uint lParamDown = 1u | (1u << 30);
        uint lParamUp = 1u | (1u << 30) | (1u << 31);

        if (IsExtendedKey(vk))
        {
            lParamDown |= 1u << 24;
            lParamUp |= 1u << 24;
        }

        if (!User32.PostMessage(hWnd, NativeConstants.WM_KEYDOWN, new IntPtr(vk), new IntPtr(unchecked((int)lParamDown))))
        {
            throw new InvalidOperationException(
                $"向窗口 0x{hWnd.ToInt64():X} 投递 WM_KEYDOWN 失败（错误码 {Marshal.GetLastWin32Error()}），目标可能已销毁或被 UIPI 拒绝。");
        }

        Thread.Sleep(5);

        if (!User32.PostMessage(hWnd, NativeConstants.WM_KEYUP, new IntPtr(vk), new IntPtr(unchecked((int)lParamUp))))
        {
            throw new InvalidOperationException(
                $"向窗口 0x{hWnd.ToInt64():X} 投递 WM_KEYUP 失败（错误码 {Marshal.GetLastWin32Error()}），可能只按下未抬起。");
        }

        // 补发 WM_CHAR：多数控件依赖该消息完成实际字符输入
        char ch = MapVirtualKeyToChar(vk);
        if (ch != '\0')
        {
            User32.PostMessage(hWnd, NativeConstants.WM_CHAR, new IntPtr(ch), new IntPtr(unchecked((int)lParamUp)));
        }
    }

    private static int ApplyJitter(int baseDelay, int jitterMs)
    {
        if (jitterMs <= 0)
        {
            return baseDelay;
        }

        int delta = _randomHolder.Value!.Next(-jitterMs, jitterMs + 1);
        int actual = baseDelay + delta;
        return actual < 0 ? 0 : actual;
    }

    private static void SendCharUnicode(char ch)
    {
        var inputs = new INPUT[2];

        inputs[0] = new INPUT
        {
            Type = NativeConstants.INPUT_KEYBOARD,
            Data = new InputUnion
            {
                Keyboard = new KEYBDINPUT
                {
                    Vk = 0,
                    Scan = ch,
                    Flags = KeyEventUnicode,
                    Time = 0,
                    ExtraInfo = UIntPtr.Zero
                }
            }
        };

        inputs[1] = new INPUT
        {
            Type = NativeConstants.INPUT_KEYBOARD,
            Data = new InputUnion
            {
                Keyboard = new KEYBDINPUT
                {
                    Vk = 0,
                    Scan = ch,
                    Flags = KeyEventUnicode | KeyEventKeyUp,
                    Time = 0,
                    ExtraInfo = UIntPtr.Zero
                }
            }
        };

        SendInputs(inputs);
    }

    /// <summary>
    /// 以代理对形式投递一个非 BMP 码元。
    /// 顺序为「低代理项 Down、高代理项 Down、低代理项 Up、高代理项 Up」，
    /// 与 Windows 对 UTF-16 代理序列的组装顺序一致。
    /// </summary>
    private static void SendSurrogatePair(char low, char high)
    {
        SendUnicodeSequence(new[] { low, high, low, high });
    }

    private static void SendUnicodeSequence(char[] sequence)
    {
        var inputs = new INPUT[sequence.Length];

        for (int i = 0; i < sequence.Length; i++)
        {
            uint flags = KeyEventUnicode;
            if (i >= sequence.Length / 2)
            {
                flags |= KeyEventKeyUp;
            }

            inputs[i] = new INPUT
            {
                Type = NativeConstants.INPUT_KEYBOARD,
                Data = new InputUnion
                {
                    Keyboard = new KEYBDINPUT
                    {
                        Vk = 0,
                        Scan = sequence[i],
                        Flags = flags,
                        Time = 0,
                        ExtraInfo = UIntPtr.Zero
                    }
                }
            };
        }

        SendInputs(inputs);
    }

    private static void SendSingleKey(Keys key, bool isKeyUp)
    {
        uint flags = isKeyUp ? KeyEventKeyUp : 0u;
        uint vk = (uint)key;

        if (IsExtendedKey(vk))
        {
            flags |= KeyEventExtendedKey;
        }

        var inputs = new INPUT[1];

        inputs[0] = new INPUT
        {
            Type = NativeConstants.INPUT_KEYBOARD,
            Data = new InputUnion
            {
                Keyboard = new KEYBDINPUT
                {
                    Vk = (ushort)key,
                    Scan = 0,
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = UIntPtr.Zero
                }
            }
        };

        SendInputs(inputs);
    }

    private static void SendInputs(INPUT[] inputs)
    {
        uint inserted = User32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

        if (inserted != inputs.Length)
        {
            // 0 表示被 UIPI 完整性级别阻止，或数组被锁定；必须让上层知晓
            throw new InvalidOperationException(
                $"SendInput 仅插入 {inserted}/{inputs.Length} 个事件（错误码 {Marshal.GetLastWin32Error()}）。目标窗口完整性级别高于本进程时会被 UIPI 阻止。");
        }
    }

    private static void PostCharWithKeySequence(IntPtr hWnd, char ch, bool isUnicode)
    {
        // 对非 ASCII 字符，WM_KEYDOWN 的 wParam 传 0 并由 WM_CHAR 携带字符
        uint vk = isUnicode ? 0u : (uint)ch;
        uint lParamDown = 1u | (1u << 30);
        uint lParamUp = 1u | (1u << 30) | (1u << 31);

        if (ch == '\r')
        {
            User32.PostMessage(hWnd, NativeConstants.WM_KEYDOWN, new IntPtr(NativeConstants.VK_RETURN), new IntPtr(unchecked((int)lParamDown)));
            User32.PostMessage(hWnd, NativeConstants.WM_CHAR, new IntPtr('\r'), new IntPtr(unchecked((int)lParamUp)));
            User32.PostMessage(hWnd, NativeConstants.WM_KEYUP, new IntPtr(NativeConstants.VK_RETURN), new IntPtr(unchecked((int)lParamUp)));
            return;
        }

        if (ch == '\n')
        {
            User32.PostMessage(hWnd, NativeConstants.WM_KEYDOWN, new IntPtr(NativeConstants.VK_RETURN), new IntPtr(unchecked((int)lParamDown)));
            User32.PostMessage(hWnd, NativeConstants.WM_CHAR, new IntPtr('\n'), new IntPtr(unchecked((int)lParamUp)));
            User32.PostMessage(hWnd, NativeConstants.WM_KEYUP, new IntPtr(NativeConstants.VK_RETURN), new IntPtr(unchecked((int)lParamUp)));
            return;
        }

        User32.PostMessage(hWnd, NativeConstants.WM_KEYDOWN, new IntPtr(unchecked((int)vk)), new IntPtr(unchecked((int)lParamDown)));
        User32.PostMessage(hWnd, NativeConstants.WM_CHAR, new IntPtr(ch), new IntPtr(unchecked((int)lParamUp)));
        User32.PostMessage(hWnd, NativeConstants.WM_KEYUP, new IntPtr(unchecked((int)vk)), new IntPtr(unchecked((int)lParamUp)));
    }

    private static bool IsExtendedKey(uint vk)
    {
        // Win32 扩展键集合：方向键、Insert/Delete/Home/End/PageUp/PageEnd、数字区导航、右 Ctrl/Alt、Win 键
        return vk is (>= 0x21 and <= 0x2E) or 0x6F or 0x7A or 0x7B or 0x7C or 0x7D or 0x7E or 0x7F or 0x90 or 0xA3 or 0xA4 or 0xA5 or 0xA6 or 0xAD;
    }

    private static char MapVirtualKeyToChar(uint vk)
    {
        return vk switch
        {
            0x0D => '\r',
            0x20 => ' ',
            0x08 => '\b',
            0x09 => '\t',
            0x1B => (char)0x1B,
            _ => '\0'
        };
    }

    private static List<Keys> GetModifierKeyList(KeyModifiers modifiers)
    {
        var list = new List<Keys>();
        if ((modifiers & KeyModifiers.Control) == KeyModifiers.Control)
        {
            list.Add(Keys.ControlKey);
        }

        if ((modifiers & KeyModifiers.Alt) == KeyModifiers.Alt)
        {
            list.Add(Keys.Menu);
        }

        if ((modifiers & KeyModifiers.Shift) == KeyModifiers.Shift)
        {
            list.Add(Keys.ShiftKey);
        }

        if ((modifiers & KeyModifiers.Windows) == KeyModifiers.Windows)
        {
            list.Add(Keys.LWin);
        }

        return list;
    }
}
