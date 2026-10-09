using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using GameTools.Core.Abstractions;
using GameTools.Core.Enums;
using GameTools.Core.Models;
using GameTools.Win32.Helpers;
using GameTools.Win32.Native;

namespace GameTools.Infrastructure.Automation;

/// <summary>
/// 控件点击实现，按降级链依次尝试。
/// </summary>
/// <remarks>
/// <para>降级链顺序依据实测结论确定：</para>
/// <list type="number">
/// <item>
/// <b>UI Automation 的 <c>InvokePattern</c></b>。实测在 Chromium 应用上有 25 个按钮
/// 支持该模式，且窗口最小化时仍可调用，是不依赖前台焦点的主路径。
/// </item>
/// <item>
/// <b>传统 <c>Button</c> 控件的 <c>BM_CLICK</c></b>。仅适用于遗留 MFC / WinForms /
/// 对话框程序；现代应用基本不暴露此类控件。
/// </item>
/// <item>
/// <b>按元素矩形中心点投递鼠标消息</b>。实测 Chromium 只对真正的 button 元素暴露
/// <c>InvokePattern</c>，自绘的 div 按钮不暴露该模式且句柄为 0，既无法调用模式也
/// 无法直接投递消息；此类元素可由「向根窗口投递矩形中心点的鼠标消息」触发，
/// 目标应用会自行做命中测试。
/// </item>
/// </list>
/// <para>
/// 原计划的 <c>LegacyIAccessiblePattern</c> 一级已移除：实测该类型位于
/// <c>Accessibility.dll</c>（MSAA interop），.NET 桌面目标包的 UIAutomationClient、
/// UIAutomationTypes 与 UIAutomationClientsideProviders 均不导出它。
/// 为一个在现代应用中极少命中的兜底级引入额外依赖并不划算。
/// </para>
/// <para>
/// 特征识图定位路径由后续阶段接入，命中后同样落到坐标点击逻辑。
/// 每级失败都记录原因并继续下一级，最终结果回显实际采用的机制。
/// </para>
/// </remarks>
public sealed class UiElementClicker : IElementClicker
{
    /// <summary>投递移动消息后等待的毫秒数，让悬停态先生效。</summary>
    private const int HoverDelayMs = 60;

    /// <summary>按下与抬起之间的毫秒数，部分控件依赖该间隔。</summary>
    private const int PressDelayMs = 40;

    private readonly IUiElementLocator _locator;
    private readonly IIntegrityLevelProbe _integrityProbe;

    /// <summary>
    /// 初始化点击器。
    /// </summary>
    /// <param name="locator">元素发现实现。</param>
    /// <param name="integrityProbe">完整性级别探测，用于提前给出 UIPI 风险提示。</param>
    public UiElementClicker(IUiElementLocator locator, IIntegrityLevelProbe integrityProbe)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _integrityProbe = integrityProbe ?? throw new ArgumentNullException(nameof(integrityProbe));
    }

    /// <inheritdoc />
    public async Task<ClickOutcome> ClickAsync(
        UiQuery query,
        TemplateDescriptor? template = null,
        MatchOptions? matchOptions = null,
        CancellationToken cancellationToken = default)
    {
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        var stopwatch = Stopwatch.StartNew();

        UiQueryResult located = await _locator.FindAsync(query, cancellationToken).ConfigureAwait(false);
        if (!located.Success || located.Elements.Count == 0)
        {
            string reason = located.Message.Length > 0
                ? $"未找到目标控件：{located.Message}"
                : "未找到目标控件。";

            return new ClickOutcome
            {
                Success = false,
                Message = reason + BuildElevationHint(query.RootWindowHandle),
                Elapsed = stopwatch.Elapsed
            };
        }

        var reasons = new System.Text.StringBuilder();

        // 优先在可点击类型上尝试，避免误点 Pane、文本等非交互元素
        var candidates = located.Elements.Where(e => e.IsClickCandidate).ToList();
        if (candidates.Count == 0)
        {
            candidates = located.Elements.ToList();
            reasons.Append("提示：未识别出可点击类型元素，已在全部候选上尝试。");
        }

        // 禁用元素一律跳过，且对所有降级级生效。
        // 常规用户输入由目标应用的命中测试过滤禁用控件，但自动化的三条路径都绕过了
        // 那一层：InvokePattern 在 WinForms 上会调用 PerformClick 并真实触发 Click；
        // 坐标点击则直接投递消息。因此必须在编排层统一拦截。
        var enabledCandidates = candidates.Where(e => e.IsEnabled).ToList();
        if (enabledCandidates.Count != candidates.Count)
        {
            int skipped = candidates.Count - enabledCandidates.Count;
            reasons.Append($"提示：已跳过 {skipped} 个禁用元素。");

            if (enabledCandidates.Count == 0)
            {
                return new ClickOutcome
                {
                    Success = false,
                    Message = "命中的控件全部处于禁用状态，未执行任何点击。" + reasons +
                              BuildElevationHint(query.RootWindowHandle),
                    Elapsed = stopwatch.Elapsed
                };
            }
        }

        candidates = enabledCandidates;

        foreach (UiElementInfo element in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string elevationHint = BuildElevationHint(query.RootWindowHandle);
            string label = element.Display + elevationHint;

            // 第一级：InvokePattern
            if (TryInvokePattern(element, out string? invokeFailure))
            {
                return new ClickOutcome
                {
                    Success = true,
                    Mechanism = ClickMechanism.UiaInvokePattern,
                    Message = $"已通过 UI Automation InvokePattern 点击「{label}」。",
                    Elapsed = stopwatch.Elapsed
                };
            }

            AppendReason(reasons, $"InvokePattern（{element.Display}）", invokeFailure);
        }

        // 第二级：传统 Button 控件 BM_CLICK
        IntPtr legacyButton = LegacyControlHelper.FindFirstButton(query.RootWindowHandle);
        if (legacyButton != IntPtr.Zero)
        {
            if (LegacyButtonClicker.TryClick(legacyButton, out string? clickFailure))
            {
                return new ClickOutcome
                {
                    Success = true,
                    Mechanism = ClickMechanism.LegacyButtonClick,
                    Message = "已通过传统 Button 控件的 BM_CLICK 点击。" +
                              BuildElevationHint(query.RootWindowHandle),
                    Elapsed = stopwatch.Elapsed
                };
            }

            AppendReason(reasons, "BM_CLICK", clickFailure);
        }
        else
        {
            AppendReason(
                reasons,
                "BM_CLICK",
                "窗口内未找到传统 Button 控件，现代应用通常需依赖 UI Automation。");
        }

        // 第四级：按元素矩形中心点投递鼠标消息。
        // 这是 Chromium 自绘按钮的唯一可行路径：它们不暴露 InvokePattern，
        // 句柄也为 0，但目标应用会对收到的鼠标消息自行做命中测试。
        foreach (UiElementInfo element in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryCoordinateClick(query.RootWindowHandle, element, out string? coordinateFailure))
            {
                return new ClickOutcome
                {
                    Success = true,
                    Mechanism = ClickMechanism.UiCoordinateClick,
                    Message = $"已向根窗口投递「{element.Display}」矩形中心点的鼠标消息。" +
                              "该路径不校验结果，若目标未响应需自行确认。" +
                              BuildElevationHint(query.RootWindowHandle),
                    Elapsed = stopwatch.Elapsed
                };
            }

            AppendReason(reasons, $"坐标点击（{element.Display}）", coordinateFailure);
        }

        return new ClickOutcome
        {
            Success = false,
            Message = "全部降级路径均失败：" + reasons,
            Elapsed = stopwatch.Elapsed
        };
    }

    /// <summary>
    /// 按元素矩形中心点向根窗口投递鼠标消息。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 适用于 Chromium 内核应用的自绘按钮：实测这类元素不暴露
    /// <c>InvokePattern</c>，原生句柄也为 0，既无法调用模式也无法直接投递消息，
    /// 但其矩形可由 UI Automation 读到。把中心点换算为窗口客户区坐标后
    /// 投递给根窗口，由目标应用自行命中测试即可触发。
    /// </para>
    /// <para>
    /// 必须先投递 <c>WM_MOUSEMOVE</c>：悬停后才显示的控件需要先收到移动消息。
    /// </para>
    /// </remarks>
    /// <param name="rootWindowHandle">目标根窗口句柄。</param>
    /// <param name="element">目标元素。</param>
    /// <param name="failure">失败原因；成功时为 <c>null</c>。</param>
    /// <returns>是否已成功投递鼠标消息。</returns>
    private static bool TryCoordinateClick(IntPtr rootWindowHandle, UiElementInfo element, out string? failure)
    {
        failure = null;

        if (element.Bounds.Width <= 0 || element.Bounds.Height <= 0)
        {
            failure = "元素矩形为空，无法计算点击坐标。";
            return false;
        }

        // 禁用元素必须跳过。
        // 常规输入路径由目标应用的命中测试过滤禁用控件，但消息是直接投递给
        // 根窗口的，绕过了那一层判断；实测 WinForms 的禁用 Button 仍会响应
        // WM_LBUTTONUP 并触发 Click 事件，因此此处必须显式拦截。
        if (!element.IsEnabled)
        {
            failure = "元素处于禁用状态。";
            return false;
        }

        if (element.IsOffscreen)
        {
            failure = "元素离屏。";
            return false;
        }

        // 元素矩形为屏幕坐标，需换算为窗口客户区坐标
        POINT screenPoint = new(
            element.Bounds.X + (element.Bounds.Width / 2),
            element.Bounds.Y + (element.Bounds.Height / 2));

        POINT clientPoint = screenPoint;
        if (!User32.ScreenToClient(rootWindowHandle, ref clientPoint))
        {
            failure = "屏幕坐标到客户区坐标换算失败。";
            return false;
        }

        IntPtr lParam = PackPoint(clientPoint);

        try
        {
            // 先移动：悬停后才显示的控件需要先收到移动消息
            User32.PostMessage(rootWindowHandle, NativeConstants.WM_MOUSEMOVE, IntPtr.Zero, lParam);
            Thread.Sleep(HoverDelayMs);

            // 按下时 wParam 须带 MK_LBUTTON
            User32.PostMessage(rootWindowHandle, NativeConstants.WM_LBUTTONDOWN, new IntPtr(1), lParam);
            Thread.Sleep(PressDelayMs);

            User32.PostMessage(rootWindowHandle, NativeConstants.WM_LBUTTONUP, IntPtr.Zero, lParam);
            return true;
        }
        catch (Exception ex)
        {
            failure = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 按 Win32 约定把客户区坐标打包进 <c>lParam</c>：低字为 X，高字为 Y。
    /// </summary>
    /// <param name="point">客户区坐标。</param>
    /// <returns>打包后的消息参数。</returns>
    private static IntPtr PackPoint(POINT point) =>
        new((point.Y << 16) | (point.X & 0xFFFF));

    /// <summary>
    /// 构造 UIPI 风险提示。
    /// </summary>
    /// <remarks>
    /// UIPI 会拦截低完整性级别进程向更高完整性级别进程发送消息，
    /// 表现是调用返回失败而无明确原因。提前比对双方级别可把这种失败转为可操作提示。
    /// </remarks>
    /// <param name="rootWindowHandle">目标窗口句柄。</param>
    /// <returns>存在风险时返回提示文案；否则返回空字符串。</returns>
    private string BuildElevationHint(IntPtr rootWindowHandle)
    {
        try
        {
            int processId = GameTools.Win32.Native.User32.GetWindowThreadProcessId(rootWindowHandle, out uint pid) != 0
                ? (int)pid
                : 0;

            if (processId <= 0)
            {
                return string.Empty;
            }

            return _integrityProbe.CheckElevationRisk(processId) ?? string.Empty;
        }
        catch (Exception)
        {
            // 探测失败不得影响点击流程
            return string.Empty;
        }
    }

    private static bool TryInvokePattern(UiElementInfo element, out string? failure)
    {
        failure = null;

        try
        {
            AutomationElement? target = ResolveElement(element);
            if (target == null)
            {
                failure = "无法定位对应的 AutomationElement。";
                return false;
            }

            if (!target.TryGetCurrentPattern(InvokePattern.Pattern, out object? pattern) || pattern is not InvokePattern invoke)
            {
                failure = "元素不支持 InvokePattern。";
                return false;
            }

            invoke.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            failure = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 按句柄、自动化标识、名称依次解析元素。
    /// </summary>
    /// <remarks>
    /// 顺序按稳定性排列：句柄最精确，自动化标识次之，名称最不稳定。
    /// 多数 Chromium 元素的句柄为 0，因此后两种方式是其主要途径。
    /// </remarks>
    /// <para>
    /// 名称并不唯一：实测按名称回查时可能命中同名但不同类型的元素，
    /// 导致 <c>InvokePattern</c> 等模式检测得出错误结论。因此名称条件
    /// 一律附加控件类型约束，避免把另一个元素的模式误当作目标元素的。
    /// </para>
    private static AutomationElement? ResolveElement(UiElementInfo element)
    {
        if (element.NativeHandle != IntPtr.Zero)
        {
            return AutomationElement.FromHandle(element.NativeHandle);
        }

        if (!string.IsNullOrWhiteSpace(element.AutomationId))
        {
            AutomationElement? byId = AutomationElement.RootElement.FindFirst(
                TreeScope.Descendants,
                BuildCondition(AutomationElement.AutomationIdProperty, element.AutomationId, element.ControlType));

            if (byId != null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(element.Name))
        {
            return AutomationElement.RootElement.FindFirst(
                TreeScope.Descendants,
                BuildCondition(AutomationElement.NameProperty, element.Name, element.ControlType));
        }

        return null;
    }

    /// <summary>
    /// 构造「属性等于指定值且控件类型一致」的查找条件。
    /// </summary>
    /// <param name="property">目标属性。</param>
    /// <param name="value">属性值。</param>
    /// <param name="controlType">期望的控件类型；为空时只按属性匹配。</param>
    /// <returns>查找条件。</returns>
    private static Condition BuildCondition(
        AutomationProperty property,
        string value,
        string? controlType)
    {
        var valueCondition = new PropertyCondition(property, value);

        ControlType? expected = ControlTypeFor(controlType);
        if (expected == null)
        {
            // 类型名为空或无法识别时不参与约束，避免因拼写差异导致查不到元素
            return valueCondition;
        }

        return new AndCondition(
            valueCondition,
            new PropertyCondition(AutomationElement.ControlTypeProperty, expected));
    }

    /// <summary>
    /// 把简写的控件类型名还原为 UI Automation 的控件类型。
    /// </summary>
    /// <param name="controlType">简写名，例如 Button、Edit。</param>
    /// <returns>
    /// 控件类型；名称为空、未知或为占位符 <c>?</c> 时返回 <c>null</c>。
    /// </returns>
    /// <remarks>
    /// 必须返回 <see cref="ControlType"/> 实例而非其编程名称字符串：
    /// <c>ControlTypeProperty</c> 的 <c>PropertyCondition</c> 只接受
    /// <see cref="ControlType"/> 对象，传入字符串会在运行期抛
    /// <c>ArgumentException</c>。
    /// </remarks>
    private static ControlType? ControlTypeFor(string? controlType) => controlType switch
    {
        "Button" => global::System.Windows.Automation.ControlType.Button,
        "Edit" => global::System.Windows.Automation.ControlType.Edit,
        "CheckBox" => global::System.Windows.Automation.ControlType.CheckBox,
        "RadioButton" => global::System.Windows.Automation.ControlType.RadioButton,
        "MenuItem" => global::System.Windows.Automation.ControlType.MenuItem,
        "Hyperlink" => global::System.Windows.Automation.ControlType.Hyperlink,
        "TabItem" => global::System.Windows.Automation.ControlType.TabItem,
        "ComboBox" => global::System.Windows.Automation.ControlType.ComboBox,
        "Document" => global::System.Windows.Automation.ControlType.Document,
        "Pane" => global::System.Windows.Automation.ControlType.Pane,
        "Text" => global::System.Windows.Automation.ControlType.Text,
        "ListItem" => global::System.Windows.Automation.ControlType.ListItem,
        _ => null
    };

    private static void AppendReason(System.Text.StringBuilder builder, string stage, string? reason)
    {
        if (builder.Length > 0)
        {
            builder.Append("；");
        }

        builder.Append(stage).Append('：').Append(string.IsNullOrWhiteSpace(reason) ? "未知原因" : reason);
    }
}
