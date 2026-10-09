using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GameTools.Win32.Native;

namespace GameTools.Infrastructure.Automation;

/// <summary>
/// 传统 Win32 按钮点击。
/// </summary>
/// <remarks>
/// 仅适用于暴露传统子控件的程序（MFC、WinForms、原生对话框）。实测现代应用
/// 基本不适用：Chromium 内核窗口仅有 1 个子窗口，WPF 控件没有独立 HWND，
/// 资源管理器使用 DirectUI 自绘。因此该路径在整体方案中位于降级链末位。
/// </remarks>
public static class LegacyButtonClicker
{
    /// <summary>
    /// 等待目标窗口处理消息的上限。
    /// </summary>
    /// <remarks>
    /// <c>BM_CLICK</c> 是同步消息，目标无响应时调用方会一直阻塞。
    /// 因此统一经 <c>SendMessageTimeout</c> 投递并设上限，符合「所有外部调用
    /// 必须具备明确超时」的要求。
    /// </remarks>
    private const uint SendMessageTimeoutMs = 3000;

    /// <summary>
    /// 点击指定按钮句柄。
    /// </summary>
    /// <param name="buttonHandle">按钮句柄。</param>
    /// <param name="failure">失败原因；成功时为 <c>null</c>。</param>
    /// <returns>是否成功投递点击。</returns>
    public static bool TryClick(IntPtr buttonHandle, out string? failure)
    {
        failure = null;

        if (buttonHandle == IntPtr.Zero)
        {
            failure = "按钮句柄为空。";
            return false;
        }

        if (!User32.IsWindow(buttonHandle))
        {
            failure = "按钮窗口已不存在。";
            return false;
        }

        if (!User32.IsWindowEnabled(buttonHandle))
        {
            failure = "按钮处于禁用状态，点击不会产生动作。";
            return false;
        }

        try
        {
            IntPtr result = User32.SendMessageTimeout(
                buttonHandle,
                NativeConstants.BM_CLICK,
                IntPtr.Zero,
                IntPtr.Zero,
                NativeConstants.SMTO_BLOCK | NativeConstants.SMTO_ABORTIFHUNG,
                SendMessageTimeoutMs,
                out _);

            // SendMessageTimeout 返回 0 表示超时或消息未被处理
            if (result == IntPtr.Zero)
            {
                failure = $"BM_CLICK 未在 {SendMessageTimeoutMs}ms 内被目标处理" +
                          $"（错误码 {Marshal.GetLastWin32Error()}）。目标程序可能无响应。";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            // UIPI 拦截在此表现为异常或返回 0
            failure = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }
}
