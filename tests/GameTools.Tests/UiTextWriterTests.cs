using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GameTools.Core.Abstractions;
using GameTools.Core.Enums;
using GameTools.Core.Models;
using GameTools.Infrastructure.Automation;
using Xunit;

namespace GameTools.Tests;

/// <summary>
/// <see cref="UiTextWriter"/> 降级链的回归测试。
/// </summary>
/// <remarks>
/// 重点锁定两处曾经出现的假成功与误导提示：
/// <list type="number">
/// <item>空文本曾走到逐字符路径并报成功，而逐字符投递空串不会发出任何消息。</item>
/// <item>未命中时曾提示「目标窗口没有暴露任何可自动化元素」，
/// 但该结论在服务端已施加名称过滤时不成立。</item>
/// </list>
/// </remarks>
[Collection(UiAutomationCollection.Name)]
public class UiTextWriterTests
{
    /// <summary>
    /// null 文本必须返回失败而不是静默无操作。
    /// </summary>
    [Fact]
    public async Task WriteAsync_NullText_ShouldFail()
    {
        TextEntryOutcome outcome = await CreateWriter().WriteAsync(
            new UiQuery { RootWindowHandle = new IntPtr(1) },
            null!);

        Assert.False(outcome.Success);
        Assert.Contains("null", outcome.Message);
    }

    /// <summary>
    /// 根句柄无效时应失败，且提示指向句柄而非控件类型。
    /// </summary>
    [Fact]
    public async Task WriteAsync_InvalidRoot_ShouldFailWithHandleMessage()
    {
        TextEntryOutcome outcome = await CreateWriter().WriteAsync(
            new UiQuery { RootWindowHandle = IntPtr.Zero },
            "文本");

        Assert.False(outcome.Success);
        Assert.Contains("句柄", outcome.Message);
    }

    /// <summary>
    /// 未命中任何控件时，提示不得断言窗口没有可自动化元素。
    /// </summary>
    /// <remarks>
    /// 服务端已施加名称过滤时命中 0 只说明条件不匹配，
    /// 此时提示「目标窗口没有暴露任何可自动化元素」会误导用户。
    /// </remarks>
    [Fact]
    public void WriteAsync_NoMatch_ShouldNotClaimWindowHasNoElements()
    {
        RunSta(() =>
        {
            var writer = new UiTextWriter(new UiaElementLocator(), new NoOpSimulator());

            using var form = CreateForm();
            form.Show();
            form.BringToFront();
            Application.DoEvents();
            Thread.Sleep(150);
            Application.DoEvents();

            TextEntryOutcome outcome = StaWait.PumpUntil(writer.WriteAsync(
                    new UiQuery
                    {
                        RootWindowHandle = form.Handle,
                        NameExact = "绝对不存在的控件名-ZZZ"
                    },
                    "文本"));

            Assert.False(outcome.Success);
            Assert.DoesNotContain("没有暴露任何可自动化元素", outcome.Message);
            Assert.Contains("匹配", outcome.Message);
        });
    }

    /// <summary>
    /// 空文本必须由 ValuePattern 或 WM_SETTEXT 承担，不得走逐字符路径。
    /// </summary>
    /// <remarks>
    /// 逐字符投递空串不会发出任何消息，若仍报成功即为假成功。
    /// </remarks>
    [Fact]
    public void WriteAsync_EmptyText_ShouldNotUseCharSequence()
    {
        RunSta(() =>
        {
            var simulator = new RecordingSimulator();
            var writer = new UiTextWriter(new UiaElementLocator(), simulator);

            using var form = CreateForm();
            var box = new TextBox
            {
                Name = "targetBox",
                Location = new System.Drawing.Point(20, 20),
                Width = 200
            };
            box.Text = "原有内容";
            form.Controls.Add(box);
            form.Show();
            form.BringToFront();
            Application.DoEvents();
            Thread.Sleep(150);
            Application.DoEvents();

            TextEntryOutcome outcome = StaWait.PumpUntil(writer.WriteAsync(new UiQuery { TimeoutMs = 20000, RootWindowHandle = form.Handle, ControlTypes = new[] { "Edit" } }, string.Empty));

            Assert.NotEqual(TextEntryMechanism.UiaCharSequence, outcome.Mechanism);
            Assert.False(
                simulator.CharSequenceCalled,
                "空文本不得触发逐字符投递，否则会报出假成功。");

            // 空文本应真正清空输入框
            Assert.Equal(string.Empty, box.Text);
        });
    }

    /// <summary>
    /// 非空文本应能真正写入目标文本框并读回。
    /// </summary>
    [Fact]
    public void WriteAsync_NonEmptyText_ShouldWriteAndReadBack()
    {
        RunSta(() =>
        {
            var writer = new UiTextWriter(new UiaElementLocator(), new NoOpSimulator());

            using var form = CreateForm();
            var box = new TextBox
            {
                Name = "targetBox",
                Location = new System.Drawing.Point(20, 20),
                Width = 240
            };
            form.Controls.Add(box);
            form.Show();
            form.BringToFront();
            Application.DoEvents();
            Thread.Sleep(150);
            Application.DoEvents();

            const string payload = "S16-回归测试文本";
            TextEntryOutcome outcome = StaWait.PumpUntil(writer.WriteAsync(new UiQuery { TimeoutMs = 20000, RootWindowHandle = form.Handle, ControlTypes = new[] { "Edit" } }, payload));

            Assert.True(outcome.Success, outcome.Message);
            Assert.Equal(TextEntryMechanism.UiaValuePattern, outcome.Mechanism);
            Assert.Equal(payload, box.Text);
        });
    }

    private static Form CreateForm() =>
        new() { Text = "GameTools 文本写入回归测试", ClientSize = new System.Drawing.Size(360, 200) };

    private static UiTextWriter CreateWriter() =>
        new(new UiaElementLocator(), new NoOpSimulator());

    /// <summary>
    /// 在独立 STA 线程上执行 WinForms 操作并等待完成。
    /// </summary>
    /// <remarks>
    /// xUnit 默认在 MTA 上运行测试，而 WinForms 要求 STA；
    /// 直接在测试线程上创建窗口会抛线程套间异常或导致测试宿主挂起。
    /// </remarks>
    /// <param name="action">需要执行的测试逻辑。</param>
    private static void RunSta(Action action)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
            Name = "UiTextWriterTests-STA"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA 测试线程执行超时。");

        if (failure != null)
        {
            throw new InvalidOperationException("STA 测试线程内发生异常。", failure);
        }
    }
}

/// <summary>
/// 空实现的输入模拟器：文本写入应优先走 UI Automation，不应触达模拟器。
/// </summary>
internal sealed class NoOpSimulator : ITextInputSimulator
{
    /// <inheritdoc />
    public void SendText(string text, int delayBetweenCharsMs = 10, int jitterMs = 0, CancellationToken cancellationToken = default)
    {
    }

    /// <inheritdoc />
    public void SendKeyPress(VirtualKey key, KeyModifiers modifiers = KeyModifiers.None)
    {
    }

    /// <inheritdoc />
    public void PostTextToWindow(IntPtr hWnd, string text, int delayBetweenCharsMs = 10, CancellationToken cancellationToken = default)
    {
    }

    /// <inheritdoc />
    public bool SetWindowText(IntPtr hWnd, string text) => false;

    /// <inheritdoc />
    public void PostKeyPressToWindow(IntPtr hWnd, VirtualKey key)
    {
    }
}

/// <summary>
/// 记录调用的输入模拟器，用于断言逐字符路径是否被触发。
/// </summary>
internal sealed class RecordingSimulator : ITextInputSimulator
{
    /// <summary>逐字符投递是否被触发。</summary>
    internal bool CharSequenceCalled { get; private set; }

    /// <inheritdoc />
    public void SendText(string text, int delayBetweenCharsMs = 10, int jitterMs = 0, CancellationToken cancellationToken = default)
    {
    }

    /// <inheritdoc />
    public void SendKeyPress(VirtualKey key, KeyModifiers modifiers = KeyModifiers.None)
    {
    }

    /// <inheritdoc />
    public void PostTextToWindow(IntPtr hWnd, string text, int delayBetweenCharsMs = 10, CancellationToken cancellationToken = default)
    {
        CharSequenceCalled = true;
    }

    /// <inheritdoc />
    public bool SetWindowText(IntPtr hWnd, string text) => false;

    /// <inheritdoc />
    public void PostKeyPressToWindow(IntPtr hWnd, VirtualKey key)
    {
    }
}
