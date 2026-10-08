using GameTools.Win32.Native;

namespace GameTools.Tests;

/// <summary>
/// 测试用原生调用薄封装。
/// </summary>
/// <remarks>
/// 测试项目引用 Win32 层，直接调用其 P/Invoke 包装；此处仅提供语义化别名，
/// 使测试代码的意图更清晰，且避免在测试中重复 DllImport。
/// </remarks>
internal static class User32Bridge
{
    /// <summary>
    /// 向窗口投递消息。
    /// </summary>
    internal static bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        => User32.PostMessage(hWnd, msg, wParam, lParam);
}