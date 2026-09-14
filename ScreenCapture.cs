using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace SircleToSearch;

public static class ScreenCapture
{
    public readonly record struct Result(Bitmap Bitmap, Rectangle VirtualBounds);

    public static Result CaptureVirtualScreen()
    {
        var bounds = SystemInformation.VirtualScreen;
        // 24bpp, no alpha: the screen has none, and an alpha channel is exactly what
        // trips up GDI+'s Bitmap.Clone()/JPEG encoder into producing corrupt output.
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        }
        return new Result(bitmap, bounds);
    }
}
