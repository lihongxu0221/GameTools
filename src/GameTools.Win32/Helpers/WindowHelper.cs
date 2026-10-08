using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using GameTools.Core.Models;
using GameTools.Win32.Native;

namespace GameTools.Win32.Helpers;

/// <summary>
/// 窗口探测与枚举辅助类，包含 Win7 到 Win11 的坐标计算自适应降级
/// </summary>
public static class WindowHelper
{
    /// <summary>
    /// 获取窗口的精准外接矩形
    /// Windows 8/10/11: 优先使用 DwmGetWindowAttribute 去除 Aero 隐形阴影
    /// Windows 7 / DWM 未启用时: 降级使用 GetWindowRect
    /// </summary>
    public static Rectangle GetWindowBounds(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return Rectangle.Empty;
        }

        // 仅在 Win8+ 且 DWM 启用时尝试 DWMWA_EXTENDED_FRAME_BOUNDS
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
                    return dwmRect.ToRectangle();
                }
            }
            catch
            {
                // dwmapi 加载或调用异常时降级
            }
        }

        if (User32.GetWindowRect(hWnd, out RECT rect))
        {
            return rect.ToRectangle();
        }

        return Rectangle.Empty;
    }

    /// <summary>
    /// 获取指定窗口的详细模型信息
    /// </summary>
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
        string procName = string.Empty;
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            procName = proc.ProcessName;
        }
        catch
        {
            procName = "Unknown";
        }

        var bounds = GetWindowBounds(hWnd);
        bool isVisible = User32.IsWindowVisible(hWnd);
        bool isMinimized = User32.IsIconic(hWnd);

        return new WindowInfo
        {
            Handle = hWnd,
            Title = title,
            ClassName = className,
            Bounds = bounds,
            ProcessId = (int)pid,
            ProcessName = procName,
            IsVisible = isVisible,
            IsMinimized = isMinimized
        };
    }

    /// <summary>
    /// 枚举所有顶级可见窗口
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
    /// 根据标题关键字查找窗口
    /// </summary>
    public static IReadOnlyList<WindowInfo> FindWindowsByTitle(string titleKeyword)
    {
        return FindTopLevelWindows()
            .Where(w => w.Title.IndexOf(titleKeyword, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
    }
}
