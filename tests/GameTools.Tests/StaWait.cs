using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace GameTools.Tests;

/// <summary>
/// 测试中处理 WinForms 与 UI Automation 时序的辅助方法。
/// </summary>
internal static class StaWait
{
    /// <summary>
    /// 轮询间隔，过短会空转占用 CPU，过长会拖慢测试。
    /// </summary>
    private const int PollIntervalMs = 10;

    /// <summary>
    /// 在独立 STA 线程上执行操作并等待完成。
    /// </summary>
    /// <remarks>
    /// <para>
    /// xUnit 默认在 MTA 上运行，而 WinForms 要求 STA。在 MTA 上创建窗口会导致
    /// 消息泵行为异常，表现为测试宿主挂起并锁住程序集。
    /// </para>
    /// <para>
    /// 不得把 <c>[STAThread]</c> 标在 <c>async</c> 入口方法上：异步入口方法
    /// 实际在线程池线程上执行，该标记不会生效。
    /// </para>
    /// </remarks>
    /// <param name="action">需要执行的测试逻辑。</param>
    /// <param name="timeoutSeconds">最长等待秒数。</param>
    internal static void RunSta(Action action, int timeoutSeconds = 60)
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
            Name = "GameTools-STA"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            throw new TimeoutException($"STA 测试线程执行超时（{timeoutSeconds}s）。");
        }

        if (failure != null)
        {
            throw new InvalidOperationException("STA 测试线程内发生异常。", failure);
        }
    }

    /// <summary>
    /// 在泵消息的同时等待任务完成。
    /// </summary>
    /// <typeparam name="T">任务结果类型。</typeparam>
    /// <param name="task">待等待的任务。</param>
    /// <param name="timeoutSeconds">最长等待秒数。</param>
    /// <returns>任务的返回值。</returns>
    /// <exception cref="TimeoutException">超时未完成时抛出。</exception>
    /// <remarks>
    /// <para>
    /// UI Automation 的调用经由目标窗口的无障碍提供程序应答，而 WinForms 的提供程序
    /// 在其所属 STA 线程上响应。若测试在该 STA 线程上直接阻塞等待
    /// （<c>GetAwaiter().GetResult()</c>），提供程序得不到消息泵应答，
    /// 调用方与提供程序互相等待，最终触发查询超时。
    /// </para>
    /// <para>
    /// 生产代码不受影响：<c>UiaElementLocator</c> 把遍历放在线程池线程执行，
    /// 界面线程始终可自由泵消息。此辅助方法只是让测试的等待方式与真实宿主一致。
    /// </para>
    /// </remarks>
    internal static T PumpUntil<T>(Task<T> task, int timeoutSeconds = 30)
    {
        var stopwatch = Stopwatch.StartNew();

        while (!task.IsCompleted)
        {
            if (stopwatch.Elapsed.TotalSeconds > timeoutSeconds)
            {
                throw new TimeoutException($"等待任务超时（{timeoutSeconds}s）。");
            }

            // 关键：让出线程并泵消息，使 UI Automation 提供程序能够应答
            Application.DoEvents();
            Thread.Sleep(PollIntervalMs);
        }

        return task.GetAwaiter().GetResult();
    }
}
