using GameTools.Core.Enums;

namespace GameTools.Core.Abstractions;

/// <summary>
/// 文本与按键模拟输入接口。
/// </summary>
public interface ITextInputSimulator
{
    /// <summary>
    /// 前台模拟输入 Unicode 文本（绕过输入法，直接投递字符）。
    /// </summary>
    /// <remarks>
    /// 代理对（Emoji 等非 BMP 字符）按 UTF-16 代理对成组投递，组内不插入延迟；
    /// 若在两个码元之间插入字符间延迟，多数目标应用会丢弃孤立代理项导致输出错误。
    /// </remarks>
    /// <param name="text">要输入的文本。</param>
    /// <param name="delayBetweenCharsMs">字符输入间隔毫秒数（防丢字），0 表示无延迟。</param>
    /// <param name="jitterMs">字符间隔随机抖动毫秒数，实际间隔在 delay 正负 jitter 范围内取整。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    void SendText(string text, int delayBetweenCharsMs = 10, int jitterMs = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// 前台模拟单键或组合键按下并弹起。
    /// </summary>
    /// <param name="key">主键。</param>
    /// <param name="modifiers">修饰键。</param>
    void SendKeyPress(VirtualKey key, KeyModifiers modifiers = KeyModifiers.None);

    /// <summary>
    /// 向未激活的后台指定窗口定向投递字符（基于 PostMessage WM_CHAR）。
    /// </summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="text">要投递的文本。</param>
    /// <param name="delayBetweenCharsMs">字符间隔毫秒数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    void PostTextToWindow(IntPtr hWnd, string text, int delayBetweenCharsMs = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// 向目标窗口的文本框控件发送 WM_SETTEXT 快速写入文本。
    /// </summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="text">要写入的文本。</param>
    /// <returns>是否写入成功；跨进程调用可能被 UIPI 拒绝。</returns>
    bool SetWindowText(IntPtr hWnd, string text);

    /// <summary>
    /// 向后台窗口投递虚拟按键按下与抬起（WM_KEYDOWN / WM_KEYUP / WM_CHAR）。
    /// </summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="key">主键。</param>
    void PostKeyPressToWindow(IntPtr hWnd, VirtualKey key);
}