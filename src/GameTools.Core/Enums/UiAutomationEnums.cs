namespace GameTools.Core.Enums;

/// <summary>
/// 控件文本写入最终采用的机制。
/// </summary>
/// <remarks>
/// 降级链由上至下尝试，具体顺序与实测结论一致：
/// 现代应用（Grok Bot、DeepSeek、Chrome 等 Chromium 内核，WPF/WinUI）
/// 不暴露传统 Win32 子控件，但 UI Automation 可在后台（含最小化）读写，
/// 故优先使用 UI Automation；传统控件仅作为遗留程序的兜底。
/// </remarks>
public enum TextEntryMechanism
{
    /// <summary>未成功写入。</summary>
    None = 0,

    /// <summary>UI Automation 的 <c>ValuePattern</c>，一次性设置全量文本。</summary>
    UiaValuePattern = 1,

    /// <summary>UI Automation 定位元素后逐字符投递 <c>WM_CHAR</c>。</summary>
    UiaCharSequence = 2,

    /// <summary>向传统 <c>Edit</c> 控件发送 <c>WM_SETTEXT</c>。</summary>
    LegacySetText = 3
}

/// <summary>
/// 控件点击最终采用的机制。
/// </summary>
public enum ClickMechanism
{
    /// <summary>未成功点击。</summary>
    None = 0,

    /// <summary>UI Automation 的 <c>InvokePattern</c>。</summary>
    UiaInvokePattern = 1,

    /// <summary>UI Automation 的 <c>LegacyIAccessible</c> 动作。</summary>
    UiaLegacyAccessible = 2,

    /// <summary>向传统 <c>Button</c> 控件发送 <c>BM_CLICK</c>。</summary>
    LegacyButtonClick = 3,

    /// <summary>特征识图定位后向目标句柄投递鼠标消息。</summary>
    TemplateMatchClick = 4
}

/// <summary>
/// 特征识图实际采用的捕获源。
/// </summary>
/// <remarks>
/// 硬编码单一捕获源不可靠：实测 Chromium / WinUI / 桌面窗口仅
/// <c>PW_RENDERFULLCONTENT</c> 能取得内容，而硬件加速窗口
/// （如 Avalonia 应用）三种 flag 的 <c>PrintWindow</c> 全部失败，
/// 必须回退到屏幕区域捕获。
/// </remarks>
public enum CaptureSourceKind
{
    /// <summary>未确定。</summary>
    Unknown = 0,

    /// <summary><c>PrintWindow</c> + <c>PW_RENDERFULLCONTENT</c>。</summary>
    PrintWindowRenderFullContent = 1,

    /// <summary><c>PrintWindow</c> + 默认标志。</summary>
    PrintWindowDefault = 2,

    /// <summary><c>PrintWindow</c> + <c>PW_CLIENTONLY</c>。</summary>
    PrintWindowClientOnly = 3,

    /// <summary>屏幕区域 <c>BitBlt</c>，要求窗口在屏幕上可见且未被完全遮挡。</summary>
    ScreenBitBlt = 4
}
