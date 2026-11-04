using System.Drawing;
using System.Windows;
using System.Windows.Controls; // Обязательно для Canvas
using System.Windows.Input;

namespace SharpLensAI
{
    public partial class SnippingWindow : Window
    {
        private System.Windows.Point _startPoint;
        private bool _isDragging = false;
        
        // Помечаем как nullable (Bitmap?), чтобы убрать предупреждение CS8618
        public Bitmap? CapturedBitmap { get; private set; }

        private readonly Bitmap _screenBitmap;

        public SnippingWindow(Bitmap screenBitmap)
        {
            InitializeComponent();
            _screenBitmap = screenBitmap;
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(this);
            _isDragging = true;
            SelectionBox.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionBox, _startPoint.X);
            Canvas.SetTop(SelectionBox, _startPoint.Y);
            SelectionBox.Width = 0;
            SelectionBox.Height = 0;
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;

            var currentPoint = e.GetPosition(this);

            double x = System.Math.Min(currentPoint.X, _startPoint.X);
            double y = System.Math.Min(currentPoint.Y, _startPoint.Y);
            double width = System.Math.Abs(currentPoint.X - _startPoint.X);
            double height = System.Math.Abs(currentPoint.Y - _startPoint.Y);

            Canvas.SetLeft(SelectionBox, x);
            Canvas.SetTop(SelectionBox, y);
            SelectionBox.Width = width;
            SelectionBox.Height = height;
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;

            double x = Canvas.GetLeft(SelectionBox);
            double y = Canvas.GetTop(SelectionBox);
            double width = SelectionBox.Width;
            double height = SelectionBox.Height;

            if (width > 10 && height > 10)
            {
                var source = PresentationSource.FromVisual(this);
                double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                int rectX = (int)(x * dpiX);
                int rectY = (int)(y * dpiY);
                int rectWidth = (int)(width * dpiX);
                int rectHeight = (int)(height * dpiY);

                CapturedBitmap = new Bitmap(rectWidth, rectHeight);
                using (var g = Graphics.FromImage(CapturedBitmap))
                {
                    // Явно используем System.Drawing.Rectangle во избежание конфликтов с WPF классами
                    g.DrawImage(_screenBitmap, new System.Drawing.Rectangle(0, 0, rectWidth, rectHeight),
                        new System.Drawing.Rectangle(rectX, rectY, rectWidth, rectHeight), GraphicsUnit.Pixel);
                }

                DialogResult = true;
            }
            else
            {
                DialogResult = false;
            }

            Close();
        }
    }
}