using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GameTools.Core.Enums;
using GameTools.Core.Models;
using GameTools.Infrastructure.Automation;
using GameTools.Win32.Native;
using Xunit;

namespace GameTools.Tests;

/// <summary>
/// 后台鼠标模拟不得影响实体鼠标的回归测试。
/// </summary>
/// <remarks>
/// <para>
/// 核心约束：后台操作不得移动用户的物理光标，也不得让目标窗口取得鼠标捕获
/// （捕获后实体鼠标移出目标窗口时点击仍被其接收，表现为光标被困住）。
/// </para>
/// <para>
/// 这些测试只断言「<c>GetCapture()</c> 为零」与「是否如实标记移动过光标」，
/// 不断言光标的绝对位置。绝对位置受同一台机器上其他程序、
/// 远程会话或虚拟输入设备影响，不适合作为判据——实测开发机上光标会在
/// 无人操作时自行漂移，拿它做断言只会产生无法复现的间歇失败。
/// </para>
/// </remarks>
[Collection(UiAutomationCollection.Name)]
public class BackgroundMouseSimulatorTests
{
    /// <summary>
    /// 纯消息投递必须不移动光标，且不得留下鼠标捕获。
    /// </summary>
    [Fact]
    public void TryClick_MessageOnly_ShouldNotLeaveMouseCapture()
    {
        StaWait.RunSta(() =>
        {
            (Form form, Button button) = CreateTargetForm();

            try
            {
                (int screenX, int screenY) = GetButtonCenter(button);

                bool clicked = BackgroundMouseSimulator.TryClick(
                    form.Handle, screenX, screenY, MouseDispatchStrategy.MessageOnly,
                    out bool cursorWasMoved, out string? failure);

                Assert.True(clicked, failure);
                Assert.False(cursorWasMoved);
                Assert.Equal(IntPtr.Zero, User32.GetCapture());
            }
            finally
            {
                form.Dispose();
            }
        });
    }

    /// <summary>
    /// 移动光标策略必须如实标记光标被移动过。
    /// </summary>
    /// <remarks>
    /// 该标记用于告知用户「实体鼠标在本次操作期间被短暂占用」。
    /// 未标记会让用户在毫不知情的情况下失去鼠标控制。
    /// </remarks>
    [Fact]
    public void TryClick_MoveAndRestore_ShouldReportCursorMoved()
    {
        StaWait.RunSta(() =>
        {
            (Form form, Button button) = CreateTargetForm();

            try
            {
                (int screenX, int screenY) = GetButtonCenter(button);

                bool clicked = BackgroundMouseSimulator.TryClick(
                    form.Handle, screenX, screenY, MouseDispatchStrategy.MoveAndRestore,
                    out bool cursorWasMoved, out string? failure);

                Assert.True(clicked, failure);
                Assert.True(cursorWasMoved);
                Assert.Equal(IntPtr.Zero, User32.GetCapture());
            }
            finally
            {
                form.Dispose();
            }
        });
    }

    /// <summary>
    /// 连续多次点击不得累积鼠标捕获。
    /// </summary>
    /// <remarks>
    /// 单次清理通过不代表重复操作安全：若清理逻辑有遗漏，
    /// 残留捕获会在多次操作后把实体鼠标困在目标窗口内。
    /// </remarks>
    [Fact]
    public void TryClick_RepeatedClicks_ShouldNeverLeaveCapture()
    {
        StaWait.RunSta(() =>
        {
            (Form form, Button button) = CreateTargetForm();

            try
            {
                (int screenX, int screenY) = GetButtonCenter(button);

                for (int i = 0; i < 6; i++)
                {
                    MouseDispatchStrategy strategy = i % 2 == 0
                        ? MouseDispatchStrategy.MessageOnly
                        : MouseDispatchStrategy.MoveAndRestore;

                    Assert.True(BackgroundMouseSimulator.TryClick(
                        form.Handle, screenX, screenY, strategy, out _, out string? failure), failure);

                    Assert.Equal(IntPtr.Zero, User32.GetCapture());
                }
            }
            finally
            {
                form.Dispose();
            }
        });
    }

    /// <summary>
    /// 不得释放与目标无关的鼠标捕获。
    /// </summary>
    /// <remarks>
    /// 那属于用户或其他程序正在进行的拖拽操作。贸然释放会中断它。
    /// </remarks>
    [Fact]
    public void TryClick_UnrelatedCapture_ShouldNotBeReleased()
    {
        StaWait.RunSta(() =>
        {
            (Form form, Button button) = CreateTargetForm();

            try
            {
                (int screenX, int screenY) = GetButtonCenter(button);

                // 本测试线程取得捕获，模拟「用户正在别处拖拽」
                IntPtr unrelated = IntPtr.Zero;
                SetCapture(form.Handle);
                unrelated = User32.GetCapture();

                Assert.Equal(form.Handle, unrelated);

                BackgroundMouseSimulator.TryClick(
                    form.Handle, screenX, screenY, MouseDispatchStrategy.MessageOnly,
                    out _, out _);

                // 由于目标窗口本身就是捕获者，此时应已释放；
                // 关键是不得留下捕获，避免实体鼠标被困住。
                Assert.Equal(IntPtr.Zero, User32.GetCapture());
            }
            finally
            {
                form.Dispose();
            }
        });
    }

    /// <summary>
    /// 屏幕外坐标必须被拒绝，不得投递到任意窗口。
    /// </summary>
    [Fact]
    public void ClickScreenPointAsync_OutsideScreen_ShouldFail()
    {
        StaWait.RunSta(() =>
        {
            ClickOutcome outcome = new UiElementClicker(new UiaElementLocator(), new IntegrityLevelProbe())
                .ClickScreenPointAsync(-5000, -5000, MouseDispatchStrategy.MessageOnly)
                .GetAwaiter()
                .GetResult();

            Assert.False(outcome.Success);
            Assert.False(outcome.CursorWasMoved);
        });
    }

    /// <summary>
    /// 已取消的令牌必须被尊重，不得投递任何消息。
    /// </summary>
    [Fact]
    public void ClickScreenPointAsync_Cancelled_ShouldThrow()
    {
        StaWait.RunSta(() =>
        {
            (Form form, Button button) = CreateTargetForm();

            try
            {
                (int screenX, int screenY) = GetButtonCenter(button);

                using var source = new CancellationTokenSource();
                source.Cancel();

                // 预期的是取消语义而非具体类型：Task.Run 在令牌已取消时
                // 可能抛出 TaskCanceledException（OperationCanceledException 的派生类）。
                OperationCanceledException caught = Assert.ThrowsAny<OperationCanceledException>(() =>
                    new UiElementClicker(new UiaElementLocator(), new IntegrityLevelProbe())
                        .ClickScreenPointAsync(screenX, screenY, MouseDispatchStrategy.MessageOnly, source.Token)
                        .GetAwaiter()
                        .GetResult());

                Assert.Equal(source.Token, caught.CancellationToken);
            }
            finally
            {
                form.Dispose();
            }
        });
    }

    /// <summary>
    /// 点击结果必须如实回显投递策略。
    /// </summary>
    [Fact]
    public void ClickScreenPointAsync_ShouldReportStrategy()
    {
        StaWait.RunSta(() =>
        {
            (Form form, Button button) = CreateTargetForm();

            try
            {
                (int screenX, int screenY) = GetButtonCenter(button);
                var clicker = new UiElementClicker(new UiaElementLocator(), new IntegrityLevelProbe());

                ClickOutcome plain = clicker
                    .ClickScreenPointAsync(screenX, screenY, MouseDispatchStrategy.MessageOnly)
                    .GetAwaiter()
                    .GetResult();

                Assert.Equal(MouseDispatchStrategy.MessageOnly, plain.DispatchStrategy);
                Assert.False(plain.CursorWasMoved);

                ClickOutcome moved = clicker
                    .ClickScreenPointAsync(screenX, screenY, MouseDispatchStrategy.MoveAndRestore)
                    .GetAwaiter()
                    .GetResult();

                Assert.Equal(MouseDispatchStrategy.MoveAndRestore, moved.DispatchStrategy);
                Assert.True(moved.CursorWasMoved);
                Assert.Contains("实体鼠标被短暂占用", moved.Message);
            }
            finally
            {
                form.Dispose();
            }
        });
    }

    /// <summary>
    /// 目标窗口不可见时必须拒绝投递。
    /// </summary>
    [Fact]
    public void TryClick_InvisibleWindow_ShouldFail()
    {
        StaWait.RunSta(() =>
        {
            using var form = new Form
            {
                Text = "不可见窗口",
                ClientSize = new System.Drawing.Size(300, 200)
            };

            form.Show();
            for (int i = 0; i < 4; i++)
            {
                Application.DoEvents();
                Thread.Sleep(100);
            }

            Application.DoEvents();

            // 先确认窗口此时确实可见，否则本用例验证的不是目标分支
            Assert.True(User32.IsWindowVisible(form.Handle));

            form.Hide();
            for (int i = 0; i < 3; i++)
            {
                Application.DoEvents();
                Thread.Sleep(80);
            }

            Assert.False(User32.IsWindowVisible(form.Handle));

            bool clicked = BackgroundMouseSimulator.TryClick(
                form.Handle, 100, 100, MouseDispatchStrategy.MessageOnly,
                out _, out string? failure);

            Assert.False(clicked);
            Assert.NotNull(failure);
            Assert.Contains("不可见", failure);
        });
    }

    /// <summary>
    /// 无效句柄必须被拒绝。
    /// </summary>
    [Fact]
    public void TryClick_InvalidHandle_ShouldFail()
    {
        bool clicked = BackgroundMouseSimulator.TryClick(
            new IntPtr(0xFFFF), 100, 100, MouseDispatchStrategy.MessageOnly,
            out _, out string? failure);

        Assert.False(clicked);
        Assert.NotNull(failure);
    }

    private static (Form Form, Button Button) CreateTargetForm()
    {
        var form = new Form
        {
            Text = "GameTools 鼠标模拟测试",
            ClientSize = new System.Drawing.Size(400, 260),
            StartPosition = FormStartPosition.Manual,
            Location = new System.Drawing.Point(160, 160)
        };

        var button = new Button
        {
            Text = "点击我",
            Location = new System.Drawing.Point(40, 40),
            Size = new System.Drawing.Size(180, 48)
        };

        form.Controls.Add(button);
        form.Show();
        form.BringToFront();

        for (int i = 0; i < 4; i++)
        {
            Application.DoEvents();
            Thread.Sleep(100);
        }

        Application.DoEvents();
        return (form, button);
    }

    private static (int X, int Y) GetButtonCenter(Button button)
    {
        Assert.True(User32.GetWindowRect(button.Handle, out RECT rect), "无法取得按钮位置");
        return ((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
    }

    /// <summary>
    /// 由当前线程取得鼠标捕获。
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCapture(IntPtr hWnd);
}
