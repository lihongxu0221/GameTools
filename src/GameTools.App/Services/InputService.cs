using GameTools.Core.Abstractions;
using GameTools.Core.Enums;

namespace GameTools.App.Services;

/// <summary>
/// 基于 <see cref="ITextInputSimulator"/> 的输入服务实现。
/// </summary>
/// <remarks>
/// 将平台无关的 <see cref="VirtualKey"/> 透传给底层实现，并补充参数校验与结果诊断。
/// </remarks>
public sealed class InputService : IInputService
{
    private readonly ITextInputSimulator _simulator;

    /// <summary>
    /// 初始化输入服务。
    /// </summary>
    /// <param name="simulator">底层输入模拟实现。</param>
    public InputService(ITextInputSimulator simulator)
    {
        _simulator = simulator ?? throw new ArgumentNullException(nameof(simulator));
    }

    /// <inheritdoc />
    public void SendText(string text, int delayMs, int jitterMs)
    {
        if (delayMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delayMs), delayMs, "字符间隔不能为负数。");
        }

        if (jitterMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(jitterMs), jitterMs, "随机抖动不能为负数。");
        }

        _simulator.SendText(text, delayMs, jitterMs);
    }

    /// <inheritdoc />
    public void SendKey(VirtualKey key, KeyModifiers modifiers)
    {
        _simulator.SendKeyPress(key, modifiers);
    }

    /// <inheritdoc />
    public void PostText(IntPtr hWnd, string text, int delayMs)
    {
        _simulator.PostTextToWindow(hWnd, text, delayMs);
    }

    /// <inheritdoc />
    public void PostKey(IntPtr hWnd, VirtualKey key)
    {
        _simulator.PostKeyPressToWindow(hWnd, key);
    }

    /// <inheritdoc />
    public bool SetWindowText(IntPtr hWnd, string text)
    {
        return _simulator.SetWindowText(hWnd, text);
    }
}