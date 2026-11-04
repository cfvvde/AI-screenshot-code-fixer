using System;
using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SharpLensAI.Views
{
    public partial class OverlayWindow : Window
    {
        public BitmapSource? CapturedImage { get; private set; }

        private System.Windows.Point _startPoint;
        private bool _isSelecting;

        public OverlayWindow()
        {
            InitializeComponent();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isSelecting = true;
                _startPoint = e.GetPosition(this);
                SelectionCanvas.CaptureMouse();

                System.Windows.Controls.Canvas.SetLeft(SelectionBox, _startPoint.X);
                System.Windows.Controls.Canvas.SetTop(SelectionBox, _startPoint.Y);
                SelectionBox.Width = 0;
                SelectionBox.Height = 0;
                SelectionBox.Visibility = Visibility.Visible;
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isSelecting)
            {
                System.Windows.Point currentPoint = e.GetPosition(this);

                double x = Math.Min(_startPoint.X, currentPoint.X);
                double y = Math.Min(_startPoint.Y, currentPoint.Y);
                double width = Math.Abs(currentPoint.X - _startPoint.X);
                double height = Math.Abs(currentPoint.Y - _startPoint.Y);

                System.Windows.Controls.Canvas.SetLeft(SelectionBox, x);
                System.Windows.Controls.Canvas.SetTop(SelectionBox, y);
                SelectionBox.Width = width;
                SelectionBox.Height = height;
            }
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSelecting)
            {
                _isSelecting = false;
                SelectionCanvas.ReleaseMouseCapture();

                int width = (int)SelectionBox.Width;
                int height = (int)SelectionBox.Height;

                if (width > 10 && height > 10)
                {
                    int x = (int)System.Windows.Controls.Canvas.GetLeft(SelectionBox);
                    int y = (int)System.Windows.Controls.Canvas.GetTop(SelectionBox);

                    SelectionBox.Visibility = Visibility.Collapsed;
                    this.Opacity = 0;

                    CapturedImage = CaptureArea(x, y, width, height);

                    DialogResult = CapturedImage != null;
                }
                else
                {
                    DialogResult = false;
                }
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
            }
        }

        private BitmapSource? CaptureArea(int x, int y, int width, int height)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            double dpiX = dpi.DpiScaleX;
            double dpiY = dpi.DpiScaleY;

            int pixelX = (int)(x * dpiX);
            int pixelY = (int)(y * dpiY);
            int pixelWidth = (int)(width * dpiX);
            int pixelHeight = (int)(height * dpiY);

            if (pixelWidth <= 0 || pixelHeight <= 0)
            {
                return null;
            }

            using var bitmap = new Bitmap(pixelWidth, pixelHeight);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(pixelX, pixelY, 0, 0, new System.Drawing.Size(pixelWidth, pixelHeight));
            }

            var handle = bitmap.GetHbitmap();
            try
            {
                return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    handle,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(handle);
            }
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}