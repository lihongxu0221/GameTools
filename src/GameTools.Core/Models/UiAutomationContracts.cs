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

    /// <summary>
    /// 实际采用的鼠标投递方式。
    /// </summary>
    public MouseDispatchStrategy DispatchStrategy { get; init; } = MouseDispatchStrategy.MessageOnly;

    /// <summary>
    /// 物理光标是否被移动过。
    /// </summary>
    /// <remarks>
    /// 仅 <see cref="MouseDispatchStrategy.MoveAndRestore"/> 会置为真。
    /// 用于如实告知用户「实体鼠标在本次操作期间被短暂占用」，
    /// 而不是在未告知的情况下改变其光标位置。
    /// </remarks>
    public bool CursorWasMoved { get; init; }
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
    /// 是否启用加速路径。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 对自研匹配器而言，该开关控制是否启用<b>平方差提前终止</b>：累加过程中一旦
    /// 超过当前最优即可判定该位置不可能更优并立即退出。
    /// </para>
    /// <para>
    /// 加速<b>不改变匹配结果</b>——平方差各项非负，部分和是总和的下界，
    /// 因此剪掉的位置必然不优。实测 1920×1080 画面配合 80×30 模板，
    /// 关闭时约 5.7 秒、开启时约 0.4 秒，得分与坐标完全一致。
    /// </para>
    /// <para>
    /// 该开关<b>不是</b>「跳步粗筛」。跳步采样会漏掉落在采样网格之间的匹配位置，
    /// 早期实现因此无法找到画面中确实存在的模板，现已改为逐位置穷举。
    /// 其他实现（如未来的 OpenCV 适配）可自行解释该开关的语义。
    /// </para>
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

    /// <summary>实际采用的捕获源；由调用方在匹配前填充。</summary>
    public CaptureSourceKind CaptureSource { get; init; } = CaptureSourceKind.Unknown;
}

/// <summary>
/// 单个捕获源的尝试结果。
/// </summary>
public sealed record CaptureSourceAttempt
{
    /// <summary>被尝试的捕获源。</summary>
    public CaptureSourceKind Source { get; init; }

    /// <summary>该源是否取得了可用画面。</summary>
    public bool Usable { get; init; }

    /// <summary>
    /// 结果说明。
    /// </summary>
    /// <remarks>
    /// 失败时给出具体原因（调用返回假、未写入像素、内容为单色、窗口不可见等），
    /// 而不是笼统的「失败」，否则无法判断该换模板还是该换捕获方式。
    /// </remarks>
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// 捕获源探测报告。
/// </summary>
public sealed record CaptureSourceReport
{
    /// <summary>
    /// 实际可用的捕获源；<see cref="CaptureSourceKind.Unknown"/> 表示全部候选均不可用。
    /// </summary>
    public CaptureSourceKind Source { get; init; } = CaptureSourceKind.Unknown;

    /// <summary>是否存在可用捕获源。</summary>
    public bool Usable => Source != CaptureSourceKind.Unknown;

    /// <summary>
    /// 每个候选源的尝试结果，按尝试顺序排列。
    /// </summary>
    /// <remarks>
    /// 保留全部记录而非只给结论：探测失败时，这份列表是用户唯一能看到的诊断依据。
    /// </remarks>
    public IReadOnlyList<CaptureSourceAttempt> Attempts { get; init; } =
        Array.Empty<CaptureSourceAttempt>();

    /// <summary>整体说明。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>探测耗时。</summary>
    public TimeSpan Elapsed { get; init; }
}
