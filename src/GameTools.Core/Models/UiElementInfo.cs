namespace GameTools.Core.Models;

/// <summary>
/// 平台无关的控件元素描述。
/// </summary>
/// <remarks>
/// <para>
/// 由 <c>UI Automation</c> 或传统 Win32 子控件枚举产出，用于界面展示与后续动作定位。
/// 不引用任何 UI Automation 或 Win32 类型，使契约层可在任意目标框架下使用。
/// </para>
/// <para>
/// 实测背景：现代应用几乎不暴露传统 Win32 子控件——Grok Bot（Chromium 内核）
/// 仅 1 个子窗口，WPF 控件没有独立 HWND，因此
/// <see cref="NativeHandle"/> 对多数元素为 0，此时应依赖
/// <see cref="Name"/> 与 <see cref="AutomationId"/> 定位。
/// </para>
/// </remarks>
/// <param name="NativeHandle">原生窗口句柄；无独立句柄时为 0。</param>
/// <param name="Name">元素名称，通常为界面可见文本。</param>
/// <param name="AutomationId">自动化标识，用于稳定定位。</param>
/// <param name="ControlType">控件类型名，例如 Edit、Button。</param>
/// <param name="ClassName">窗口类名。</param>
/// <param name="Bounds">元素在屏幕坐标系中的矩形。</param>
/// <param name="IsEnabled">元素是否可用。</param>
/// <param name="IsOffscreen">元素是否离屏。</param>
/// <param name="ProcessId">所属进程 ID。</param>
/// <param name="Depth">在无障碍树中的层级深度，根窗口为 0。</param>
public readonly record struct UiElementInfo(
    IntPtr NativeHandle,
    string Name,
    string AutomationId,
    string ControlType,
    string ClassName,
    CaptureBounds Bounds,
    bool IsEnabled,
    bool IsOffscreen,
    int ProcessId,
    int Depth)
{
    /// <summary>
    /// 是否具备可用的定位依据。
    /// </summary>
    /// <remarks>
    /// 无句柄且名称与自动化标识均为空时无法定位任何元素，
    /// 上层应据此过滤，避免把不可操作元素呈现为可点击目标。
    /// </remarks>
    public bool IsAddressable =>
        NativeHandle != IntPtr.Zero || !string.IsNullOrWhiteSpace(Name) || !string.IsNullOrWhiteSpace(AutomationId);

    /// <summary>
    /// 是否为可写入文本的元素类型。
    /// </summary>
    public bool IsTextEntryCandidate =>
        ControlType is "Edit" or "Document" or "ComboBox";

    /// <summary>
    /// 是否为可点击的元素类型。
    /// </summary>
    public bool IsClickCandidate =>
        ControlType is "Button" or "CheckBox" or "RadioButton" or "MenuItem" or "Hyperlink" or "TabItem";

    /// <summary>
    /// 界面显示文本。
    /// </summary>
    /// <remarks>
    /// 名称为空时回退到类型与句柄，保证列表项始终可读。
    /// </remarks>
    public string Display
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name))
            {
                return Name;
            }

            if (!string.IsNullOrWhiteSpace(AutomationId))
            {
                return $"[{ControlType}] {AutomationId}";
            }

            return NativeHandle != IntPtr.Zero
                ? $"[{ControlType}] 0x{NativeHandle.ToInt64():X}"
                : $"[{ControlType}] (无可用句柄)";
        }
    }
}
