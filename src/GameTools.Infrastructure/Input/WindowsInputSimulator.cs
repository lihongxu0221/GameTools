using System.Runtime.InteropServices;
using System.Windows.Forms;
using GameTools.Core.Abstractions;
using GameTools.Core.Enums;
using GameTools.Win32.Native;

namespace GameTools.Infrastructure.Input;

/// <summary>
/// 文本与按键模拟输入实现
/// 包含前台 SendInput (支持 Unicode 与中文直通) 以及后台定向 PostMessage / SendMessage
/// </summary>
public sealed class WindowsInputSimulator : ITextInputSimulator
{
    /// <summary>
    /// 前台模拟输入 Unicode 文本（绕过输入法干扰，直接投递中文字符/多语言）
    /// </summary>
    public void SendText(string text, int delayBetweenCharsMs = 10)
    {
        if (string.IsNullOrEmpty(text)) return;

        foreach (char ch in text)
        {
            SendCharUnicode(ch);
            if (delayBetweenCharsMs > 0)
            {
                Thread.Sleep(delayBetweenCharsMs);
            }
        }
    }

    /// <summary>
    /// 前台模拟单键或组合键按下并弹起
    /// </summary>
    public void SendKeyPress(Keys key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var modifierKeys = GetModifierKeyList(modifiers);

        // 1. 按下次序修饰键
        foreach (var modKey in modifierKeys)
        {
            SendSingleKey(modKey, isKeyUp: false);
        }

        // 2. 按下并抬起目标键
        SendSingleKey(key, isKeyUp: false);
        Thread.Sleep(5);
        SendSingleKey(key, isKeyUp: true);

        // 3. 逆序释放修饰键
        for (int i = modifierKeys.Count - 1; i >= 0; i--)
        {
            SendSingleKey(modifierKeys[i], isKeyUp: true);
        }
    }

    /// <summary>
    /// 向未激活的后台指定窗口定向投递字符（基于 PostMessage WM_CHAR）
    /// </summary>
    public void PostTextToWindow(IntPtr hWnd, string text, int delayBetweenCharsMs = 10)
    {
        if (hWnd == IntPtr.Zero || string.IsNullOrEmpty(text)) return;

        foreach (char ch in text)
        {
            User32.PostMessage(hWnd, NativeConstants.WM_CHAR, (IntPtr)ch, IntPtr.Zero);
            if (delayBetweenCharsMs > 0)
            {
                Thread.Sleep(delayBetweenCharsMs);
            }
        }
    }

    /// <summary>
    /// 向目标窗口的文本框控件发送 WM_SETTEXT 快速写入文本
    /// </summary>
    public bool SetWindowText(IntPtr hWnd, string text)
    {
        if (hWnd == IntPtr.Zero) return false;
        IntPtr result = User32.SendMessage(hWnd, NativeConstants.WM_SETTEXT, IntPtr.Zero, text ?? string.Empty);
        return result != IntPtr.Zero;
    }

    /// <summary>
    /// 向后台窗口投递虚拟按键按下与抬起 (WM_KEYDOWN / WM_KEYUP)
    /// </summary>
    public void PostKeyPressToWindow(IntPtr hWnd, Keys key)
    {
        if (hWnd == IntPtr.Zero) return;

        User32.PostMessage(hWnd, NativeConstants.WM_KEYDOWN, (IntPtr)key, IntPtr.Zero);
        Thread.Sleep(5);
        User32.PostMessage(hWnd, NativeConstants.WM_KEYUP, (IntPtr)key, IntPtr.Zero);
    }

    private static void SendCharUnicode(char ch)
    {
        var inputs = new INPUT[2];

        // Key Down
        inputs[0] = new INPUT
        {
            Type = NativeConstants.INPUT_KEYBOARD,
            Data = new InputUnion
            {
                Keyboard = new KEYBDINPUT
                {
                    Vk = 0,
                    Scan = ch,
                    Flags = NativeConstants.KEYEVENTF_UNICODE,
                    Time = 0,
                    ExtraInfo = UIntPtr.Zero
                }
            }
        };

        // Key Up
        inputs[1] = new INPUT
        {
            Type = NativeConstants.INPUT_KEYBOARD,
            Data = new InputUnion
            {
                Keyboard = new KEYBDINPUT
                {
                    Vk = 0,
                    Scan = ch,
                    Flags = NativeConstants.KEYEVENTF_UNICODE | NativeConstants.KEYEVENTF_KEYUP,
                    Time = 0,
                    ExtraInfo = UIntPtr.Zero
                }
            }
        };

        User32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void SendSingleKey(Keys key, bool isKeyUp)
    {
        var input = new INPUT[1];
        uint flags = isKeyUp ? NativeConstants.KEYEVENTF_KEYUP : 0;

        input[0] = new INPUT
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

        User32.SendInput(1, input, Marshal.SizeOf<INPUT>());
    }

    private static List<Keys> GetModifierKeyList(KeyModifiers modifiers)
    {
        var list = new List<Keys>();
        if ((modifiers & KeyModifiers.Control) == KeyModifiers.Control) list.Add(Keys.ControlKey);
        if ((modifiers & KeyModifiers.Alt) == KeyModifiers.Alt) list.Add(Keys.Menu);
        if ((modifiers & KeyModifiers.Shift) == KeyModifiers.Shift) list.Add(Keys.ShiftKey);
        if ((modifiers & KeyModifiers.Windows) == KeyModifiers.Windows) list.Add(Keys.LWin);
        return list;
    }
}
