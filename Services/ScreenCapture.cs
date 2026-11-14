using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace SharpLensAI.Services;
public static class ScreenCapture
{
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    public static (int X, int Y, int Width, int Height) Bounds => (GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));
    public static Bitmap CaptureDesktop()
    {
        var b = Bounds;
        var bitmap = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb);
        try { using var g = Graphics.FromImage(bitmap); g.CopyFromScreen(b.X, b.Y, 0, 0, bitmap.Size); return bitmap; }
        catch { bitmap.Dispose(); throw; }
    }
}
