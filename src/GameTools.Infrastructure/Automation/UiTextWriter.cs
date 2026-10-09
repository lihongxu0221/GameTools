using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using GameTools.Core.Abstractions;
using GameTools.Core.Enums;
using GameTools.Core.Models;
using GameTools.Infrastructure.Input;
using GameTools.Win32.Helpers;

namespace GameTools.Infrastructure.Automation;

/// <summary>
/// 控件文本写入实现，按降级链依次尝试。
/// </summary>
/// <remarks>
/// <para>降级链顺序依据实测结论确定：</para>
/// <list type="number">
/// <item>
/// <b>UI Automation 的 <c>ValuePattern</c></b>。实测在 Chromium 应用上可写，
/// 且窗口最小化时仍然有效，是唯一不依赖前台焦点的整段写入方式。
/// </item>
/// <item>
/// <b>UI Automation 定位 + 逐字符 <c>WM_CHAR</c></b>。用于不支持
/// <c>ValuePattern</c> 但能接受字符消息的元素；需要元素有原生句柄。
/// </item>
/// <item>
/// <b>传统 <c>Edit</c> 控件的 <c>WM_SETTEXT</c></b>。仅适用于遗留 MFC / WinForms /
/// 对话框程序；实测 Chromium 内核窗口仅有 1 个子窗口且无 Edit 控件，
/// WPF 控件则没有独立 HWND。
/// </item>
/// </list>
/// <para>
/// 每级失败都记录原因并继续下一级，最终结果回显实际采用的机制，
/// 使调用方能区分「成功但走了低效路径」与「彻底失败」。
/// </para>
/// </remarks>
public sealed class UiTextWriter : ITextWriter
{
    private readonly IUiElementLocator _locator;
    private readonly ITextInputSimulator _simulator;

    /// <summary>
    /// 初始化文本写入器。
    /// </summary>
    /// <param name="locator">元素发现实现。</param>
    /// <param name="simulator">文本与按键模拟实现。</param>
    public UiTextWriter(IUiElementLocator locator, ITextInputSimulator simulator)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _simulator = simulator ?? throw new ArgumentNullException(nameof(simulator));
    }

    /// <inheritdoc />
    public async Task<TextEntryOutcome> WriteAsync(
        UiQuery query,
        string text,
        int delayBetweenCharsMs = 10,
        CancellationToken cancellationToken = default)
    {
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        var stopwatch = Stopwatch.StartNew();

        if (text == null)
        {
            return new TextEntryOutcome
            {
                Success = false,
                Message = "待写入文本为 null。",
                Elapsed = stopwatch.Elapsed
            };
        }

        UiQueryResult located = await _locator.FindAsync(query, cancellationToken).ConfigureAwait(false);
        if (!located.Success || located.Elements.Count == 0)
        {
            return new TextEntryOutcome
            {
                Success = false,
                Message = located.Message.Length > 0
                    ? $"未找到目标控件：{located.Message}"
                    : "未找到目标控件。",
                Elapsed = stopwatch.Elapsed
            };
        }

        var reasons = new System.Text.StringBuilder();

        // 优先在文本框类元素上尝试，避免误把 Pane、列表项之类当作输入目标。
        // 若无任何元素被识别为文本输入类型，则退回全部候选，由各降级路径自行判定。
        var candidates = located.Elements.Where(e => e.IsTextEntryCandidate).ToList();
        if (candidates.Count == 0)
        {
            candidates = located.Elements.ToList();
            reasons.Append("提示：未识别出文本输入类元素，已在全部候选上尝试。");
        }

        foreach (UiElementInfo element in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 第一级：ValuePattern 整段写入
            if (TryValuePattern(element, text, out string? valuePatternFailure))
            {
                return new TextEntryOutcome
                {
                    Success = true,
                    Mechanism = TextEntryMechanism.UiaValuePattern,
                    Message = $"已通过 UI Automation ValuePattern 写入「{element.Display}」。",
                    Elapsed = stopwatch.Elapsed
                };
            }

            AppendReason(reasons, $"ValuePattern（{element.Display}）", valuePatternFailure);
        }

        // 第二级：UI Automation 定位 + 逐字符 WM_CHAR。
        // 空文本时本级无效：逐字符投递空串不会发出任何消息，若仍报成功即为假成功。
        // 清空输入框由第一级 SetValue("") 或第三级 WM_SETTEXT("") 承担。
        bool canPostChars = text.Length > 0;

        if (canPostChars)
        {
            foreach (UiElementInfo element in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (element.NativeHandle == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    _simulator.PostTextToWindow(element.NativeHandle, text, delayBetweenCharsMs, cancellationToken);

                    return new TextEntryOutcome
                    {
                        Success = true,
                        Mechanism = TextEntryMechanism.UiaCharSequence,
                        Message = $"已通过逐字符 WM_CHAR 写入「{element.Display}」。" +
                                  "该路径不校验结果，若目标未接受需自行确认。",
                        Elapsed = stopwatch.Elapsed
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    AppendReason(reasons, $"逐字符写入（{element.Display}）", ex.Message);
                }
            }

            AppendReason(reasons, "逐字符写入", "候选元素均无可用原生句柄。");
        }

        // 第三级：传统 Edit 控件 WM_SETTEXT
        IntPtr legacyHandle = LegacyControlHelper.FindFirstTextControl(query.RootWindowHandle);
        if (legacyHandle != IntPtr.Zero)
        {
            try
            {
                if (_simulator.SetWindowText(legacyHandle, text))
                {
                    return new TextEntryOutcome
                    {
                        Success = true,
                        Mechanism = TextEntryMechanism.LegacySetText,
                        Message = "已通过传统 Edit 控件的 WM_SETTEXT 写入。",
                        Elapsed = stopwatch.Elapsed
                    };
                }

                AppendReason(reasons, "WM_SETTEXT", "返回失败，可能是跨进程调用被 UIPI 拦截。");
            }
            catch (Exception ex)
            {
                AppendReason(reasons, "WM_SETTEXT", ex.Message);
            }
        }
        else if (text.Length == 0)
        {
            // 空文本且未找到传统控件：无法清空，如实报告而非假成功
            AppendReason(reasons, "WM_SETTEXT", "窗口内未找到传统 Edit 控件，无法清空输入框。");
        }
        else
        {
            AppendReason(reasons, "WM_SETTEXT", "窗口内未找到传统 Edit 控件。");
        }

        return new TextEntryOutcome
        {
            Success = false,
            Message = "全部降级路径均失败：" + reasons,
            Elapsed = stopwatch.Elapsed
        };
    }

    /// <summary>
    /// 尝试以 <c>ValuePattern</c> 写入元素文本。
    /// </summary>
    /// <param name="element">目标元素。</param>
    /// <param name="text">待写入文本。</param>
    /// <param name="failure">失败原因；成功时为 <c>null</c>。</param>
    /// <returns>是否成功。</returns>
    private static bool TryValuePattern(UiElementInfo element, string text, out string? failure)
    {
        failure = null;

        try
        {
            AutomationElement? target = element.NativeHandle != IntPtr.Zero
                ? AutomationElement.FromHandle(element.NativeHandle)
                : AutomationElement.RootElement.FindFirst(
                    TreeScope.Descendants,
                    BuildLocator(element));

            if (target == null)
            {
                failure = "无法定位对应的 AutomationElement。";
                return false;
            }

            if (!target.TryGetCurrentPattern(ValuePattern.Pattern, out object? pattern) || pattern is not ValuePattern valuePattern)
            {
                failure = "元素不支持 ValuePattern。";
                return false;
            }

            valuePattern.SetValue(text);
            return true;
        }
        catch (Exception ex)
        {
            // UIPI 拦截在此表现为 COM 异常，需在结果中如实说明
            failure = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 按元素属性构造 UI Automation 定位条件。
    /// </summary>
    /// <param name="element">目标元素。</param>
    /// <returns>定位条件；无任何可用属性时返回 <c>null</c>。</returns>
    private static Condition? BuildLocator(UiElementInfo element)
    {
        // AutomationId 最稳定，优先使用
        if (!string.IsNullOrWhiteSpace(element.AutomationId))
        {
            return new PropertyCondition(AutomationElement.AutomationIdProperty, element.AutomationId);
        }

        // 名称可能重复，仅作为次选
        if (!string.IsNullOrWhiteSpace(element.Name))
        {
            return new PropertyCondition(AutomationElement.NameProperty, element.Name);
        }

        return null;
    }

    private static void AppendReason(System.Text.StringBuilder builder, string stage, string? reason)
    {
        if (builder.Length > 0)
        {
            builder.Append("；");
        }

        builder.Append(stage).Append('：').Append(string.IsNullOrWhiteSpace(reason) ? "未知原因" : reason);
    }
}
