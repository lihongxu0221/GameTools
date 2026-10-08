using GameTools.Core.Enums;

namespace GameTools.App.Services;

/// <summary>
/// 输入模拟能力的服务接口。
/// </summary>
public interface IInputService
{
    /// <summary>
    /// 向当前前台窗口发送 Unicode 文本。
    /// </summary>
    /// <param name="text">文本内容。</param>
    /// <param name="delayMs">字符间隔毫秒数。</param>
    /// <param name="jitterMs">字符间隔随机抖动毫秒数。</param>
    void SendText(string text, int delayMs, int jitterMs);

    /// <summary>
    /// 向当前前台窗口发送按键。
    /// </summary>
    /// <param name="key">主键。</param>
    /// <param name="modifiers">修饰键。</param>
    void SendKey(VirtualKey key, KeyModifiers modifiers);

    /// <summary>
    /// 向后台窗口投递文本（PostMessage WM_CHAR）。
    /// </summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="text">文本内容。</param>
    /// <param name="delayMs">字符间隔毫秒数。</param>
    void PostText(IntPtr hWnd, string text, int delayMs);

    /// <summary>
    /// 向后台窗口投递按键。
    /// </summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="key">主键。</param>
    void PostKey(IntPtr hWnd, VirtualKey key);

    /// <summary>
    /// 替换目标窗口的文本内容。
    /// </summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="text">文本内容。</param>
    /// <returns>是否写入成功。</returns>
    bool SetWindowText(IntPtr hWnd, string text);
}
