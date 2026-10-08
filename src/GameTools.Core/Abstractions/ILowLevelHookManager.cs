using GameTools.Core.Events;

namespace GameTools.Core.Abstractions;

/// <summary>
/// 全局低级输入钩子管理器接口
/// </summary>
public interface ILowLevelHookManager : IDisposable
{
    /// <summary>
    /// 键盘按键按下事件
    /// </summary>
    event EventHandler<KeyboardHookEventArgs>? KeyDown;

    /// <summary>
    /// 键盘按键抬起事件
    /// </summary>
    event EventHandler<KeyboardHookEventArgs>? KeyUp;

    /// <summary>
    /// 鼠标事件
    /// </summary>
    event EventHandler<MouseHookEventArgs>? MouseEvent;

    /// <summary>
    /// 启动低级键盘钩子
    /// </summary>
    /// <param name="targetProcessId">可选过滤的目标进程 PID，为 null 则监听全局</param>
    void StartKeyboardHook(int? targetProcessId = null);

    /// <summary>
    /// 停止低级键盘钩子
    /// </summary>
    void StopKeyboardHook();

    /// <summary>
    /// 启动低级鼠标钩子
    /// </summary>
    /// <param name="targetProcessId">可选过滤的目标进程 PID，为 null 则监听全局</param>
    void StartMouseHook(int? targetProcessId = null);

    /// <summary>
    /// 停止低级鼠标钩子
    /// </summary>
    void StopMouseHook();
}
