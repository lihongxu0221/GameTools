using System.Runtime.InteropServices;
using System.Text;
using GameTools.Core.Models;
using GameTools.Win32.Native;

namespace GameTools.Win32.Helpers;

/// <summary>
/// 窗口探测与枚举辅助类，包含 Windows 7 至 Windows 11 的坐标计算自适应降级。
/// </summary>
public static class WindowHelper
{
    /// <summary>
    /// 获取窗口的可见边界矩形。
    /// Windows 8.1 及以上优先使用 DWM 扩展帧边界以去除不可见的调整边框，
    /// Windows 7 与 8 或 DWM 不可用时降级使用 <c>GetWindowRect</c>。
    /// </summary>
    /// <remarks>
    /// 两种来源语义并不一致：DWM 扩展边界排除了不可见调整边框，而 <c>GetWindowRect</c> 在
    /// DPI 虚拟化下返回被缩放的坐标。若需要与 <c>PrintWindow</c> 绘制区域严格对齐，
    /// 应使用 <see cref="GetPrintWindowSize"/>。
    /// </remarks>
    /// <param name="hWnd">目标窗口句柄。</param>
    public static CaptureBounds GetWindowBounds(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return CaptureBounds.Empty;
        }

        if (OSVersionHelper.IsWindows81OrGreater)
        {
            try
            {
                int hr = DwmApi.DwmGetWindowAttribute(
                    hWnd,
                    NativeConstants.DWMWA_EXTENDED_FRAME_BOUNDS,
                    out RECT dwmRect,
                    Marshal.SizeOf<RECT>());

                if (hr == 0 && dwmRect.Width > 0 && dwmRect.Height > 0)
                {
                    return dwmRect.ToCaptureBounds();
                }
            }
            catch (DllNotFoundException)
            {
                // dwmapi 不可用时降级
            }
            catch (EntryPointNotFoundException)
            {
                // 符号缺失时降级
            }
        }

        if (User32.GetWindowRect(hWnd, out RECT rect))
        {
            return rect.ToCaptureBounds();
        }

        return CaptureBounds.Empty;
    }

    /// <summary>
    /// 获取与 <c>PrintWindow</c> 绘制区域严格一致的位图尺寸。
    /// </summary>
    /// <remarks>
    /// <c>PrintWindow</c> 始终按窗口完整外框（包含不可见调整边框）绘制，而
    /// <see cref="GetWindowBounds"/> 在 DWM 可用时会排除该边框。若直接把 DWM 边界当作位图尺寸，
    /// 会出现右侧与底部被裁切的结果，因此位图分配一律以本方法返回的尺寸为准。
    /// </remarks>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <returns>位图宽高；窗口无效时返回 0,0。</returns>
    public static (int Width, int Height) GetPrintWindowSize(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return (0, 0);
        }

        if (User32.GetWindowRect(hWnd, out RECT rect) && rect.Width > 0 && rect.Height > 0)
        {
            return (rect.Width, rect.Height);
        }

        CaptureBounds bounds = GetWindowBounds(hWnd);
        return (bounds.Width, bounds.Height);
    }

    /// <summary>
    /// 获取窗口外框在屏幕上的矩形（含不可见缩放边框）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与 <see cref="GetWindowBounds"/> 的区别是本方法使用 <c>GetWindowRect</c>，
    /// 结果包含 DWM 扩展边界所排除的不可见缩放边框，实测两者每侧相差约 7 像素。
    /// </para>
    /// <para>
    /// <b>捕获帧的坐标系以本方法为准</b>：<c>PrintWindow</c> 按完整外框绘制，
    /// 位图也按外框尺寸分配，因此帧内原点是外框左上角。用 DWM 边界当原点会
    /// 产生系统性的坐标偏移。涉及「捕获帧坐标 ↔ 屏幕坐标」换算的场景必须使用本方法。
    /// </para>
    /// </remarks>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <returns>窗口外框矩形；句柄无效或调用失败时返回 <see cref="CaptureBounds.Empty"/>。</returns>
    public static CaptureBounds GetOuterFrameBounds(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return CaptureBounds.Empty;
        }

        return User32.GetWindowRect(hWnd, out RECT rect) && rect.Width > 0 && rect.Height > 0
            ? new CaptureBounds(rect.Left, rect.Top, rect.Width, rect.Height)
            : CaptureBounds.Empty;
    }

    /// <summary>
    /// 获取指定窗口的详细信息。
    /// </summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    public static WindowInfo? GetWindowInfo(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return null;
        }

        var sbTitle = new StringBuilder(512);
        User32.GetWindowText(hWnd, sbTitle, sbTitle.Capacity);
        string title = sbTitle.ToString();

        var sbClass = new StringBuilder(256);
        User32.GetClassName(hWnd, sbClass, sbClass.Capacity);
        string className = sbClass.ToString();

        User32.GetWindowThreadProcessId(hWnd, out uint pid);
        string procName = ProcessInfoCache.TryGetProcessName((int)pid);

        return new WindowInfo(
            hWnd,
            title,
            className,
            GetWindowBounds(hWnd),
            (int)pid,
            procName,
            User32.IsWindowVisible(hWnd),
            User32.IsIconic(hWnd));
    }

    /// <summary>
    /// 枚举所有可见且有标题的顶级窗口。
    /// </summary>
    public static IReadOnlyList<WindowInfo> FindTopLevelWindows()
    {
        var list = new List<WindowInfo>();

        User32.EnumWindows((hWnd, _) =>
        {
            if (User32.IsWindowVisible(hWnd))
            {
                var info = GetWindowInfo(hWnd);
                if (info != null && !string.IsNullOrWhiteSpace(info.Title))
                {
                    list.Add(info);
                }
            }
            return true;
        }, IntPtr.Zero);

        return list;
    }

    /// <summary>
    /// 根据标题关键字查找窗口。
    /// </summary>
    /// <param name="titleKeyword">标题关键字，忽略大小写。</param>
    public static IReadOnlyList<WindowInfo> FindWindowsByTitle(string titleKeyword)
    {
        if (string.IsNullOrEmpty(titleKeyword))
        {
            return Array.Empty<WindowInfo>();
        }

        return FindTopLevelWindows()
            .Where(w => w.Title.IndexOf(titleKeyword, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
    }
}
