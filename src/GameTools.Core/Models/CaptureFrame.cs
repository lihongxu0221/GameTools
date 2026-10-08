namespace GameTools.Core.Models;

/// <summary>
/// 捕获到的画面帧，以平台无关的 BGRA 像素承载。
/// </summary>
/// <remarks>
/// 像素为紧密排列的 32 位 BGRA，每像素 4 字节，按从上到下、从左到右的行序排列，不含行对齐填充。
/// 该布局与 DIB 的 BI_RGB 一致，可直接交由图像处理或 GPU 使用；
/// 转换为 GDI+ 位图由基础设施层完成。
/// </remarks>
public sealed class CaptureFrame : IDisposable
{
    private readonly byte[] _bgra;
    private IReadOnlyList<byte>? _readonlyView;

    /// <summary>
    /// 初始化画面帧。
    /// </summary>
    /// <param name="width">宽度（像素）。</param>
    /// <param name="height">高度（像素）。</param>
    /// <param name="bgra">BGRA 像素数据，长度必须等于 width * height * 4。</param>
    public CaptureFrame(int width, int height, byte[] bgra)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "宽度必须大于 0。");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "高度必须大于 0。");
        }

        if (bgra is null)
        {
            throw new ArgumentNullException(nameof(bgra));
        }

        int expected = width * height * 4;
        if (bgra.Length != expected)
        {
            throw new ArgumentException($"像素数据长度应为 {expected}，实际为 {bgra.Length}。", nameof(bgra));
        }

        Width = width;
        Height = height;
        _bgra = bgra;
    }

    /// <summary>宽度（像素）。</summary>
    public int Width { get; }

    /// <summary>高度（像素）。</summary>
    public int Height { get; }

    /// <summary>字节长度。</summary>
    public int ByteLength => _bgra.Length;

    /// <summary>
    /// 获取内部像素数组的只读视图。
    /// </summary>
    /// <remarks>
    /// 返回只读集合而非 <c>ReadOnlySpan</c>：netstandard2.0 无内置 Span 支持，
    /// 仅为一个视图接口引入 System.Memory 并不划算。需要高性能访问时使用 <see cref="ToArray"/>。
    /// </remarks>
    public IReadOnlyList<byte> Pixels => _readonlyView ??= Array.AsReadOnly(_bgra);

    /// <summary>
    /// 复制并返回 BGRA 像素数据。
    /// </summary>
    public byte[] ToArray() => (byte[])_bgra.Clone();

    /// <summary>
    /// 释放像素缓冲。
    /// </summary>
    public void Dispose()
    {
        // 像素数组由 GC 回收；此处保留显式释放入口以满足 IDisposable 契约，
        // 未来若改为池化缓冲可在此归还池。
        _readonlyView = null;
        GC.SuppressFinalize(this);
    }
}