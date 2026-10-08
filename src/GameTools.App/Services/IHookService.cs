using GameTools.App.ViewModels;

namespace GameTools.App.Services;

/// <summary>
/// 钩子与窗口事件监听能力的服务接口。
/// </summary>
public interface IHookService
{
    /// <summary>
    /// 应用低级输入钩子配置。
    /// </summary>
    /// <param name="enableKeyboard">是否启用键盘钩子。</param>
    /// <param name="enableMouse">是否启用鼠标钩子。</param>
    /// <param name="filterProcessId">过滤目标进程；为 null 表示监听全局。</param>
    void ApplyHookConfiguration(bool enableKeyboard, bool enableMouse, int? filterProcessId);

    /// <summary>
    /// 开始监听指定进程的窗口生命周期事件。
    /// </summary>
    /// <param name="processId">目标进程 ID。</param>
    void StartWindowEventHook(int processId);

    /// <summary>
    /// 停止窗口事件监听。
    /// </summary>
    void StopWindowEventHook();

    /// <summary>
    /// 列出可选进程。
    /// </summary>
    IReadOnlyList<ProcessChoice> ListProcesses();
}