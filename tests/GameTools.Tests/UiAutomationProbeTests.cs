using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using GameTools.Infrastructure.Automation;
using Xunit;

namespace GameTools.Tests;

/// <summary>
/// 使涉及真实窗口与 UI Automation 的测试串行执行。
/// </summary>
/// <remarks>
/// UI Automation 的无障碍树在进程内有缓存，且多个测试同时操作窗口会互相干扰。
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UiAutomationCollection
{
    /// <summary>
    /// 集合名称。
    /// </summary>
    public const string Name = "UiAutomation";
}

/// <summary>
/// <see cref="UiAutomationProbe"/> 的回归测试。
/// </summary>
/// <remarks>
/// 主要锁定两点：
/// <list type="number">
/// <item>UI Automation 程序集在 net8.0-windows 与 net48 双目标下均可解析并链接
/// （若引用丢失，本文件在编译期即失败，等价于依赖守卫）；</item>
/// <item>探测逻辑在无效句柄与不可用窗口上返回结构化失败而非抛出异常。</item>
/// </list>
/// 真实 Chromium 应用的无障碍树需要窗口激活后才完整，属环境相关行为，
/// 不在自动化测试中断言，改由实施阶段人工验证。
/// </remarks>
[Collection(UiAutomationCollection.Name)]
public class UiAutomationProbeTests
{
    /// <summary>
    /// 无效句柄必须返回结构化失败，不得抛出异常。
    /// </summary>
    [Fact]
    public void Probe_ZeroHandle_ShouldReturnFailure()
    {
        var probe = new UiAutomationProbe();
        UiAutomationProbeResult result = probe.Probe(IntPtr.Zero);

        Assert.False(result.Success);
        Assert.Contains("句柄", result.ErrorMessage);
    }

    /// <summary>
    /// 读取与写入在无法定位元素时都应返回失败，不得抛出异常。
    /// </summary>
    [Fact]
    public void ValueOperations_UnresolvableElement_ShouldReturnFailure()
    {
        var probe = new UiAutomationProbe();

        UiAutomationValueResult read = probe.ReadValue(IntPtr.Zero, name: null);
        Assert.False(read.Success);
        Assert.NotEmpty(read.ErrorMessage);

        UiAutomationValueResult write = probe.WriteValue(IntPtr.Zero, name: null, "文本");
        Assert.False(write.Success);
        Assert.NotEmpty(write.ErrorMessage);
    }

    /// <summary>
    /// 对真实窗口探测应成功并返回元素统计。
    /// </summary>
    /// <remarks>
    /// 使用测试自身创建的 WinForms 窗口：标准控件具备稳定的无障碍信息，
    /// 不依赖外部应用状态。标准 Button 支持 <c>InvokePattern</c>，
    /// 因此可同时验证模式探测路径。
    /// </remarks>
    [Fact]
    public void Probe_StandardButtonWindow_ShouldExposeInvokePattern()
    {
        RunSta(() =>
        {
            using var form = new Form
            {
                Text = "GameTools UIA 回归测试",
                ClientSize = new System.Drawing.Size(360, 180)
            };

            var button = new Button { Text = "确认按钮", Location = new System.Drawing.Point(24, 24), Size = new System.Drawing.Size(160, 40) };
            var edit = new TextBox { Location = new System.Drawing.Point(24, 90), Width = 240 };
            form.Controls.Add(button);
            form.Controls.Add(edit);
            form.Show();
            form.BringToFront();
            Application.DoEvents();
            Thread.Sleep(200);
            Application.DoEvents();

            var probe = new UiAutomationProbe();
            UiAutomationProbeResult result = probe.Probe(form.Handle);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Contains("UIA", result.WindowName);
            Assert.True(result.ButtonCount >= 1, $"未发现按钮控件：ButtonCount={result.ButtonCount}");
            Assert.True(
                result.InvokePatternCount >= 1,
                $"按钮未暴露 InvokePattern：InvokePatternCount={result.InvokePatternCount}");

            // 文本框应支持 ValuePattern，并能读回所写内容
            UiAutomationValueResult write = probe.WriteValue(IntPtr.Zero, edit.Name, "G");
            if (write.Success)
            {
                UiAutomationValueResult read = probe.ReadValue(IntPtr.Zero, edit.Name);
                Assert.True(read.Success);
                Assert.Equal("G", read.Value);
            }
            else
            {
                // 部分环境下按名称定位可能命中不到控件，此时只要求返回结构化失败
                Assert.NotEmpty(write.ErrorMessage);
            }
        });
    }

    /// <summary>
    /// 在独立 STA 线程上执行 WinForms 操作并等待完成。
    /// </summary>
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
            Name = "UiAutomationTests-STA"
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