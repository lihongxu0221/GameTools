using System.Drawing;
using System.Drawing.Imaging;
using GameTools.Core.Models;

namespace GameTools.Infrastructure.Capture;

/// <summary>
/// <see cref="CaptureFrame"/> 与 GDI+ <see cref="Bitmap"/> 的双向转换。
/// </summary>
/// <remarks>
/// 契约层保持平台无关的 BGRA 表示，位图构造与编码属于基础设施层职责。
/// </remarks>
public static class CaptureFrameConverter
{
    /// <summary>
    /// 将 BGRA 帧转换为 32 位 ARGB 位图。
    /// </summary>
    /// <param name="frame">源画面帧。</param>
    /// <returns>新建的位图；调用方负责释放。</returns>
    public static Bitmap ToBitmap(CaptureFrame frame)
    {
        if (frame == null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        // BGRA 内存序与 32 位 ARGB 位图逐字节一致，可直接 LockBits 写入
        var bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, frame.Width, frame.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            byte[] pixels = frame.ToArray();

            for (int y = 0; y < frame.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    pixels,
                    y * frame.Width * 4,
                    IntPtr.Add(data.Scan0, y * data.Stride),
                    frame.Width * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    /// <summary>
    /// 将位图转换为 BGRA 帧。
    /// </summary>
    /// <param name="bitmap">源位图。</param>
    /// <returns>新建的画面帧；调用方负责释放。</returns>
    public static CaptureFrame ToFrame(Bitmap bitmap)
    {
        if (bitmap == null)
        {
            throw new ArgumentNullException(nameof(bitmap));
        }

        int width = bitmap.Width;
        int height = bitmap.Height;
        var result = new byte[width * height * 4];

        var data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            for (int y = 0; y < height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    IntPtr.Add(data.Scan0, y * data.Stride),
                    result,
                    y * width * 4,
                    width * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return new CaptureFrame(width, height, result);
    }
}