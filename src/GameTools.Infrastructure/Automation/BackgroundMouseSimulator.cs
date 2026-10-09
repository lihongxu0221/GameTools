using System;
using System.Runtime.InteropServices;
using GameTools.Core.Enums;
using GameTools.Win32.Native;

namespace GameTools.Infrastructure.Automation;

/// <summary>
/// 后台模拟左键点击。
/// </summary>
/// <remarks>
/// <para>
/// <strong>核心约束</strong>：后台操作不得影响用户的实体鼠标。
/// 这意味着不得使用输入注入（<c>SendInput</c>、<c>mouse_event</c>），
/// 也不得在默认路径上调用 <c>SetCursorPos</c>——前者会伪造真实的输入事件，
/// 后者会直接搬走用户的物理光标。二者都会让用户正在进行的操作被劫持。
/// </para>
/// <para>
/// <strong>捕获处理</strong>：目标程序收到鼠标按下消息后可能调用
/// <c>SetCapture</c> 把自己设为鼠标捕获者。此后实体鼠标移出该窗口时，
/// 点击依然被它接收，表现为光标「被困住」。这是消息投递路径上唯一
/// 会真正影响实体鼠标的情形，因此点击前后都要检查并清理。
/// </para>
/// <para>
/// <strong>投递方式的选择</strong>：纯消息投递时物理光标不会真的移到目标上，
/// 依赖悬停才触发的交互不会响应。<see cref="MouseDispatchStrategy.MoveAndRestore"/>
/// 通过临时移动并还原光标来覆盖该场景，但期间会短暂占用实体鼠标，
/// 属用户明确选择后的取舍，而非默认行为。
/// </para>
/// </remarks>
public static class BackgroundMouseSimulator
{
    /// <summary>
    /// 投递移动消息后等待的毫秒数，让悬停态先生效。
    /// </summary>
    private const int HoverDelayMs = 60;

    /// <summary>
    /// 按下与抬起之间的毫秒数，部分控件依赖该间隔。
    /// </summary>
    private const int PressDelayMs = 40;

    /// <summary>
    /// 移动光标后等待的毫秒数，给目标程序处理悬停进入的时间。
    /// </summary>
    private const int MoveSettleDelayMs = 80;

    /// <summary>
    /// 在指定屏幕坐标模拟一次左键点击。
    /// </summary>
    /// <param name="windowHandle">接收消息的目标窗口句柄。</param>
    /// <param name="screenX">目标屏幕横坐标。</param>
    /// <param name="screenY">目标屏幕纵坐标。</param>
    /// <param name="strategy">投递方式。</param>
    /// <param name="cursorWasMoved">
    /// 输出物理光标是否被移动过。仅 <see cref="MouseDispatchStrategy.MoveAndRestore"/>
    /// 会置为真，用于让调用方如实回显给用户。
    /// </param>
    /// <param name="failure">失败原因；成功时为 <c>null</c>。</param>
    /// <returns>是否已成功投递点击。</returns>
    public static bool TryClick(
        IntPtr windowHandle,
        int screenX,
        int screenY,
        MouseDispatchStrategy strategy,
        out bool cursorWasMoved,
        out string? failure)
    {
        cursorWasMoved = false;

        if (windowHandle == IntPtr.Zero || !User32.IsWindow(windowHandle))
        {
            failure = "目标窗口句柄无效或窗口已关闭。";
            return false;
        }

        if (!User32.IsWindowVisible(windowHandle))
        {
            failure = "目标窗口不可见。";
            return false;
        }

        // 屏幕坐标先换算为窗口客户区坐标：投递的消息用的是客户区坐标，
        // 直接把屏幕坐标当客户区坐标会在非零偏移的窗口上落到错误位置。
        var screenPoint = new POINT(screenX, screenY);
        POINT clientPoint = screenPoint;

        if (!User32.ScreenToClient(windowHandle, ref clientPoint))
        {
            failure = "屏幕坐标到客户区坐标换算失败。";
            return false;
        }

        IntPtr lParam = PackPoint(clientPoint);

        // 点击前释放可能残留的捕获：若上一次点击后目标仍持有捕获，
        // 实体鼠标此时已经处于被困状态。
        ReleaseCaptureIfOwned(windowHandle);

        bool dispatched = strategy switch
        {
            MouseDispatchStrategy.MoveAndRestore =>
                ClickWithCursorMove(windowHandle, screenX, screenY, lParam, out cursorWasMoved, out failure),
            _ =>
                ClickWithMessagesOnly(windowHandle, lParam, out failure)
        };

        if (!dispatched)
        {
            return false;
        }

        // 点击后目标可能因处理按下消息而取得捕获，必须清理，
        // 否则用户的实体鼠标会被困在该窗口内。
        ReleaseCaptureIfOwned(windowHandle);

        return true;
    }

    /// <summary>
    /// 纯消息投递：完全不触碰物理光标。
    /// </summary>
    private static bool ClickWithMessagesOnly(IntPtr windowHandle, IntPtr lParam, out string? failure)
    {
        failure = null;

        try
        {
            // 先移动：悬停后才显示的控件需要先收到移动消息
            if (!User32.PostMessage(windowHandle, NativeConstants.WM_MOUSEMOVE, IntPtr.Zero, lParam))
            {
                failure = DescribePostFailure("WM_MOUSEMOVE");
                return false;
            }

            System.Threading.Thread.Sleep(HoverDelayMs);

            // 按下时 wParam 须带 MK_LBUTTON
            if (!User32.PostMessage(windowHandle, NativeConstants.WM_LBUTTONDOWN, new IntPtr(1), lParam))
            {
                failure = DescribePostFailure("WM_LBUTTONDOWN");
                return false;
            }

            System.Threading.Thread.Sleep(PressDelayMs);

            if (!User32.PostMessage(windowHandle, NativeConstants.WM_LBUTTONUP, IntPtr.Zero, lParam))
            {
                failure = DescribePostFailure("WM_LBUTTONUP");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            failure = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 临时移动物理光标，点击后立即还原。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 该路径会短暂占用实体鼠标，是用户显式选择的取舍。实现上务必保证：
    /// </para>
    /// <list type="number">
    /// <item>记录原位置并在 <c>finally</c> 中还原，异常路径不得遗漏；</item>
    /// <item>还原前检查光标是否仍在目标位置——若用户已自行移动，
    /// 说明这段时间他在操作鼠标，此时强行拉回会造成更大干扰，应放弃还原。</item>
    /// </list>
    /// </remarks>
    private static bool ClickWithCursorMove(
        IntPtr windowHandle,
        int screenX,
        int screenY,
        IntPtr lParam,
        out bool cursorWasMoved,
        out string? failure)
    {
        cursorWasMoved = false;
        failure = null;

        if (!User32.GetCursorPos(out POINT original))
        {
            failure = "无法读取当前光标位置，已放弃移动光标以免影响实体鼠标。";
            return false;
        }

        bool moved = false;

        try
        {
            if (!User32.SetCursorPos(screenX, screenY))
            {
                failure = $"移动光标到 ({screenX},{screenY}) 失败，错误码 {Marshal.GetLastWin32Error()}。";
                return false;
            }

            moved = true;
            cursorWasMoved = true;

            // 让目标先处理悬停进入，部分控件只有在悬停后才会响应点击
            System.Threading.Thread.Sleep(MoveSettleDelayMs);

            if (!User32.PostMessage(windowHandle, NativeConstants.WM_LBUTTONDOWN, new IntPtr(1), lParam))
            {
                failure = DescribePostFailure("WM_LBUTTONDOWN");
                return false;
            }

            System.Threading.Thread.Sleep(PressDelayMs);

            if (!User32.PostMessage(windowHandle, NativeConstants.WM_LBUTTONUP, IntPtr.Zero, lParam))
            {
                failure = DescribePostFailure("WM_LBUTTONUP");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            failure = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
        finally
        {
            if (moved)
            {
                RestoreCursorIfUnmoved(original, screenX, screenY);
            }
        }
    }

    /// <summary>
    /// 还原光标位置，但仅当用户在此期间没有移动过光标。
    /// </summary>
    /// <remarks>
    /// 若当前光标已不在目标位置，说明用户正在使用鼠标。此时把他拉回原处
    /// 比留下偏移更糟：前者会让他的下一次点击落在意料之外的位置。
    /// 宁可留下一个偏移并如实告知，也不要打断用户的操作。
    /// </remarks>
    private static void RestoreCursorIfUnmoved(POINT original, int targetX, int targetY)
    {
        if (!User32.GetCursorPos(out POINT current))
        {
            return;
        }

        if (current.X != targetX || current.Y != targetY)
        {
            // 用户已自行移动光标，不干预
            return;
        }

        User32.SetCursorPos(original.X, original.Y);
    }

    /// <summary>
    /// 当鼠标捕获者为目标窗口自身或其后代时释放捕获。
    /// </summary>
    /// <remarks>
    /// 只处理「由本次点击导致」的捕获，不动与目标无关的捕获——
    /// 那属于用户或其他程序正在进行拖拽等操作，贸然释放会造成破坏。
    /// </remarks>
    private static void ReleaseCaptureIfOwned(IntPtr windowHandle)
    {
        IntPtr captured = User32.GetCapture();

        if (captured == IntPtr.Zero)
        {
            return;
        }

        // 捕获窗口必须是目标窗口本身或其子窗口，才认为是本次点击造成的
        bool owned = captured == windowHandle ||
                     User32.GetParent(captured) == windowHandle ||
                     IsDescendantOf(captured, windowHandle);

        if (owned)
        {
            User32.ReleaseCapture();
        }
    }

    /// <summary>
    /// 判断句柄是否为祖先窗口的后代。
    /// </summary>
    private static bool IsDescendantOf(IntPtr candidate, IntPtr ancestor)
    {
        IntPtr current = candidate;
        int guard = 0;

        // 层级深度有限，但仍设上限以防句柄环导致死循环
        while (current != IntPtr.Zero && guard < 64)
        {
            if (current == ancestor)
            {
                return true;
            }

            current = User32.GetParent(current);
            guard++;
        }

        return false;
    }

    /// <summary>
    /// 按 Win32 约定把客户区坐标打包进 <c>lParam</c>：低字为 X，高字为 Y。
    /// </summary>
    private static IntPtr PackPoint(POINT point) =>
        new((point.Y << 16) | (point.X & 0xFFFF));

    /// <summary>
    /// 生成消息投递失败的说明。
    /// </summary>
    /// <remarks>
    /// 必须检查返回值：<c>PostMessage</c> 返回 false 表示消息未进入队列
    /// （多因 UIPI 拦截或队列已满），此时若继续执行会报告「已点击」，
    /// 而目标其实完全没有收到任何消息。
    /// </remarks>
    private static string DescribePostFailure(string message)
    {
        int code = Marshal.GetLastWin32Error();

        return code == 5
            ? $"{message} 投递被 UIPI 拒绝（错误码 5）。目标窗口的完整性级别高于本进程，请以管理员身份重新运行。"
            : $"{message} 投递失败（错误码 {code}）。";
    }
}
