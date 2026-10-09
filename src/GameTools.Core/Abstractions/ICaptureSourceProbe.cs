using GameTools.Core.Enums;
using GameTools.Core.Models;

namespace GameTools.Core.Abstractions;

/// <summary>
/// 捕获源运行时探测。
/// </summary>
/// <remarks>
/// <para>
/// 特征识图必须先取得目标窗口的画面，但「哪种捕获方式能取到内容」是运行时
/// 才知道的事实，无法在编译期或配置中写死：
/// </para>
/// <list type="bullet">
/// <item>
/// Chromium、WinUI 等 DWM 渲染窗口：必须用
/// <c>PrintWindow</c> + <c>PW_RENDERFULLCONTENT</c>，其余标志一律返回全黑。
/// </item>
/// <item>
/// 硬件加速窗口（如 v2rayN 一类基于 Avalonia / Skia 的应用）：
/// <c>PrintWindow</c> 三种标志全部失败，只能回退到屏幕区域捕获，
/// 且要求窗口在屏幕上可见、未被完全遮挡。
/// </item>
/// <item>
/// 传统 Win32 程序：<c>PrintWindow</c> 默认标志即可。
/// </item>
/// </list>
/// <para>
/// 因此本接口在运行时逐个尝试并如实报告结果，调用方据此选择，
/// 并可把实际采用的源回显给用户，避免「为什么截不到图」无从排查。
/// </para>
/// </remarks>
public interface ICaptureSourceProbe
{
    /// <summary>
    /// 探测指定窗口当前可用的捕获源。
    /// </summary>
    /// <remarks>
    /// 调用会实际执行捕获，因此可能较慢；应由调用方放在后台线程执行。
    /// </remarks>
    /// <param name="windowHandle">目标窗口句柄。</param>
    /// <returns>探测报告，包含实际可用的源与每个候选源的尝试结果。</returns>
    CaptureSourceReport Probe(IntPtr windowHandle);

    /// <summary>
    /// 按指定捕获源捕获窗口，不做任何降级。
    /// </summary>
    /// <remarks>
    /// 与按优先级自动降级的常规捕获不同，本方法只使用给定的那一种方式。
    /// 用途是让调用方明确知道画面来自哪里，识别图结果的来源可追溯。
    /// </remarks>
    /// <param name="windowHandle">目标窗口句柄。</param>
    /// <param name="source">指定捕获源。</param>
    /// <returns>捕获结果；该源不可用时 <see cref="CaptureResult.Success"/> 为假。</returns>
    CaptureResult Capture(IntPtr windowHandle, CaptureSourceKind source);
}
