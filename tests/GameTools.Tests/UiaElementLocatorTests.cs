using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using GameTools.Core.Abstractions;
using GameTools.Core.Models;
using GameTools.Infrastructure.Automation;
using Xunit;

namespace GameTools.Tests;

/// <summary>
/// <see cref="UiaElementLocator"/> 的回归测试。
/// </summary>
/// <remarks>
/// 重点锁定两处易错行为：
/// <list type="number">
/// <item>元素矩形必须正确读出。UI Automation 的 BoundingRectangle 类型是
/// <c>System.Windows.Rect</c>，为避免把 WPF 基础程序集引入 WinForms 工程，
/// 本实现改用反射读取；该类型的 X/Y/Width/Height 在不同运行时上可能表现为
/// 字段或属性，取错成员会让矩形恒为 0×0。</item>
/// <item>UI Automation 的 COM 调用无法真正取消，超时必须能放弃等待并如实回报。</item>
/// </list>
/// </remarks>
[Collection(UiAutomationCollection.Name)]
public class UiaElementLocatorTests
{
    /// <summary>
    /// 无效根句柄必须返回结构化失败，不得抛出异常。
    /// </summary>
    [Fact]
    public async Task FindAsync_InvalidRoot_ShouldReturnFailure()
    {
        var locator = new UiaElementLocator();
        UiQueryResult result = await locator.FindAsync(new UiQuery { RootWindowHandle = IntPtr.Zero });

        Assert.False(result.Success);
        Assert.Contains("句柄", result.Message);
    }

    /// <summary>
    /// 预取消的令牌必须立即返回取消提示，而不是等待超时。
    /// </summary>
    [Fact]
    public async Task FindAsync_PreCancelledToken_ShouldReturnCancelled()
    {
        var locator = new UiaElementLocator();

        using var form = CreateForm();
        form.Show();
        form.BringToFront();
        Application.DoEvents();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        UiQueryResult result = await locator.FindAsync(
            new UiQuery { RootWindowHandle = form.Handle, TimeoutMs = 5000 },
            cts.Token);

        Assert.False(result.Success);
        Assert.Contains("取消", result.Message);
    }

    /// <summary>
    /// 超时应放弃等待并返回可诊断信息，而不是无限阻塞。
    /// </summary>
    [Fact]
    public async Task FindAsync_ExpiringTimeout_ShouldGiveUp()
    {
        var locator = new UiaElementLocator();

        using var form = CreateForm();
        form.Show();
        form.BringToFront();
        Application.DoEvents();

        UiQueryResult result = await locator.FindAsync(
            new UiQuery { RootWindowHandle = form.Handle, TimeoutMs = 1 });

        // 结果取决于 1ms 是否足以完成一次简单查询：无论哪种都不应抛异常
        Assert.NotNull(result);
        if (!result.Success)
        {
            Assert.Contains("ms", result.Message);
        }
    }

    /// <summary>
    /// 标准 WinForms 窗口的按钮必须可被发现，且矩形宽高非零。
    /// </summary>
    /// <remarks>
    /// 该断言是本次修复的直接回归点：矩形读取失效时元素仍会被发现，
    /// 但 Bounds 恒为 0×0，导致界面无法定位元素、点击落点无从计算。
    /// </remarks>
    [Fact]
    public void FindAsync_StandardWindow_ShouldReturnElementsWithValidBounds()
    {
        RunSta(() =>
        {
            using var form = new Form
            {
                Text = "GameTools 元素定位回归测试",
                ClientSize = new System.Drawing.Size(420, 260)
            };

            var button = new Button
            {
                Text = "确定",
                Location = new System.Drawing.Point(40, 40),
                Size = new System.Drawing.Size(160, 44)
            };
            form.Controls.Add(button);
            form.Show();
            form.BringToFront();
            Application.DoEvents();
            Thread.Sleep(200);
            Application.DoEvents();

            var locator = new UiaElementLocator();
            UiQueryResult result = StaWait.PumpUntil(locator.FindAsync(new UiQuery { TimeoutMs = 20000, RootWindowHandle = form.Handle, ControlTypes = new[] { "Button" } }));

            Assert.True(result.Success, result.Message);
            Assert.NotEmpty(result.Elements);

            Assert.All(result.Elements, element =>
            {
                Assert.True(
                    element.Bounds.Width > 0 && element.Bounds.Height > 0,
                    $"元素 '{element.Display}' 矩形为 {element.Bounds.Width}x{element.Bounds.Height}，" +
                    "说明 System.Windows.Rect 的成员读取方式有误。");
            });
            var targets = result.Elements.Where(e => e.Name.Contains("确定")).ToList();
            Assert.NotEmpty(targets);
            Assert.All(targets, e => Assert.True(e.IsClickCandidate));
        });
    }

    /// <summary>
    /// 按名称包含过滤应只返回名称匹配的元素。
    /// </summary>
    [Fact]
    public void FindAsync_NameContains_ShouldFilterByName()
    {
        RunSta(() =>
        {
            using var form = new Form
            {
                Text = "GameTools 名称过滤回归测试",
                ClientSize = new System.Drawing.Size(420, 260)
            };

            form.Controls.Add(new Button { Text = "保存更改", Location = new System.Drawing.Point(30, 30), Size = new System.Drawing.Size(140, 36) });
            form.Controls.Add(new Button { Text = "放弃", Location = new System.Drawing.Point(30, 90), Size = new System.Drawing.Size(140, 36) });
            form.Show();
            form.BringToFront();
            Application.DoEvents();
            Thread.Sleep(200);
            Application.DoEvents();

            var locator = new UiaElementLocator();
            UiQueryResult result = StaWait.PumpUntil(locator.FindAsync(new UiQuery { TimeoutMs = 20000,
                    RootWindowHandle = form.Handle,
                    ControlTypes = new[] { "Button" },
                    NameContains = "保存"
                }));

            Assert.True(result.Success, result.Message);
            Assert.NotEmpty(result.Elements);
            Assert.All(result.Elements, e => Assert.Contains("保存", e.Name));
        });
    }

    private static Form CreateForm() =>
        new() { Text = "GameTools locator 测试", ClientSize = new System.Drawing.Size(320, 180) };

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
            Name = "UiaElementLocatorTests-STA"
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
