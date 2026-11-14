using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using SharpLensAI.Services;
namespace SharpLensAI;
public partial class SnippingWindow : Window
{
    private readonly Bitmap _screen;
    private System.Windows.Point _start;
    private bool _dragging;
    public Bitmap? CapturedBitmap { get; private set; }
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
    public SnippingWindow(Bitmap screen)
    {
        InitializeComponent(); _screen = screen;
        using var stream = new MemoryStream(); screen.Save(stream, System.Drawing.Imaging.ImageFormat.Png); stream.Position = 0;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); DesktopImage.Source = image;
        SourceInitialized += (_, _) => { var b = ScreenCapture.Bounds; SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), b.X, b.Y, b.Width, b.Height, 0x0040); };
        Loaded += (_, _) => { Activate(); Focus(); };
    }
    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right) { DialogResult = false; return; }
        if (e.ChangedButton != MouseButton.Left) return;
        _start = e.GetPosition(SelectionCanvas); _dragging = true; SelectionCanvas.CaptureMouse();
        SelectionBox.Visibility = Visibility.Visible; Canvas.SetLeft(SelectionBox, _start.X); Canvas.SetTop(SelectionBox, _start.Y); SelectionBox.Width = SelectionBox.Height = 0;
    }
    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(SelectionCanvas);
        p.X = Math.Clamp(p.X, 0, SelectionCanvas.ActualWidth); p.Y = Math.Clamp(p.Y, 0, SelectionCanvas.ActualHeight);
        Canvas.SetLeft(SelectionBox, Math.Min(p.X, _start.X)); Canvas.SetTop(SelectionBox, Math.Min(p.Y, _start.Y));
        SelectionBox.Width = Math.Abs(p.X - _start.X); SelectionBox.Height = Math.Abs(p.Y - _start.Y);
    }
    private void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !_dragging) return;
        Window_MouseMove(sender, e); // Include the final pointer position even on a quick drag.
        _dragging = false;
        SelectionCanvas.ReleaseMouseCapture();
        CompleteSelection();
    }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }
    private void CompleteSelection()
    {
        if (SelectionBox.Width < 3 || SelectionBox.Height < 3) return;
        double sx = _screen.Width / SelectionCanvas.ActualWidth, sy = _screen.Height / SelectionCanvas.ActualHeight;
        int x = Math.Clamp((int)(Canvas.GetLeft(SelectionBox) * sx), 0, _screen.Width - 1), y = Math.Clamp((int)(Canvas.GetTop(SelectionBox) * sy), 0, _screen.Height - 1);
        int w = Math.Clamp((int)(SelectionBox.Width * sx), 1, _screen.Width - x), h = Math.Clamp((int)(SelectionBox.Height * sy), 1, _screen.Height - y);
        CapturedBitmap = _screen.Clone(new Rectangle(x, y, w, h), System.Drawing.Imaging.PixelFormat.Format32bppArgb); DialogResult = true;
    }
}
