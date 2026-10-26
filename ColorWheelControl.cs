using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PersonalLearningAgent;

public sealed class ColorWheelControl : FrameworkElement
{
    private const int BitmapSize = 256;
    private const double Padding = 7;

    private WriteableBitmap? _wheelBitmap;
    private double _hue;
    private double _saturation;
    private double _value = 1;
    private DragTarget _dragTarget;

    public ColorWheelControl()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
    }

    public event Action<Color>? ColorChanged;

    public Color SelectedColor => FromHsv(_hue, _saturation, _value);

    public void SetColor(Color color)
    {
        ToHsv(color, out _hue, out _saturation, out _value);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var geometry = GetGeometry();
        if (geometry.Radius <= 0)
        {
            return;
        }

        EnsureWheelBitmap();
        drawingContext.DrawImage(_wheelBitmap, geometry.WheelBounds);
        drawingContext.DrawEllipse(
            null,
            new Pen(new SolidColorBrush(Color.FromRgb(90, 90, 90)), 1),
            geometry.Center,
            geometry.Radius,
            geometry.Radius);

        var selectedPoint = GetWheelPoint(geometry);
        drawingContext.DrawEllipse(
            new SolidColorBrush(Color.FromArgb(210, 25, 25, 25)),
            null,
            selectedPoint,
            7,
            7);
        drawingContext.DrawEllipse(
            new SolidColorBrush(SelectedColor),
            new Pen(Brushes.White, 1.3),
            selectedPoint,
            5.5,
            5.5);

        var sliderBounds = geometry.SliderBounds;
        var gradient = new LinearGradientBrush(
            FromHsv(_hue, _saturation, 1),
            Colors.Black,
            new Point(0.5, 0),
            new Point(0.5, 1));
        drawingContext.DrawRoundedRectangle(gradient, null, sliderBounds, 4, 4);
        drawingContext.DrawRoundedRectangle(
            null,
            new Pen(new SolidColorBrush(Color.FromRgb(100, 100, 100)), 1),
            sliderBounds,
            4,
            4);

        var thumbY = sliderBounds.Top + (1 - _value) * sliderBounds.Height;
        drawingContext.DrawLine(
            new Pen(new SolidColorBrush(Color.FromArgb(190, 0, 0, 0)), 4),
            new Point(sliderBounds.Left - 2, thumbY),
            new Point(sliderBounds.Right + 2, thumbY));
        drawingContext.DrawLine(
            new Pen(Brushes.White, 2),
            new Point(sliderBounds.Left - 2, thumbY),
            new Point(sliderBounds.Right + 2, thumbY));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();

        var point = e.GetPosition(this);
        var geometry = GetGeometry();
        _dragTarget = geometry.SliderBounds.Contains(point)
            ? DragTarget.Value
            : DragTarget.Wheel;

        if (CaptureMouse())
        {
            UpdateSelection(point);
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateSelection(e.GetPosition(this));
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
            _dragTarget = DragTarget.None;
            e.Handled = true;
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    private void UpdateSelection(Point point)
    {
        var geometry = GetGeometry();
        if (_dragTarget == DragTarget.Value)
        {
            _value = Math.Clamp(
                1 - (point.Y - geometry.SliderBounds.Top) / geometry.SliderBounds.Height,
                0,
                1);
        }
        else
        {
            if (_value < 0.05)
            {
                _value = 1;
            }

            var offsetX = point.X - geometry.Center.X;
            var offsetY = point.Y - geometry.Center.Y;
            var distance = Math.Sqrt((offsetX * offsetX) + (offsetY * offsetY));
            if (distance > geometry.Radius)
            {
                offsetX *= geometry.Radius / distance;
                offsetY *= geometry.Radius / distance;
                distance = geometry.Radius;
            }

            _hue = (Math.Atan2(offsetY, offsetX) * 180 / Math.PI + 360) % 360;
            _saturation = Math.Clamp(distance / geometry.Radius, 0, 1);
        }

        InvalidateVisual();
        ColorChanged?.Invoke(SelectedColor);
    }

    private void EnsureWheelBitmap()
    {
        if (_wheelBitmap is not null)
        {
            return;
        }

        var pixels = new byte[BitmapSize * BitmapSize * 4];
        var center = (BitmapSize - 1) / 2d;
        var radius = center - 1;
        for (var y = 0; y < BitmapSize; y++)
        {
            for (var x = 0; x < BitmapSize; x++)
            {
                var offsetX = (x - center) / radius;
                var offsetY = (y - center) / radius;
                var saturation = Math.Sqrt((offsetX * offsetX) + (offsetY * offsetY));
                var pixelIndex = (y * BitmapSize + x) * 4;
                if (saturation > 1)
                {
                    continue;
                }

                var hue = (Math.Atan2(offsetY, offsetX) * 180 / Math.PI + 360) % 360;
                var color = FromHsv(hue, saturation, 1);
                pixels[pixelIndex] = color.B;
                pixels[pixelIndex + 1] = color.G;
                pixels[pixelIndex + 2] = color.R;
                pixels[pixelIndex + 3] = byte.MaxValue;
            }
        }

        _wheelBitmap = new WriteableBitmap(
            BitmapSize,
            BitmapSize,
            96,
            96,
            PixelFormats.Bgra32,
            null);
        _wheelBitmap.WritePixels(
            new Int32Rect(0, 0, BitmapSize, BitmapSize),
            pixels,
            BitmapSize * 4,
            0);
    }

    private PickerGeometry GetGeometry()
    {
        var size = Math.Min(ActualHeight - (Padding * 2), ActualWidth - 58);
        var radius = Math.Max(0, size / 2 - 1);
        var top = (ActualHeight - size) / 2;
        var center = new Point(size / 2 + Padding, ActualHeight / 2);
        var wheelBounds = new Rect(Padding, top, size, size);
        var sliderBounds = new Rect(
            Padding + size + 18,
            top + 2,
            17,
            Math.Max(1, size - 4));

        return new PickerGeometry(center, radius, wheelBounds, sliderBounds);
    }

    private Point GetWheelPoint(PickerGeometry geometry)
    {
        var angle = _hue * Math.PI / 180;
        var radius = geometry.Radius * _saturation;
        return new Point(
            geometry.Center.X + Math.Cos(angle) * radius,
            geometry.Center.Y + Math.Sin(angle) * radius);
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var hueSection = hue / 60;
        var secondary = chroma * (1 - Math.Abs((hueSection % 2) - 1));
        var (red, green, blue) = hueSection switch
        {
            < 1 => (chroma, secondary, 0d),
            < 2 => (secondary, chroma, 0d),
            < 3 => (0d, chroma, secondary),
            < 4 => (0d, secondary, chroma),
            < 5 => (secondary, 0d, chroma),
            _ => (chroma, 0d, secondary)
        };
        var match = value - chroma;
        return Color.FromRgb(
            ToByte(red + match),
            ToByte(green + match),
            ToByte(blue + match));
    }

    private static void ToHsv(Color color, out double hue, out double saturation, out double value)
    {
        var red = color.R / 255d;
        var green = color.G / 255d;
        var blue = color.B / 255d;
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        var delta = maximum - minimum;

        if (delta == 0)
        {
            hue = 0;
        }
        else if (maximum == red)
        {
            hue = 60 * (((green - blue) / delta) % 6);
        }
        else if (maximum == green)
        {
            hue = 60 * (((blue - red) / delta) + 2);
        }
        else
        {
            hue = 60 * (((red - green) / delta) + 4);
        }

        hue = (hue + 360) % 360;
        saturation = maximum == 0 ? 0 : delta / maximum;
        value = maximum;
    }

    private static byte ToByte(double value) =>
        (byte)Math.Round(Math.Clamp(value, 0, 1) * byte.MaxValue);

    private readonly record struct PickerGeometry(
        Point Center,
        double Radius,
        Rect WheelBounds,
        Rect SliderBounds);

    private enum DragTarget
    {
        None,
        Wheel,
        Value
    }
}
