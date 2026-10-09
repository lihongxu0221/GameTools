using GameTools.Core.Enums;
using GameTools.Core.Models;

namespace GameTools.Core.Abstractions;

/// <summary>
/// 控件元素发现接口。
/// </summary>
/// <remarks>
/// 现代应用不暴露传统 Win32 子控件，UI Automation 是主要发现手段，
/// 因此该能力属于契约层的一部分，供应用层与基础设施层共同依赖。
/// </remarks>
public interface IUiElementLocator
{
    /// <summary>
    /// 按条件查找控件元素。
    /// </summary>
    /// <param name="query">查找条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>查找结果。</returns>
    Task<UiQueryResult> FindAsync(UiQuery query, CancellationToken cancellationToken = default);
}

/// <summary>
/// 控件文本写入接口。
/// </summary>
public interface ITextWriter
{
    /// <summary>
    /// 向指定控件写入文本，内部按降级链尝试。
    /// </summary>
    /// <param name="query">目标控件的查找条件。</param>
    /// <param name="text">要写入的文本。</param>
    /// <param name="delayBetweenCharsMs">逐字符投递时的字符间隔毫秒数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>写入结果，含实际采用的机制。</returns>
    Task<TextEntryOutcome> WriteAsync(
        UiQuery query,
        string text,
        int delayBetweenCharsMs = 10,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 控件点击接口。
/// </summary>
public interface IElementClicker
{
    /// <summary>
    /// 点击指定控件，内部按降级链尝试。
    /// </summary>
    /// <param name="query">目标控件的查找条件。</param>
    /// <param name="template">
    /// 当控件无法通过元素树定位时用于特征识图的模板；为 <c>null</c> 表示不使用识图路径。
    /// </param>
    /// <param name="matchOptions">识图参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>点击结果，含实际采用的机制与捕获源。</returns>
    Task<ClickOutcome> ClickAsync(
        UiQuery query,
        TemplateDescriptor? template = null,
        MatchOptions? matchOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在指定屏幕坐标模拟左键点击。
    /// </summary>
    /// <param name="screenX">屏幕横坐标。</param>
    /// <param name="screenY">屏幕纵坐标。</param>
    /// <param name="strategy">
    /// 投递方式。默认 <see cref="MouseDispatchStrategy.MessageOnly"/>，
    /// 该方式保证完全不移动物理光标，实体鼠标不受影响。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>点击结果。</returns>
    Task<ClickOutcome> ClickScreenPointAsync(
        int screenX,
        int screenY,
        MouseDispatchStrategy strategy = MouseDispatchStrategy.MessageOnly,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 图像模板匹配接口。
/// </summary>
/// <remarks>
/// 抽象为接口以预留 OpenCV 等实现：本期提供自研固定尺度匹配，
/// 后续若需多尺度、旋转或特征点匹配，可新增实现而不改动上层。
/// </remarks>
public interface ITemplateMatcher
{
    /// <summary>
    /// 在给定画面中查找模板。
    /// </summary>
    /// <param name="frame">待搜索的画面。</param>
    /// <param name="template">模板图像（BGRA，与 <see cref="CaptureFrame"/> 同格式）。</param>
    /// <param name="options">匹配参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>匹配结果。</returns>
    Task<TemplateMatchResult> MatchAsync(
        CaptureFrame frame,
        CaptureFrame template,
        MatchOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 进程完整性级别探测接口。
/// </summary>
/// <remarks>
/// UIPI 会拦截低完整性级别进程向高完整性级别进程发送输入与调用，
/// 因此在操作前比对双方级别，可提前给出明确提示而不是让操作神秘失败。
/// </remarks>
public interface IIntegrityLevelProbe
{
    /// <summary>
    /// 获取指定进程所在用户的完整性级别。
    /// </summary>
    /// <param name="processId">进程 ID；为 0 表示当前进程。</param>
    /// <returns>完整性级别；无法探测时返回 <c>null</c>。</returns>
    IntegrityLevel? GetProcessIntegrityLevel(int processId = 0);

    /// <summary>
    /// 判断当前进程是否可能被 UIPI 拦截。
    /// </summary>
    /// <param name="targetProcessId">目标进程 ID。</param>
    /// <returns>存在拦截风险时返回提示文案；无风险时返回 <c>null</c>。</returns>
    string? CheckElevationRisk(int targetProcessId);
}

/// <summary>
/// Windows 完整性级别。
/// </summary>
public enum IntegrityLevel
{
    /// <summary>未分级（低完整性级别沙箱）。</summary>
    Untrusted = 0,

    /// <summary>低完整性级别。</summary>
    Low = 1,

    /// <summary>中完整性级别（普通桌面程序）。</summary>
    Medium = 2,

    /// <summary>高完整性级别（管理员）。</summary>
    High = 3,

    /// <summary>系统完整性级别。</summary>
    System = 4
}
