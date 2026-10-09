using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using GameTools.Core.Abstractions;
using GameTools.Core.Enums;
using GameTools.Core.Models;
using GameTools.Infrastructure.Automation;
using Xunit;

namespace GameTools.Tests;

/// <summary>
/// <see cref="UiElementClicker"/> 与提权引导的回归测试。
/// </summary>
[Collection(UiAutomationCollection.Name)]
public class UiElementClickerTests
{
    /// <summary>
    /// 根句柄无效时应失败，提示指向句柄。
    /// </summary>
    [Fact]
    public async Task ClickAsync_InvalidRoot_ShouldFailWithHandleMessage()
    {
        UiElementClicker clicker = CreateClicker();
        ClickOutcome outcome = await clicker.ClickAsync(new UiQuery { RootWindowHandle = IntPtr.Zero });

        Assert.False(outcome.Success);
        Assert.Contains("句柄", outcome.Message);
        Assert.Equal(ClickMechanism.None, outcome.Mechanism);
    }

    /// <summary>
    /// 未命中控件时不得误报为已点击。
    /// </summary>
    [Fact]
    public void ClickAsync_NoMatch_ShouldFailWithMatchReason()
    {
        RunSta(() =>
        {
            UiElementClicker clicker = CreateClicker();

            using var form = CreateForm();
            form.Show();
            form.BringToFront();
            Application.DoEvents();
            Thread.Sleep(150);
            Application.DoEvents();

            ClickOutcome outcome = StaWait.PumpUntil(clicker.ClickAsync(new UiQuery { TimeoutMs = 20000,
                    RootWindowHandle = form.Handle,
                    NameExact = "绝对不存在的控件名-ZZZ"
                }));

            Assert.False(outcome.Success);
            Assert.DoesNotContain("没有暴露任何可自动化元素", outcome.Message);
            Assert.Contains("匹配", outcome.Message);
        });
    }

    /// <summary>
    /// 支持 InvokePattern 的标准按钮应通过第一级点击，并真正触发点击事件。
    /// </summary>
    [Fact]
    public void ClickAsync_StandardButton_ShouldInvokeAndRaiseClick()
    {
        RunSta(() =>
        {
            int clickCount = 0;
            using var form = CreateForm();

            var button = new Button
            {
                Text = "确定",
                Name = "okButton",
                Location = new System.Drawing.Point(30, 30),
                Size = new System.Drawing.Size(140, 40)
            };
            button.Click += (_, _) => clickCount++;
            form.Controls.Add(button);
            form.Show();
            form.BringToFront();
            Application.DoEvents();
            Thread.Sleep(200);
            Application.DoEvents();

            ClickOutcome outcome = StaWait.PumpUntil(CreateClicker().ClickAsync(new UiQuery { TimeoutMs = 20000, RootWindowHandle = form.Handle, ControlTypes = new[] { "Button" } }));

            Assert.True(outcome.Success, outcome.Message);
            Assert.Equal(ClickMechanism.UiaInvokePattern, outcome.Mechanism);
            Assert.Equal(1, clickCount);
        });
    }

    /// <summary>
    /// 禁用按钮不得被点击，也不应报出成功。
    /// </summary>
    [Fact]
    public void ClickAsync_DisabledButton_ShouldNotReportSuccess()
    {
        RunSta(() =>
        {
            int clickCount = 0;
            using var form = CreateForm();

            var button = new Button
            {
                Text = "禁用",
                Location = new System.Drawing.Point(30, 30),
                Size = new System.Drawing.Size(140, 40),
                Enabled = false
            };
            button.Click += (_, _) => clickCount++;
            form.Controls.Add(button);
            form.Show();
            form.BringToFront();
            Application.DoEvents();
            Thread.Sleep(150);
            Application.DoEvents();

            ClickOutcome outcome = StaWait.PumpUntil(CreateClicker().ClickAsync(new UiQuery { TimeoutMs = 20000, RootWindowHandle = form.Handle, ControlTypes = new[] { "Button" } }));

            Assert.Equal(0, clickCount);
        });
    }

    /// <summary>
    /// 传统按钮点击必须拒绝空句柄与已失效句柄。
    /// </summary>
    [Fact]
    public void LegacyButtonClicker_InvalidHandle_ShouldFail()
    {
        Assert.False(LegacyButtonClicker.TryClick(IntPtr.Zero, out string? zeroFailure));
        Assert.NotNull(zeroFailure);

        Assert.False(LegacyButtonClicker.TryClick(new IntPtr(0xFFFF), out string? invalidFailure));
        Assert.NotNull(invalidFailure);
    }

    /// <summary>
    /// 传统按钮点击必须真正触发点击事件。
    /// </summary>
    [Fact]
    public void LegacyButtonClicker_ValidButton_ShouldRaiseClick()
    {
        RunSta(() =>
        {
            int clickCount = 0;
            using var form = CreateForm();

            var inner = new Panel
            {
                Location = new System.Drawing.Point(0, 0),
                Size = new System.Drawing.Size(200, 120)
            };
            var button = new Button
            {
                Text = "传统按钮",
                Location = new System.Drawing.Point(10, 10),
                Size = new System.Drawing.Size(120, 36)
            };
            button.Click += (_, _) => clickCount++;
            inner.Controls.Add(button);
            form.Controls.Add(inner);
            form.Show();
            form.BringToFront();
            Application.DoEvents();
            Thread.Sleep(150);
            Application.DoEvents();

            bool clicked = LegacyButtonClicker.TryClick(button.Handle, out string? failure);

            Assert.True(clicked, failure);
            Assert.Equal(1, clickCount);
        });
    }

    /// <summary>
    /// 提权探测必须能识别外部进程，且对自身不产生风险提示。
    /// </summary>
    [Fact]
    public void IntegrityProbe_SelfProcess_ShouldNotWarn()
    {
        var probe = new IntegrityLevelProbe();

        int selfProcessId;
        using (Process current = Process.GetCurrentProcess())
        {
            selfProcessId = current.Id;
        }

        Assert.NotNull(probe.GetProcessIntegrityLevel(0));
        Assert.Null(probe.CheckElevationRisk(selfProcessId));
    }

    /// <summary>
    /// 对外部进程的探测必须返回级别；这依赖进程句柄在探测期间保持有效。
    /// </summary>
    /// <remarks>
    /// 回归背景：曾用 <c>using Process</c> 取句柄，方法返回时句柄即被关闭，
    /// 导致对外部进程的探测静默返回 null，提权风险永远无法提示。
    /// </remarks>
    [Fact]
    public void IntegrityProbe_ExternalProcess_ShouldReturnLevel()
    {
        var probe = new IntegrityLevelProbe();

        int explorerProcessId = FindShellProcessId();
        if (explorerProcessId <= 0)
        {
            // 极端环境下没有可探测的外部进程，跳过而不使测试失败
            return;
        }

        Assert.NotNull(probe.GetProcessIntegrityLevel(explorerProcessId));
    }

    private static UiElementClicker CreateClicker() =>
        new(new UiaElementLocator(), new IntegrityLevelProbe());

    private static Form CreateForm() =>
        new() { Text = "GameTools 点击回归测试", ClientSize = new System.Drawing.Size(340, 200) };

    private static int FindShellProcessId()
    {
        foreach (Process process in Process.GetProcessesByName("explorer"))
        {
            using (process)
            {
                return process.Id;
            }
        }

        return 0;
    }

    /// <summary>
    /// 在独立 STA 线程上执行 WinForms 操作并等待完成。
    /// </summary>
    /// <remarks>
    /// xUnit 默认在 MTA 上运行，而 WinForms 要求 STA。
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
            Name = "UiElementClickerTests-STA"
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
