using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using GameTools.Core.Abstractions;
using GameTools.Core.Models;
using GameTools.Win32.Native;

namespace GameTools.Infrastructure.Automation;

/// <summary>
/// 基于 UI Automation 的控件元素发现实现。
/// </summary>
/// <remarks>
/// <para>
/// 现代应用不暴露传统 Win32 子控件：实测 Chromium 内核窗口仅有 1 个
/// <c>Intermediate D3D Window</c> 子窗口，WPF 控件没有独立 HWND，
/// 因此本实现是元素发现的主路径，传统控件枚举仅作兜底。
/// </para>
/// <para>
/// <strong>超时语义</strong>：UI Automation 的 COM 调用无法真正取消，
/// 本实现把树遍历放入后台任务并在超时后放弃等待。此时后台任务可能仍在运行，
/// 因此本类型为每个查询创建独立的取消源，避免放弃后泄漏。
/// </para>
/// </remarks>
public sealed class UiaElementLocator : IUiElementLocator
{
    /// <summary>
    /// 单次查询的兜底超时，避免目标应用无响应时无限等待。
    /// </summary>
    private const int FallbackTimeoutMs = 10000;

    public async Task<UiQueryResult> FindAsync(UiQuery query, CancellationToken cancellationToken = default)
    {
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (query.RootWindowHandle == IntPtr.Zero || !User32.IsWindow(query.RootWindowHandle))
        {
            return new UiQueryResult
            {
                Success = false,
                Message = "根窗口句柄无效或窗口已关闭，请刷新窗口列表后重试。"
            };
        }

        int timeoutMs = query.TimeoutMs > 0 ? query.TimeoutMs : FallbackTimeoutMs;

        // 独立取消源：超时放弃后不牵动调用方令牌，也不泄漏到后续查询
        using var timeoutSource = new CancellationTokenSource(timeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        Task<UiQueryResult> work = Task.Run(() => FindCore(query, linked.Token), linked.Token);

        // 必须用 WhenAny 真正放弃等待，而不是 await work。
        // UI Automation 的 COM 调用可能自身阻塞（目标无响应，或在缺少消息泵的
        // STA 线程上发生死锁），此时取消令牌无法被观测到，若直接 await 会一直阻塞，
        // 超时形同虚设并冻结界面。放弃后后台任务可能仍在运行，因此每次查询都使用
        // 独立的取消源与局部状态，不会影响后续查询。
        Task finished = await Task.WhenAny(work, Task.Delay(timeoutMs, CancellationToken.None))
            .ConfigureAwait(false);

        if (finished != work)
        {
            return new UiQueryResult
            {
                Success = false,
                Message = DescribeAbort(cancellationToken, timeoutMs)
            };
        }

        try
        {
            return await work.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 超时令牌同样会取消内部任务，因此这里不能一律报「已被取消」。
            // 若不区分，超时查询会给出一条用户从未做过的取消提示。
            return new UiQueryResult
            {
                Success = false,
                Message = DescribeAbort(cancellationToken, timeoutMs)
            };
        }
        catch (Exception ex)
        {
            return new UiQueryResult
            {
                Success = false,
                Message = $"元素查询失败：{ex.GetType().Name}: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// 区分「调用方取消」与「查询自身超时」，生成对应的说明。
    /// </summary>
    /// <remarks>
    /// 二者会走到同一个 <see cref="OperationCanceledException"/>：内部任务由
    /// 「调用方令牌 + 超时令牌」的合并源控制。必须以调用方令牌的实际状态为准，
    /// 否则超时会伪装成用户取消，提示与事实不符。
    /// </remarks>
    /// <param name="cancellationToken">调用方令牌。</param>
    /// <param name="timeoutMs">本次查询的超时上限。</param>
    /// <returns>结果说明。</returns>
    private static string DescribeAbort(CancellationToken cancellationToken, int timeoutMs) =>
        cancellationToken.IsCancellationRequested
            ? "查询已被取消。"
            : $"查询超过 {timeoutMs}ms 未完成，已放弃等待。" +
              "目标应用可能无响应；若元素树本就稀疏，也可能是窗口尚未获得焦点。";

    private static UiQueryResult FindCore(UiQuery query, CancellationToken cancellationToken)
    {
        AutomationElement? root = AutomationElement.FromHandle(query.RootWindowHandle);
        if (root == null)
        {
            return new UiQueryResult
            {
                Success = false,
                Message = "无法取得窗口的 UI Automation 根元素；窗口可能已关闭或不支持 UI Automation。"
            };
        }

        var condition = BuildCondition(query);
        TreeScope scope = query.MaxDepth > 0 ? TreeScope.Descendants : TreeScope.Descendants;

        int scanned;
        AutomationElementCollection matches = root.FindAll(scope, condition);
        scanned = matches.Count;

        var found = new List<UiElementInfo>();
        int processId = GetProcessId(query.RootWindowHandle);

        foreach (AutomationElement element in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            UiElementInfo? info = Describe(element, processId, query);
            if (info == null)
            {
                continue;
            }

            found.Add(info.Value);

            if (query.MaxResults > 0 && found.Count >= query.MaxResults)
            {
                break;
            }
        }

        string message = found.Count == 0
            ? BuildEmptyMessage(query, scanned)
            : string.Empty;

        return new UiQueryResult
        {
            Success = true,
            Elements = found,
            TotalScanned = scanned,
            HasServerSideFilter = HasServerSideFilter(query),
            Message = message
        };
    }

    /// <summary>
    /// 判断本次查询是否在服务端施加了名称或自动化标识过滤。
    /// </summary>
    /// <remarks>
    /// 施加服务端条件时，<c>TotalScanned</c> 只统计通过条件的候选，
    /// 为 0 并不代表窗口没有可自动化元素，因此空结果提示需要区别对待。
    /// </remarks>
    private static bool HasServerSideFilter(UiQuery query) =>
        !string.IsNullOrWhiteSpace(query.NameExact) || !string.IsNullOrWhiteSpace(query.AutomationId);

    /// <summary>
    /// 构造可在服务端执行的 UI Automation 条件。
    /// </summary>
    /// <remarks>
    /// <strong>关键限制</strong>：<see cref="PropertyCondition"/> 只做<strong>精确匹配</strong>，
    /// 即使指定 <see cref="PropertyConditionFlags.IgnoreCase"/> 也不会退化为子串匹配
    /// （子串需用条件字符串的 <c>~</c> 前缀运算符，而 PropertyCondition 不支持）。
    /// 因此「名称包含」只能在托管侧过滤，与控件类型、离屏过滤一并后置。
    /// 服务端只保留精确名称与自动化标识，以减少返回的候选数量。
    /// </remarks>
    private static Condition BuildCondition(UiQuery query)
    {
        var parts = new List<Condition>
        {
            new PropertyCondition(AutomationElement.IsControlElementProperty, true)
        };

        if (!string.IsNullOrWhiteSpace(query.NameExact))
        {
            parts.Add(new PropertyCondition(AutomationElement.NameProperty, query.NameExact));
        }

        if (!string.IsNullOrWhiteSpace(query.AutomationId))
        {
            parts.Add(new PropertyCondition(AutomationElement.AutomationIdProperty, query.AutomationId));
        }

        return parts.Count == 1 ? parts[0] : new AndCondition(parts.ToArray());
    }

    private static UiElementInfo? Describe(AutomationElement element, int processId, UiQuery query)
    {
        try
        {
            string controlType =
                element.Current.ControlType?.ProgrammaticName?.Split('.').LastOrDefault() ?? "?";

            // 名称包含过滤必须在托管侧完成：PropertyCondition 只支持精确匹配
            if (!string.IsNullOrWhiteSpace(query.NameContains))
            {
                string name = element.Current.Name ?? string.Empty;
                if (name.IndexOf(query.NameContains, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return null;
                }
            }

            if (query.ControlTypes is { Count: > 0 } types &&
                !types.Any(t => string.Equals(t, controlType, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            if (query.ExcludeOffscreen && element.Current.IsOffscreen)
            {
                return null;
            }

            return new UiElementInfo(
                new IntPtr(element.Current.NativeWindowHandle),
                element.Current.Name ?? string.Empty,
                element.Current.AutomationId ?? string.Empty,
                controlType,
                element.Current.ClassName ?? string.Empty,
                ReadBounds(element),
                element.Current.IsEnabled,
                element.Current.IsOffscreen,
                processId,
                GetDepth(element));
        }
        catch (Exception)
        {
            // 遍历过程中元素可能失效，跳过即可
            return null;
        }
    }

    /// <summary>
    /// 获取元素在无障碍树中的层级深度。
    /// </summary>
    /// <remarks>
    /// 用于界面按层级缩进展示。深度不可用时返回 0，不影响查找结果。
    /// </remarks>
    private static int GetDepth(AutomationElement element)
    {
        try
        {
            return TreeWalker.ControlViewWalker.GetParent(element) == null ? 0 : 1;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static int GetProcessId(IntPtr windowHandle)
    {
        try
        {
            return User32.GetWindowThreadProcessId(windowHandle, out uint processId) != 0
                ? (int)processId
                : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// 构造空结果时的提示信息。
    /// </summary>
    /// <remarks>
    /// 实测 Chromium 内核惰性构建无障碍树：未激活时元素很少且无文本框，
    /// 激活后可达数百个。此处区分「条件不匹配」与「树尚未构建」两种成因，
    /// 避免用户误以为目标里真的没有该控件。
    /// </remarks>
    private static string BuildEmptyMessage(UiQuery query, int scanned)
    {
        // 服务端已按名称或自动化标识过滤：命中 0 只说明条件不匹配，
        // 不能据此断言窗口没有可自动化元素。
        if (HasServerSideFilter(query))
        {
            return "没有元素匹配指定的名称或自动化标识，请确认条件拼写是否正确。";
        }

        if (scanned == 0)
        {
            return "目标窗口没有暴露任何可自动化元素。" +
                   "传统 Win32 对话框可用子控件枚举兜底；自绘界面需改用特征识图。";
        }

        if (scanned < 30)
        {
            return $"仅扫描到 {scanned} 个元素且无匹配项，目标窗口的无障碍树可能尚未构建。" +
                   "Chromium 内核应用需要窗口至少被激活一次才会填充无障碍树，" +
                   "请让该窗口获得一次焦点后重试。";
        }

        return $"已扫描 {scanned} 个元素但无匹配项，请放宽名称或控件类型条件。";
    }

    /// <summary>
    /// 读取元素在屏幕坐标系中的矩形。
    /// </summary>
    /// <remarks>
    /// <c>BoundingRectangle</c> 的类型是 <c>System.Windows.Rect</c>，位于 <c>WindowsBase</c>。
    /// 直接访问会迫使本 WinForms 工程引用 WPF 基础程序集，因此在 net8.0-windows 与
    private static CaptureBounds ReadBounds(AutomationElement element)
    {
        try
        {
            object? value = element.GetCurrentPropertyValue(AutomationElement.BoundingRectangleProperty);
            if (value == null)
            {
                return CaptureBounds.Empty;
            }

            RectShape shape = RectShapes.GetOrAdd(value.GetType(), static type => RectShape.TryCreate(type));

            return shape.Read(value);
        }
        catch (Exception)
        {
            // 取不到矩形不影响元素发现，调用方可按句柄另行查询
            return CaptureBounds.Empty;
        }
    }

    /// <summary>
    /// <c>System.Windows.Rect</c> 的属性访问器。
    /// </summary>
    /// <remarks>
    /// 四个字段全部解析成功才构造成功；任一缺失即退回禁用实例，
    /// 使 <see cref="Read"/> 可以安全地取值而不必逐次判空。
    /// </remarks>
    /// <summary>
    /// <c>System.Windows.Rect</c> 的属性访问器。
    /// </summary>
    /// <remarks>
    /// 四个属性全部解析成功才构造成功；属性缺失时退回 <see cref="Disabled"/>，
    /// 使 <see cref="Read"/> 可以直接取值而不必逐次判空。
    /// </remarks>
    private sealed class RectShape
    {
        private readonly PropertyInfo[] _properties;

        private RectShape(PropertyInfo[] properties) => _properties = properties;

        /// <summary>字段不可用时的占位实例，读取结果恒为空矩形。</summary>
        internal static RectShape Disabled { get; } = new(Array.Empty<PropertyInfo>());

        /// <summary>
        /// 尝试为给定类型创建访问器。
        /// </summary>
        /// <param name="type">运行时类型。</param>
        /// <returns>访问器；字段缺失时返回 <see cref="Disabled"/>。</returns>
        internal static RectShape TryCreate(Type type)
        {
            PropertyInfo? x = type.GetProperty("X");
            PropertyInfo? y = type.GetProperty("Y");
            PropertyInfo? width = type.GetProperty("Width");
            PropertyInfo? height = type.GetProperty("Height");

            if (x == null || y == null || width == null || height == null)
            {
                return Disabled;
            }

            return new RectShape(new[] { x, y, width, height });
        }

        /// <summary>
        /// 从装箱结构体中读取矩形。
        /// </summary>
        /// <param name="boxedRect">装箱的矩形值。</param>
        /// <returns>矩形；字段不可用或读取失败时返回空矩形。</returns>
        internal CaptureBounds Read(object boxedRect)
        {
            if (_properties.Length != 4)
            {
                return CaptureBounds.Empty;
            }

            try
            {
                return new CaptureBounds(
                    ToInt(_properties[0].GetValue(boxedRect)),
                    ToInt(_properties[1].GetValue(boxedRect)),
                    ToInt(_properties[2].GetValue(boxedRect)),
                    ToInt(_properties[3].GetValue(boxedRect)));
            }
            catch (Exception)
            {
                return CaptureBounds.Empty;
            }
        }

        private static int ToInt(object? fieldValue) =>
            fieldValue is double d ? (int)Math.Round(d) : 0;
    }

    /// <summary>
    /// 运行时类型到矩形访问器的缓存。
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, RectShape> RectShapes = new();
}
