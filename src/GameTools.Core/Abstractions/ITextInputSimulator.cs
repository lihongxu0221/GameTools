using System.Windows.Forms;
using GameTools.Core.Enums;

namespace GameTools.Core.Abstractions;

/// <summary>
/// 文本与按键模拟输入接口
/// </summary>
public interface ITextInputSimulator
{
    /// <summary>
    /// 前台模拟输入 Unicode 文本（绕过输入法干扰，直接投递中文字符/多语言）
    /// </summary>
    /// <param name="text">要输入的文本</param>
    /// <param name="delayBetweenCharsMs">字符输入间隔毫秒数（防丢字）</param>
    void SendText(string text, int delayBetweenCharsMs = 10);

    /// <summary>
    /// 前台模拟单键或组合键按下并弹起
    /// </summary>
    void SendKeyPress(Keys key, KeyModifiers modifiers = KeyModifiers.None);

    /// <summary>
    /// 向未激活的后台指定窗口定向投递字符（基于 PostMessage WM_CHAR）
    /// </summary>
    void PostTextToWindow(IntPtr hWnd, string text, int delayBetweenCharsMs = 10);

    /// <summary>
    /// 向目标窗口的文本框控件发送 WM_SETTEXT 快速写入文本
    /// </summary>
    bool SetWindowText(IntPtr hWnd, string text);

    /// <summary>
    /// 向后台窗口投递虚拟按键按下与抬起 (WM_KEYDOWN / WM_KEYUP)
    /// </summary>
    void PostKeyPressToWindow(IntPtr hWnd, Keys key);
}
