using System;
using GameTools.Core.Enums;
using GameTools.Core.Models;
using GameTools.Win32.Helpers;
using GameTools.Win32.Native;

namespace GameTools.Infrastructure.Matching;

/// <summary>
/// 识图命中位置解析结果。
/// </summary>
public sealed record ResolvedTarget
{
    /// <summary>是否解析出可点击目标。</summary>
    public bool Success { get; init; }

    /// <summary>
    /// 命中的窗口句柄。
    /// </summary>
    /// <remarks>
    /// 为最深层子窗口；若该点下没有子窗口，则为传入的根窗口本身。
    /// 现代应用（WPF、Chromium、WinUI）的控件没有独立句柄，
    /// 此时结果就是根窗口，点击仍可通过坐标消息完成。
    /// </remarks>
    public IntPtr WindowHandle { get; init; }

    /// <summary>命中点屏幕横坐标。</summary>
    public int ScreenX { get; init; }

    /// <summary>命中点屏幕纵坐标。</summary>
    public int ScreenY { get; init; }

    /// <summary>结果说明。</summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// 把识图命中的画面坐标换算为屏幕坐标并解析为窗口句柄。
/// </summary>
/// <remarks>
/// <para>
/// 特征识图只能给出「画面上的一个矩形」，而点击需要的是屏幕坐标与窗口句柄。
/// 本类补上这一步，并明确区分两类捕获源的坐标系差异：
/// </para>
/// <list type="bullet">
/// <item>
/// <see cref="CaptureSourceKind.PrintWindowRenderFullContent"/> 等
/// <c>PrintWindow</c> 来源：帧内原点为窗口外框左上角。
/// </item>
/// <item>
/// <see cref="CaptureSourceKind.ScreenBitBlt"/>：捕获的是窗口在屏幕上的那块区域，
/// 原点同样是窗口外框左上角，但此时窗口被遮挡的部分截入的是遮挡物内容。
/// </item>
/// </list>
/// <para>
/// 两者原点一致，均取窗口外框左上角；保留来源参数是为了让调用方在日志与
/// 结果中如实回显来源，也便于将来出现第三种坐标系时在此集中处理。
/// </para>
/// </remarks>
public static class TemplatePointResolver
{
    /// <summary>
    /// 解析命中区域中心点对应的窗口句柄。
    /// </summary>
    /// <param name="rootWindowHandle">目标窗口句柄。</param>
    /// <param name="matchBounds">命中区域，采用捕获帧坐标系。</param>
    /// <param name="captureSource">产生该帧的捕获源。</param>
    /// <returns>解析结果。</returns>
    public static ResolvedTarget Resolve(
        IntPtr rootWindowHandle,
        CaptureBounds matchBounds,
        CaptureSourceKind captureSource)
    {
        if (rootWindowHandle == IntPtr.Zero || !User32.IsWindow(rootWindowHandle))
        {
            return new ResolvedTarget
            {
                Message = "窗口句柄无效或窗口已关闭。"
            };
        }

        if (matchBounds.Width <= 0 || matchBounds.Height <= 0)
        {
            return new ResolvedTarget
            {
                Message = "命中区域为空，无法计算点击位置。"
            };
        }

        // 捕获帧原点是窗口外框左上角（含不可见缩放边框），
        // 与 PrintWindow 的绘制范围一致；此处若误用 DWM 扩展边界，
        // 每侧会引入约 7 像素的系统性偏移。
        CaptureBounds frameOrigin = WindowHelper.GetOuterFrameBounds(rootWindowHandle);
        if (frameOrigin.IsEmpty)
        {
            return new ResolvedTarget
            {
                Message = "无法取得窗口在屏幕上的位置。"
            };
        }

        // 取命中区域中心点，避免边缘像素落在相邻控件上
        int captureX = matchBounds.X + (matchBounds.Width / 2);
        int captureY = matchBounds.Y + (matchBounds.Height / 2);

        int screenX = frameOrigin.X + captureX;
        int screenY = frameOrigin.Y + captureY;

        var capturePoint = new POINT(captureX, captureY);

        if (!LegacyControlHelper.TryToClientPoint(
                rootWindowHandle, capturePoint, frameOrigin.X, frameOrigin.Y, out POINT clientPoint))
        {
            // 坐标换算失败不应让整条识图点击链路失败：
            // 仍可退回根窗口并用屏幕坐标投递消息。
            return new ResolvedTarget
            {
                Success = true,
                WindowHandle = rootWindowHandle,
                ScreenX = screenX,
                ScreenY = screenY,
                Message = BuildMessage(
                    rootWindowHandle,
                    rootWindowHandle,
                    captureSource,
                    "无法换算为客户区坐标，已退回根窗口。")
            };
        }

        IntPtr deepest = LegacyControlHelper.ResolveDeepestWindow(rootWindowHandle, clientPoint);
        bool hasChild = deepest != IntPtr.Zero && deepest != rootWindowHandle;

        return new ResolvedTarget
        {
            Success = true,
            WindowHandle = hasChild ? deepest : rootWindowHandle,
            ScreenX = screenX,
            ScreenY = screenY,
            Message = BuildMessage(rootWindowHandle, deepest, captureSource, null)
        };
    }

    /// <summary>
    /// 构造结果说明。
    /// </summary>
    /// <param name="rootWindow">根窗口句柄。</param>
    /// <param name="resolved">解析到的窗口句柄。</param>
    /// <param name="captureSource">捕获源。</param>
    /// <param name="extra">附加说明。</param>
    /// <returns>说明文案。</returns>
    private static string BuildMessage(
        IntPtr rootWindow,
        IntPtr resolved,
        CaptureSourceKind captureSource,
        string? extra)
    {
        var text = new System.Text.StringBuilder();

        if (resolved != IntPtr.Zero && resolved != rootWindow)
        {
            string className = LegacyControlHelper.GetClassName(resolved);
            text.Append($"已解析到子窗口 {className}。");
        }
        else
        {
            text.Append("该位置没有独立的子窗口。");
            text.Append("自绘界面（WPF、Chromium、WinUI）的控件本就没有独立句柄，");
            text.Append("此时将向根窗口投递坐标消息，由目标自行命中测试。");
        }

        text.Append($"捕获来源：{Describe(captureSource)}。");

        if (!string.IsNullOrEmpty(extra))
        {
            text.Append(extra);
        }

        return text.ToString();
    }

    /// <summary>
    /// 生成捕获源的中文描述。
    /// </summary>
    /// <param name="source">捕获源。</param>
    /// <returns>描述文案。</returns>
    private static string Describe(CaptureSourceKind source) => source switch
    {
        CaptureSourceKind.PrintWindowRenderFullContent => "PrintWindow + PW_RENDERFULLCONTENT",
        CaptureSourceKind.PrintWindowDefault => "PrintWindow 默认标志",
        CaptureSourceKind.PrintWindowClientOnly => "PrintWindow + PW_CLIENTONLY",
        CaptureSourceKind.ScreenBitBlt => "屏幕区域 BitBlt",
        _ => "未知来源"
    };
}
