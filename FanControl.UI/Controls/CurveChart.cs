using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using FanControl.Shared.Enums;
using FanControl.UI.Localization;

namespace FanControl.UI.Controls;

/// <summary>可编辑温度-PWM 曲线图：拖拽移动点、双击添加、右键删除。</summary>
public sealed class CurveChart : FrameworkElement
{
    private const double MinY = 0;
    private const double MaxY = 100;
    private const double HitRadius = 10;

    private readonly List<EditablePoint> _points = new();
    private EditablePoint? _dragging;
    private TemperatureUnit _unit = TemperatureUnit.Celsius;
    private double _minX = 0;
    private double _maxX = 120;
    private bool _isRpmMode;

    /// <summary>切换 X 轴模式：false = 温度曲线（0-120℃/℉），true = 转速曲线（0-10000 RPM）。</summary>
    public void SetRpmMode(bool rpmMode)
    {
        if (_isRpmMode == rpmMode)
        {
            return;
        }

        _isRpmMode = rpmMode;
        _minX = 0;
        _maxX = rpmMode ? 10000 : 120;
        InvalidateVisual();
    }

    public TemperatureUnit TemperatureUnit
    {
        get => _unit;
        set
        {
            if (_unit != value)
            {
                _unit = value;
                InvalidateVisual();
            }
        }
    }

    public IReadOnlyList<EditablePoint> Points => _points;

    public EditablePoint? SelectedPoint { get; private set; }

    public event EventHandler? SelectedChanged;

    public event EventHandler? PointsChanged;

    public CurveChart()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public void SetPoints(IEnumerable<EditablePoint> points)
    {
        _points.Clear();
        _points.AddRange(points);
        SelectedPoint = _points.Count > 0 ? _points[0] : null;
        InvalidateVisual();
    }

    public void AddPointAt(double temperature, double pwm)
    {
        var point = new EditablePoint(
            Math.Clamp(temperature, _minX, _maxX),
            Math.Clamp(pwm, MinY, MaxY));
        _points.Add(point);
        SelectedPoint = point;
        SelectedChanged?.Invoke(this, EventArgs.Empty);
        PointsChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void RemoveSelected()
    {
        if (SelectedPoint is null || !_points.Remove(SelectedPoint))
        {
            return;
        }

        SelectedPoint = _points.Count > 0 ? _points[Math.Min(0, _points.Count - 1)] : null;
        SelectedChanged?.Invoke(this, EventArgs.Empty);
        PointsChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void SelectPoint(EditablePoint point)
    {
        if (!_points.Contains(point) || ReferenceEquals(SelectedPoint, point))
        {
            return;
        }

        SelectedPoint = point;
        SelectedChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        var pos = e.GetPosition(this);

        if (e.ChangedButton == MouseButton.Left)
        {
            var point = HitTest(pos);
            if (point is null && e.ClickCount >= 2)
            {
                var (tx, ty) = ScreenToData(pos);
                AddPointAt(tx, ty);
            }
            else if (point is not null)
            {
                _dragging = point;
                SelectPoint(point);
                CaptureMouse();
            }
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            var point = HitTest(pos);
            if (point is not null)
            {
                SelectPoint(point);
                RemoveSelected();
            }
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            var (tx, ty) = ScreenToData(e.GetPosition(this));
            _dragging.Temperature = Math.Clamp(tx, _minX, _maxX);
            _dragging.Pwm = Math.Clamp(ty, MinY, MaxY);
            PointsChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_dragging is not null)
        {
            _dragging = null;
            ReleaseMouseCapture();
        }

        base.OnMouseUp(e);
    }

    private EditablePoint? HitTest(Point pos)
    {
        for (var i = _points.Count - 1; i >= 0; i--)
        {
            var screen = DataToScreen(_points[i].Temperature, _points[i].Pwm);
            var dx = pos.X - screen.X;
            var dy = pos.Y - screen.Y;
            if (dx * dx + dy * dy <= HitRadius * HitRadius)
            {
                return _points[i];
            }
        }

        return null;
    }

    private (double X, double Y) ScreenToData(Point pos)
    {
        var plot = PlotRect();
        var tx = _minX + (pos.X - plot.Left) / plot.Width * (_maxX - _minX);
        var ty = MaxY - (pos.Y - plot.Top) / plot.Height * (MaxY - MinY);
        return (tx, ty);
    }

    private Point DataToScreen(double tx, double ty)
    {
        var plot = PlotRect();
        var x = plot.Left + (tx - _minX) / (_maxX - _minX) * plot.Width;
        var y = plot.Bottom - (ty - MinY) / (MaxY - MinY) * plot.Height;
        return new Point(x, y);
    }

    private Rect PlotRect()
    {
        return new Rect(
            52,
            12,
            Math.Max(10, RenderSize.Width - 66),
            Math.Max(10, RenderSize.Height - 34));
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(
            ThemeService.Brush("ChartBgBrush"),
            new Pen(ThemeService.Brush("BorderBrush"), 1),
            new Rect(RenderSize));

        var plot = PlotRect();
        var gridPen = new Pen(ThemeService.Brush("ChartGridBrush"), 1)
        {
            DashStyle = new DashStyle(new double[] { 2, 3 }, 0),
        };

        var xGridStep = _isRpmMode ? 2000.0 : 20.0;
        var xLabelStep = _isRpmMode ? 2000.0 : 30.0;

        for (var t = _minX; t <= _maxX + 0.001; t += xGridStep)
        {
            var x = plot.Left + (t - _minX) / (_maxX - _minX) * plot.Width;
            dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }

        for (var p = 0; p <= MaxY; p += 20)
        {
            var y = plot.Bottom - p / MaxY * plot.Height;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(dc, $"{p}", 46, y, ThemeService.Brush("TextSecondaryBrush"), textRight: true);
        }

        DrawText(dc, LocalizationManager.Get("Curve.AxisY"), 4, plot.Top, ThemeService.Brush("TextSecondaryBrush"));

        for (var t = _minX; t <= _maxX + 0.001; t += xLabelStep)
        {
            var x = plot.Left + (t - _minX) / (_maxX - _minX) * plot.Width;
            var display = _isRpmMode ? t : TemperatureUnitHelper.ToDisplay(t, _unit);
            DrawText(dc, $"{display:0}", x, plot.Bottom + 4, ThemeService.Brush("TextSecondaryBrush"));
        }

        var axisName = _isRpmMode
            ? LocalizationManager.Get("Curve.AxisRpm")
            : _unit == TemperatureUnit.Fahrenheit
                ? LocalizationManager.Get("Curve.AxisXF")
                : LocalizationManager.Get("Curve.AxisX");
        DrawText(dc, axisName, plot.Left + plot.Width / 2, plot.Bottom + 18, ThemeService.Brush("TextSecondaryBrush"));

        if (_points.Count == 0)
        {
            DrawText(dc, LocalizationManager.Get("Curve.Empty"), plot.Left + plot.Width / 2, plot.Top + plot.Height / 2, ThemeService.Brush("TextSecondaryBrush"));
            return;
        }

        var sorted = _points.OrderBy(p => p.Temperature).ToList();
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (var i = 0; i < sorted.Count; i++)
            {
                var point = DataToScreen(sorted[i].Temperature, sorted[i].Pwm);
                if (i == 0)
                {
                    ctx.BeginFigure(point, false, false);
                }
                else
                {
                    ctx.LineTo(point, true, false);
                }
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(ThemeService.Brush("AccentBrush"), 2), geometry);

        foreach (var point in _points)
        {
            var screen = DataToScreen(point.Temperature, point.Pwm);
            var isSelected = ReferenceEquals(point, SelectedPoint);
            dc.DrawEllipse(
                isSelected ? ThemeService.Brush("AccentBrush") : ThemeService.Brush("ChartBgBrush"),
                new Pen(isSelected ? ThemeService.Brush("TextPrimaryBrush") : ThemeService.Brush("AccentBrush"), 1.5),
                screen,
                7,
                7);
        }
    }

    private static void DrawText(
        DrawingContext dc,
        string text,
        double x,
        double y,
        Brush brush,
        bool textRight = false)
    {
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"),
            10,
            brush,
            VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip);

        if (textRight)
        {
            x -= formatted.Width;
        }

        dc.DrawText(formatted, new Point(x, y - formatted.Height / 2));
    }
}

/// <summary>曲线可编辑点（UI 工作副本）。</summary>
public sealed class EditablePoint : INotifyPropertyChanged
{
    private double _temperature;
    private double _pwm;
    private string _xText = string.Empty;

    public EditablePoint(double temperature, double pwm, string xText = "")
    {
        _temperature = temperature;
        _pwm = pwm;
        _xText = xText;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public double Temperature
    {
        get => _temperature;
        set
        {
            if (Math.Abs(_temperature - value) > 0.001)
            {
                _temperature = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Temperature)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
            }
        }
    }

    /// <summary>X 轴显示文本（如 "30 °C" 或 "3000 RPM"），由视图按曲线类型设置。</summary>
    public string XText
    {
        get => _xText;
        set
        {
            if (_xText != value)
            {
                _xText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(XText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
            }
        }
    }

    public string Display => $"{XText} / {Pwm:0.#} %";

    public double Pwm
    {
        get => _pwm;
        set
        {
            if (Math.Abs(_pwm - value) > 0.001)
            {
                _pwm = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Pwm)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
            }
        }
    }
}
