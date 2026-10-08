namespace GameTools.Core.Models;

/// <summary>
/// 平台无关的矩形区域。
/// </summary>
/// <remarks>
/// 取值语义与 GDI+ 的 <c>Rectangle</c> 一致（含负坐标，允许表示位于主屏左侧的显示器），
/// 但不依赖 System.Drawing，使契约层可在任意目标框架下使用。
/// </remarks>
/// <param name="X">左上角 X 坐标。</param>
/// <param name="Y">左上角 Y 坐标。</param>
/// <param name="Width">宽度。</param>
/// <param name="Height">高度。</param>
public readonly record struct CaptureBounds(int X, int Y, int Width, int Height)
{
    /// <summary>空矩形。</summary>
    public static CaptureBounds Empty => new(0, 0, 0, 0);

    /// <summary>右边界（不含）。</summary>
    public int Right => X + Width;

    /// <summary>下边界（不含）。</summary>
    public int Bottom => Y + Height;

    /// <summary>是否为空矩形。</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;
}