using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Automation;

namespace GameTools.Infrastructure.Automation;

/// <summary>
/// UI Automation 能力探针（S13 验证用）。
/// </summary>
/// <remarks>
/// <para>
/// 仅用于验证 <c>UIAutomationClient</c> / <c>UIAutomationTypes</c> 在
/// <c>net8.0-windows</c> 与 <c>net48</c> 双目标下均可解析并正常工作。
/// </para>
/// <para>
/// 实测背景：现代应用（Grok Bot、DeepSeek、Chrome 等 Chromium 内核，WPF/WinUI，
/// 以及 DirectUI 的资源管理器）几乎不暴露传统 Win32 子控件，
/// <c>EnumChildWindows</c> 无法定位文本框或按钮；UI Automation 是主要手段。
/// 另已实测 UI Automation 的 <c>ValuePattern</c> 与 <c>InvokePattern</c>
/// 在目标窗口最小化时仍可读写与调用，满足「后台进程」操作要求。
/// </para>
/// <para>
/// 注意：Chromium 内核惰性构建无障碍树，窗口未激活过时元素数量极少
/// （实测 13 个），激活后可获得完整树（实测 180 个，含 1 个 Edit 与 32 个 Button）。
/// 因此本探针会显式返回元素总数，供上层判断无障碍树是否已就绪。
/// </para>
/// </remarks>
public sealed class UiAutomationProbe
{
    /// <summary>
    /// 探测指定窗口的 UI Automation 元素树。
    /// </summary>
    /// <param name="windowHandle">目标窗口句柄。</param>
    /// <returns>探测结果。</returns>
    public UiAutomationProbeResult Probe(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return UiAutomationProbeResult.Failed("窗口句柄无效 (IntPtr.Zero)。");
        }

        try
        {
            AutomationElement? root = AutomationElement.FromHandle(windowHandle);
            if (root == null)
            {
                return UiAutomationProbeResult.Failed(
                    $"无法从句柄 0x{windowHandle.ToInt64():X} 取得 AutomationElement；" +
                    "窗口可能已关闭，或未提供 UI Automation 支持。");
            }

            // 不加 TreeScope 限制地取全部后代，元素总数是判断无障碍树是否就绪的关键指标
            AutomationElementCollection all = root.FindAll(
                TreeScope.Descendants,
                Condition.TrueCondition);

            var edits = new List<UiAutomationProbeElement>();
            var buttons = new List<UiAutomationProbeElement>();

            foreach (AutomationElement element in all)
            {
                UiAutomationProbeElement? item = Describe(element);
                if (item == null)
                {
                    continue;
                }

                if (item.ControlType == "Edit")
                {
                    edits.Add(item);
                }
                else if (item.ControlType == "Button")
                {
                    buttons.Add(item);
                }
            }

            return new UiAutomationProbeResult
            {
                Success = true,
                WindowName = SafeName(root),
                ClassName = SafeClassName(root),
                TotalElementCount = all.Count,
                EditCount = edits.Count,
                ButtonCount = buttons.Count,
                ValuePatternCount = edits.Count(e => e.SupportsValuePattern),
                InvokePatternCount = buttons.Count(b => b.SupportsInvokePattern),
                Edits = edits,
                Buttons = buttons.Take(20).ToList()
            };
        }
        catch (Exception ex)
        {
            return UiAutomationProbeResult.Failed(
                $"UI Automation 查询失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// 读取元素的当前文本值，验证 <c>ValuePattern</c> 是否可用。
    /// </summary>
    /// <param name="elementHandle">元素原生句柄；为 0 时按 <paramref name="name"/> 查找。</param>
    /// <param name="name">元素名称，用于句柄不可用时定位。</param>
    /// <returns>读取结果。</returns>
    public UiAutomationValueResult ReadValue(IntPtr elementHandle, string? name = null)
    {
        try
        {
            AutomationElement? element = Locate(elementHandle, name);
            if (element == null)
            {
                return UiAutomationValueResult.Failed("未找到目标元素。");
            }

            if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out object? pattern))
            {
                return UiAutomationValueResult.Failed(
                    "目标元素不支持 ValuePattern，无法以设置值方式写入。" +
                    "此类元素需改用逐字符 WM_CHAR 投递。");
            }

            string value = ((ValuePattern)pattern!).Current.Value;
            return new UiAutomationValueResult { Success = true, Value = value };
        }
        catch (Exception ex)
        {
            return UiAutomationValueResult.Failed(
                $"读取元素值失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// 通过 <c>ValuePattern</c> 写入元素文本值。
    /// </summary>
    /// <param name="elementHandle">元素原生句柄；为 0 时按 <paramref name="name"/> 查找。</param>
    /// <param name="name">元素名称，用于句柄不可用时定位。</param>
    /// <param name="text">要写入的文本。</param>
    /// <returns>写入结果。</returns>
    public UiAutomationValueResult WriteValue(IntPtr elementHandle, string? name, string text)
    {
        try
        {
            AutomationElement? element = Locate(elementHandle, name);
            if (element == null)
            {
                return UiAutomationValueResult.Failed("未找到目标元素。");
            }

            if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out object? pattern))
            {
                return UiAutomationValueResult.Failed(
                    "目标元素不支持 ValuePattern，无法以设置值方式写入。" +
                    "此类元素需改用逐字符 WM_CHAR 投递。");
            }

            ((ValuePattern)pattern!).SetValue(text);
            return new UiAutomationValueResult { Success = true };
        }
        catch (Exception ex)
        {
            // UIPI 会在此表现为 COM 异常，需在结果中如实区分
            return UiAutomationValueResult.Failed(
                $"写入元素值失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static AutomationElement? Locate(IntPtr elementHandle, string? name)
    {
        if (elementHandle != IntPtr.Zero)
        {
            return AutomationElement.FromHandle(elementHandle);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return AutomationElement.RootElement.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, name));
    }

    private static UiAutomationProbeElement? Describe(AutomationElement element)
    {
        try
        {
            return new UiAutomationProbeElement
            {
                Name = SafeName(element),
                AutomationId = element.Current.AutomationId ?? string.Empty,
                ControlType = element.Current.ControlType?.ProgrammaticName?.Split('.').LastOrDefault() ?? "?",
                ClassName = element.Current.ClassName ?? string.Empty,
                IsEnabled = element.Current.IsEnabled,
                IsOffscreen = element.Current.IsOffscreen,
                NativeHandle = element.Current.NativeWindowHandle,
                SupportsValuePattern = element.TryGetCurrentPattern(ValuePattern.Pattern, out _),
                SupportsInvokePattern = element.TryGetCurrentPattern(InvokePattern.Pattern, out _)
            };
        }
        catch (Exception)
        {
            // 个别元素在遍历过程中可能失效，跳过即可
            return null;
        }
    }

    private static string SafeName(AutomationElement element)
    {
        try
        {
            return element.Current.Name ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string SafeClassName(AutomationElement element)
    {
        try
        {
            return element.Current.ClassName ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}

/// <summary>
/// 单个 UI Automation 元素的描述信息。
/// </summary>
public sealed class UiAutomationProbeElement
{
    /// <summary>元素名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>自动化标识。</summary>
    public string AutomationId { get; init; } = string.Empty;

    /// <summary>控件类型名，例如 Edit、Button。</summary>
    public string ControlType { get; init; } = string.Empty;

    /// <summary>窗口类名。</summary>
    public string ClassName { get; init; } = string.Empty;

    /// <summary>是否可用。</summary>
    public bool IsEnabled { get; init; }

    /// <summary>是否离屏。</summary>
    public bool IsOffscreen { get; init; }

    /// <summary>原生句柄；多数元素为 0。</summary>
    public int NativeHandle { get; init; }

    /// <summary>是否支持 <c>ValuePattern</c>。</summary>
    public bool SupportsValuePattern { get; init; }

    /// <summary>是否支持 <c>InvokePattern</c>。</summary>
    public bool SupportsInvokePattern { get; init; }
}

/// <summary>
/// UI Automation 探测结果。
/// </summary>
public sealed class UiAutomationProbeResult
{
    /// <summary>探测是否成功。</summary>
    public bool Success { get; init; }

    /// <summary>窗口名称。</summary>
    public string WindowName { get; init; } = string.Empty;

    /// <summary>窗口类名。</summary>
    public string ClassName { get; init; } = string.Empty;

    /// <summary>后代元素总数，用于判断无障碍树是否已就绪。</summary>
    public int TotalElementCount { get; init; }

    /// <summary>Edit（文本框）元素数量。</summary>
    public int EditCount { get; init; }

    /// <summary>Button 元素数量。</summary>
    public int ButtonCount { get; init; }

    /// <summary>支持 <c>ValuePattern</c> 的元素数量。</summary>
    public int ValuePatternCount { get; init; }

    /// <summary>支持 <c>InvokePattern</c> 的元素数量。</summary>
    public int InvokePatternCount { get; init; }

    /// <summary>文本框元素明细。</summary>
    public IReadOnlyList<UiAutomationProbeElement> Edits { get; init; } = Array.Empty<UiAutomationProbeElement>();

    /// <summary>按钮元素明细，最多 20 项。</summary>
    public IReadOnlyList<UiAutomationProbeElement> Buttons { get; init; } = Array.Empty<UiAutomationProbeElement>();

    /// <summary>失败原因。</summary>
    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>
    /// 构造失败结果。
    /// </summary>
    /// <param name="message">失败原因。</param>
    /// <returns>失败结果实例。</returns>
    internal static UiAutomationProbeResult Failed(string message) =>
        new() { Success = false, ErrorMessage = message };
}

/// <summary>
/// UI Automation 读写值结果。
/// </summary>
public sealed class UiAutomationValueResult
{
    /// <summary>操作是否成功。</summary>
    public bool Success { get; init; }

    /// <summary>读到的文本值。</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>失败原因。</summary>
    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>
    /// 构造失败结果。
    /// </summary>
    /// <param name="message">失败原因。</param>
    /// <returns>失败结果实例。</returns>
    internal static UiAutomationValueResult Failed(string message) =>
        new() { Success = false, ErrorMessage = message };
}