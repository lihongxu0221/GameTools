using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace GameTools.Tests;

/// <summary>
/// 在 STA 线程上等待异步操作并持续泵消息。
/// </summary>
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
internal static class StaWait
{
    /// <summary>
    /// 轮询间隔，过短会空转占用 CPU，过长会拖慢测试。
    /// </summary>
    private const int PollIntervalMs = 10;

    /// <summary>
    /// 在泵消息的同时等待任务完成。
    /// </summary>
    /// <typeparam name="T">任务结果类型。</typeparam>
    /// <param name="task">待等待的任务。</param>
    /// <param name="timeoutSeconds">最长等待秒数。</param>
    /// <returns>任务的返回值。</returns>
    /// <exception cref="TimeoutException">超时未完成时抛出。</exception>
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
