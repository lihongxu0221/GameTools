using GameTools.Core.Enums;

namespace GameTools.Core.Models;

/// <summary>
/// 截图捕获结果。
/// </summary>
/// <remarks>
/// <see cref="CaptureFrame"/> 以平台无关的 BGRA 像素承载画面，避免契约层依赖 GDI+；
/// 需要 GDI+ 位图的调用方应在基础设施层完成转换。
/// </remarks>
public sealed class CaptureResult : IDisposable
{
    /// <summary>是否捕获成功。</summary>
    public bool Success { get; init; }

    /// <summary>捕获到的画面帧；失败时为 null。</summary>
    public CaptureFrame? Frame { get; init; }

    /// <summary>捕获区域。</summary>
    public CaptureBounds Bounds { get; init; }

    /// <summary>实际使用的捕获模式。</summary>
    public CaptureMode Mode { get; init; }

    /// <summary>捕获耗时。</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>失败原因或降级说明。</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 释放持有的像素缓冲。
    /// </summary>
    public void Dispose()
    {
        Frame?.Dispose();
    }
}