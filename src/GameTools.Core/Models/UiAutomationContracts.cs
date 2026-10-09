using GameTools.Core.Enums;
using GameTools.Core.Models;

namespace GameTools.Core.Models;

/// <summary>
/// 控件查找条件。
/// </summary>
/// <remarks>
/// 全部条件按「与」组合；留空的字段不参与过滤。
/// 不引入任何 UI Automation 条件类型，保持契约层平台无关。
/// </remarks>
public sealed record UiQuery
{
    /// <summary>
    /// 根窗口句柄。
    /// </summary>
    public IntPtr RootWindowHandle { get; init; } = IntPtr.Zero;

    /// <summary>
    /// 元素名称需完全匹配。为 <c>null</c> 表示不过滤。
    /// </summary>
    public string? NameExact { get; init; }

    /// <summary>
    /// 元素名称需包含该子串。为 <c>null</c> 表示不过滤。
    /// </summary>
    public string? NameContains { get; init; }

    /// <summary>
    /// 自动化标识需完全匹配。为 <c>null</c> 表示不过滤。
    /// </summary>
    public string? AutomationId { get; init; }

    /// <summary>
    /// 限定控件类型。为 <c>null</c> 或空表示不过滤。
    /// </summary>
    public IReadOnlyList<string>? ControlTypes { get; init; }

    /// <summary>
    /// 最大遍历深度。为 0 表示不限制。
    /// </summary>
    /// <remarks>
    /// UI Automation 的树遍历开销随深度增长，限定深度可避免在复杂文档
    /// （如浏览器页面）上长时间阻塞。
    /// </remarks>
    public int MaxDepth { get; init; }

    /// <summary>
    /// 是否排除离屏元素。
    /// </summary>
    public bool ExcludeOffscreen { get; init; } = true;

    /// <summary>
    /// 结果数量上限。为 0 表示不限制。
    /// </summary>
    public int MaxResults { get; init; } = 200;

    /// <summary>
    /// 查询超时（毫秒）。
    /// </summary>
    /// <remarks>
    /// UI Automation 的 COM 调用无法真正取消，本值仅用于调用方放弃等待；
    /// 超时不等于底层调用已终止。
    /// </remarks>
    public int TimeoutMs { get; init; } = 5000;
}

/// <summary>
/// 控件查找结果。
/// </summary>
public sealed record UiQueryResult
{
    /// <summary>查找是否成功。</summary>
    public bool Success { get; init; }

    /// <summary>命中的元素列表。</summary>
    public IReadOnlyList<UiElementInfo> Elements { get; init; } = Array.Empty<UiElementInfo>();

    /// <summary>遍历到的元素总数，用于判断无障碍树是否已就绪。</summary>
    public int TotalScanned { get; init; }

    /// <summary>
    /// 本次查询是否在服务端施加了名称或自动化标识过滤。
    /// </summary>
    /// <remarks>
    /// 为 <c>true</c> 时 <see cref="TotalScanned"/> 只统计通过条件的候选，
    /// 因此 0 不代表窗口没有可自动化元素。
    /// </remarks>
    public bool HasServerSideFilter { get; init; }

    /// <summary>失败或降级原因。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// 无障碍树是否疑似未就绪。
    /// </summary>
    /// <remarks>
    /// 实测 Chromium 内核惰性构建无障碍树：窗口未激活过时仅 13 个元素且无 Edit，
    /// 激活后可获得数百个元素。元素数量偏少时上层应提示用户先让窗口获得焦点。
    /// </remarks>
    public bool LooksLikeUninitializedTree => Success && Elements.Count == 0 && TotalScanned > 0 && TotalScanned < 30;
}

/// <summary>
/// 写入文本的操作结果。
/// </summary>
public sealed record TextEntryOutcome
{
    /// <summary>写入是否成功。</summary>
    public bool Success { get; init; }

    /// <summary>实际采用的机制。</summary>
    public TextEntryMechanism Mechanism { get; init; } = TextEntryMechanism.None;

    /// <summary>失败或降级原因。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>耗时。</summary>
    public TimeSpan Elapsed { get; init; }
}

/// <summary>
/// 点击操作的结果。
/// </summary>
public sealed record ClickOutcome
{
    /// <summary>点击是否成功。</summary>
    public bool Success { get; init; }

    /// <summary>实际采用的机制。</summary>
    public ClickMechanism Mechanism { get; init; } = ClickMechanism.None;

    /// <summary>失败或降级原因。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>耗时。</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>识图定位时实际采用的捕获源。</summary>
    public CaptureSourceKind CaptureSource { get; init; } = CaptureSourceKind.Unknown;
}

/// <summary>
/// 图像模板描述。
/// </summary>
/// <remarks>
/// 模板本身为图像数据，不放入契约层，由应用层负责文件读写，
/// 本类型只承载可序列化的元信息。
/// </remarks>
public sealed record TemplateDescriptor
{
    /// <summary>模板标识，全局唯一。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>模板显示名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>图像文件路径。</summary>
    public string ImagePath { get; init; } = string.Empty;

    /// <summary>模板宽度（像素）。</summary>
    public int Width { get; init; }

    /// <summary>模板高度（像素）。</summary>
    public int Height { get; init; }
}

/// <summary>
/// 识图参数。
/// </summary>
public sealed record MatchOptions
{
    /// <summary>
    /// 归一化互相关得分阈值，取值 0 到 1。
    /// </summary>
    /// <remarks>
    /// 默认 0.80，低于该阈值一律拒绝执行以避免误点。
    /// </remarks>
    public double ScoreThreshold { get; init; } = 0.80;

    /// <summary>
    /// 允许的模板尺度档位。
    /// </summary>
    /// <remarks>
    /// 自研匹配器仅支持固定尺度，通过少量档位覆盖高 DPI 差异。
    /// </remarks>
    public IReadOnlyList<double> Scales { get; init; } = new[] { 1.0 };

    /// <summary>
    /// 限制搜索区域；为空表示整幅捕获帧。
    /// </summary>
    public CaptureBounds? SearchRegion { get; init; }

    /// <summary>
    /// 是否启用两阶段搜索（先粗筛再精算）。
    /// </summary>
    /// <remarks>
    /// 粗筛按较大步长跳过大部分位置，仅对通过初筛的区域做精算，
    /// 可显著降低大图上的匹配耗时。
    /// </remarks>
    public bool UseCoarseSearch { get; init; } = true;
}

/// <summary>
/// 识图结果。
/// </summary>
public sealed record TemplateMatchResult
{
    /// <summary>是否命中（达到阈值）。</summary>
    public bool Found { get; init; }

    /// <summary>最高得分。</summary>
    public double BestScore { get; init; }

    /// <summary>命中区域在捕获帧坐标系中的矩形；未命中时为空。</summary>
    public CaptureBounds BestBounds { get; init; } = CaptureBounds.Empty;

    /// <summary>耗时。</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>
    /// 说明信息。
    /// </summary>
    /// <remarks>
    /// 未命中时会说明最高得分与阈值，便于判断是模板不匹配还是界面状态变化。
    /// </remarks>
    public string Message { get; init; } = string.Empty;
}
