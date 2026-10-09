using System.Text;
using GameTools.Core.Models;
using GameTools.Win32.Native;

namespace GameTools.Win32.Helpers;

/// <summary>
/// 传统 Win32 控件的发现与操作辅助。
/// </summary>
/// <remarks>
/// <para>
/// 仅适用于暴露传统子控件的程序（MFC、WinForms、原生对话框）。实测现代应用
/// 基本不适用：Grok Bot（Chromium 内核）仅 1 个 <c>Intermediate D3D Window</c>
/// 子窗口，WPF 控件没有独立 HWND，资源管理器使用 DirectUI 自绘。
/// </para>
/// <para>
/// 因此该能力在整体方案中位于降级链末位，仅作为遗留程序的兜底；
/// 现代应用应优先使用 UI Automation。
/// </para>
/// </remarks>
public static class LegacyControlHelper
{
    /// <summary>
    /// 枚举窗口的全部后代控件（含孙控件）。
    /// </summary>
    /// <param name="hWnd">根窗口句柄。</param>
    /// <returns>后代控件句柄列表；句柄无效时返回空列表。</returns>
    public static IReadOnlyList<IntPtr> EnumerateDescendants(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !User32.IsWindow(hWnd))
        {
            return Array.Empty<IntPtr>();
        }

        var found = new List<IntPtr>();

        // 委托必须在枚举期间保持存活，否则可能被 GC 回收导致回调中断
        bool completed = User32.EnumChildWindows(
            hWnd,
            (child, _) =>
            {
                found.Add(child);
                return true;
            },
            IntPtr.Zero);

        if (!completed)
        {
            found.Clear();
        }

        return found;
    }

    /// <summary>
    /// 按控件类名筛选后代控件。
    /// </summary>
    /// <param name="hWnd">根窗口句柄。</param>
    /// <param name="classNames">允许的类名集合，比较时忽略大小写。</param>
    /// <returns>匹配的控件句柄列表。</returns>
    public static IReadOnlyList<IntPtr> EnumerateByClass(IntPtr hWnd, IEnumerable<string> classNames)
    {
        var wanted = new HashSet<string>(classNames, StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
        {
            return Array.Empty<IntPtr>();
        }

        return EnumerateDescendants(hWnd)
            .Where(child => wanted.Contains(GetClassName(child)))
            .ToList();
    }

    /// <summary>
    /// 查找首个文本可编辑控件。
    /// </summary>
    /// <param name="hWnd">根窗口句柄。</param>
    /// <returns>控件句柄；未找到时返回 <see cref="IntPtr.Zero"/>。</returns>
    public static IntPtr FindFirstTextControl(IntPtr hWnd)
    {
        IReadOnlyList<IntPtr> candidates = EnumerateByClass(
            hWnd,
            new[]
            {
                NativeConstants.ClassEdit,
                NativeConstants.ClassRichEdit,
                NativeConstants.ClassRichEditClass
            });

        return candidates.FirstOrDefault();
    }

    /// <summary>
    /// 查找首个可用按钮控件。
    /// </summary>
    /// <param name="hWnd">根窗口句柄。</param>
    /// <returns>控件句柄；未找到时返回 <see cref="IntPtr.Zero"/>。</returns>
    public static IntPtr FindFirstButton(IntPtr hWnd)
    {
        IReadOnlyList<IntPtr> candidates = EnumerateByClass(hWnd, new[] { NativeConstants.ClassButton });
        return candidates.FirstOrDefault(IsUsableButton);
    }

    /// <summary>
    /// 判断按钮是否处于可点击状态。
    /// </summary>
    /// <remarks>
    /// 禁用态按钮接收 <c>BM_CLICK</c> 不会产生任何动作，且部分程序会直接返回失败，
    /// 因此点击前必须先过滤，避免把无效操作报告为成功。
    /// </remarks>
    /// <param name="hWnd">按钮句柄。</param>
    /// <returns>可用时返回 <c>true</c>。</returns>
    public static bool IsUsableButton(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !User32.IsWindow(hWnd))
        {
            return false;
        }

        if (!User32.IsWindowVisible(hWnd) || !User32.IsWindowEnabled(hWnd))
        {
            return false;
        }

        IntPtr state = User32.SendMessage(hWnd, NativeConstants.BM_GETSTATE, IntPtr.Zero, IntPtr.Zero);
        long value = state.ToInt64();

        // BM_GETSTATE 返回值高位为按下状态，低三位为按钮风格
        bool pushed = (value & 0x00000008L) != 0;

        // 位 0 为 BS_PUSHBUTTON / BS_DEFPUSHBUTTON；实心按钮必须处于抬起状态才可点击
        bool isPushButton = (value & 0x0FL) == 0x00;

        return !(isPushButton && pushed);
    }

    /// <summary>
    /// 将捕获帧坐标系中的点位转换为窗口客户区坐标。
    /// </summary>
    /// <remarks>
    /// 特征识图的命中点位位于捕获帧坐标系。当捕获源为 <c>PrintWindow</c> 时，
    /// 该坐标系与窗口外框坐标一致，仅需扣除客户区偏移；当捕获源为屏幕
    /// <c>BitBlt</c> 时则已是屏幕坐标，需要先换算。
    /// </remarks>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="capturePoint">捕获帧坐标系中的点位。</param>
    /// <param name="originScreenX">捕获区域左上角的屏幕 X 坐标。</param>
    /// <param name="originScreenY">捕获区域左上角的屏幕 Y 坐标。</param>
    /// <param name="clientPoint">转换后的客户区坐标。</param>
    /// <returns>转换是否成功。</returns>
    public static bool TryToClientPoint(
        IntPtr hWnd,
        POINT capturePoint,
        int originScreenX,
        int originScreenY,
        out POINT clientPoint)
    {
        clientPoint = new POINT(capturePoint.X, capturePoint.Y);

        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        // 捕获帧坐标 → 屏幕坐标
        POINT screenPoint = new(clientPoint.X + originScreenX, clientPoint.Y + originScreenY);

        if (!User32.ScreenToClient(hWnd, ref screenPoint))
        {
            return false;
        }

        clientPoint = screenPoint;
        return true;
    }

    /// <summary>
    /// 按命中点解析最深子窗口句柄。
    /// </summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="clientPoint">客户区坐标。</param>
    /// <returns>命中的窗口句柄；未命中时回退为传入窗口本身。</returns>
    public static IntPtr ResolveDeepestWindow(IntPtr hWnd, POINT clientPoint)
    {
        if (hWnd == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr hit = User32.ChildWindowFromPointEx(
            hWnd,
            clientPoint,
            NativeConstants.CWP_SKIPINVISIBLE | NativeConstants.CWP_SKIPTRANSPARENT);

        return hit != IntPtr.Zero ? hit : hWnd;
    }

    /// <summary>
    /// 读取窗口类名。
    /// </summary>
    /// <param name="hWnd">窗口句柄。</param>
    /// <returns>类名；读取失败时返回空字符串。</returns>
    public static string GetClassName(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(256);
        int length = User32.GetClassName(hWnd, builder, builder.Capacity);
        return length > 0 ? builder.ToString() : string.Empty;
    }

    /// <summary>
    /// 读取窗口文本。
    /// </summary>
    /// <param name="hWnd">窗口句柄。</param>
    /// <returns>窗口文本；读取失败时返回空字符串。</returns>
    public static string GetWindowTextValue(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(1024);
        int length = User32.GetWindowText(hWnd, builder, builder.Capacity);
        return length > 0 ? builder.ToString() : string.Empty;
    }
}
