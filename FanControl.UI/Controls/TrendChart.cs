using System.Windows;
using System.Windows.Media;
using FanControl.Shared.Enums;
using FanControl.UI.Localization;

namespace FanControl.UI.Controls;

/// <summary>温度趋势图（自绘）：固定长度历史缓冲 + 平滑曲线 + CPU/GPU 温度（左轴）+ 目标 PWM（右轴）。</summary>
public sealed class TrendChart : FrameworkElement
{
    private const int MaxPoints = 240;
    private readonly List<(double Cpu, double Gpu, double Pwm)> _history = new();
    private TemperatureUnit _unit = TemperatureUnit.Celsius;

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

    public TrendChart()
    {
        ClipToBounds = true;
    }

    public void AddPoint(double cpu, double gpu, double pwm)
    {
        _history.Add((cpu, gpu, pwm));
        while (_history.Count > MaxPoints)
        {
            _history.RemoveAt(0);
        }

        InvalidateVisual();
    }

    public void Clear()
    {
        _history.Clear();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bgBrush = ThemeService.Brush("ChartBgBrush");
        var borderPen = new Pen(ThemeService.Brush("BorderBrush"), 1);
        var gridPen = new Pen(ThemeService.Brush("ChartGridBrush"), 1)
        {
            DashStyle = new DashStyle(new double[] { 2, 3 }, 0),
        };

        dc.DrawRectangle(bgBrush, borderPen, new Rect(RenderSize));

        if (_history.Count == 0)
        {
            DrawText(dc, LocalizationManager.Get("Chart.Waiting"), RenderSize.Width / 2, RenderSize.Height / 2, ThemeService.Brush("TextSecondaryBrush"));
            return;
        }

        // 左侧留温度轴，右侧留 PWM 轴
        var plot = new Rect(48, 8, Math.Max(10, RenderSize.Width - 112), Math.Max(10, RenderSize.Height - 30));

        var values = _history
            .Select(p => Math.Max(p.Cpu, p.Gpu))
            .Concat(_history.Where(p => !double.IsNaN(p.Gpu)).Select(p => p.Gpu))
            .Where(v => !double.IsNaN(v))
            .ToList();

        // 所有温度源都无有效数据时（例如某个源获取失败）不画温度轴，避免 NaN 坐标导致渲染异常
        if (values.Count == 0)
        {
            return;
        }

        var minTemp = Math.Max(0, Math.Floor((values.Min() - 2) / 10) * 10);
        var maxTemp = Math.Max(minTemp + 10, Math.Ceiling((values.Max() + 2) / 10) * 10);

        for (var t = minTemp; t <= maxTemp + 0.001; t += 10)
        {
            var y = plot.Bottom - (t - minTemp) / (maxTemp - minTemp) * plot.Height;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var display = TemperatureUnitHelper.ToDisplay(t, _unit);
            DrawText(dc, $"{display:0}°", 40, y, ThemeService.Brush("TextSecondaryBrush"), textRight: true);
        }

        // 右轴：目标 PWM 0-100%
        for (var p = 0; p <= 100.001; p += 20)
        {
            var y = plot.Bottom - p / 100.0 * plot.Height;
            dc.DrawLine(gridPen, new Point(plot.Right, y), new Point(plot.Right + 40, y));
            DrawText(dc, $"{p:0}", plot.Right + 46, y, ThemeService.Brush("TextSecondaryBrush"));
        }

        DrawSmoothLine(dc, plot, minTemp, maxTemp, p => p.Cpu, new Pen(ThemeService.Brush("AccentBrush"), 2));
        if (_history.Any(p => !double.IsNaN(p.Gpu)))
        {
            DrawSmoothLine(dc, plot, minTemp, maxTemp, p => p.Gpu, new Pen(ThemeService.Brush("GpuBrush"), 2));
        }

        var pwmPen = new Pen(Brushes.Orange, 1.5)
        {
            DashStyle = new DashStyle(new double[] { 4, 3 }, 0),
        };
        DrawSmoothLine(dc, plot, 0, 100, p => p.Pwm, pwmPen, useRightAxis: true);

        DrawLegend(dc, plot.Right - 128, plot.Top, ThemeService.Brush("AccentBrush"), LocalizationManager.Get("Chart.Cpu"));
        if (_history.Any(p => !double.IsNaN(p.Gpu)))
        {
            DrawLegend(dc, plot.Right - 86, plot.Top, ThemeService.Brush("GpuBrush"), LocalizationManager.Get("Chart.Gpu"));
        }

        DrawLegend(dc, plot.Right - 44, plot.Top, Brushes.Orange, LocalizationManager.Get("Chart.Pwm"));
    }

    private void DrawSmoothLine(
        DrawingContext dc,
        Rect plot,
        double minTemp,
        double maxTemp,
        Func<(double Cpu, double Gpu, double Pwm), double> selector,
        Pen pen,
        bool useRightAxis = false)
    {
        var points = new List<Point>();
        for (var i = 0; i < _history.Count; i++)
        {
            var value = selector(_history[i]);
            if (double.IsNaN(value))
            {
                continue;
            }

            var x = plot.Left + i / (double)(MaxPoints - 1) * plot.Width;
            var range = useRightAxis ? 100.0 : (maxTemp - minTemp);
            var baseValue = useRightAxis ? 0.0 : minTemp;
            var y = plot.Bottom - (value - baseValue) / range * plot.Height;
            y = Math.Clamp(y, plot.Top, plot.Bottom);
            points.Add(new Point(x, y));
        }

        if (points.Count < 2)
        {
            return;
        }

        // Catmull-Rom 转三次贝塞尔：曲线平滑过渡
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            if (points.Count == 2)
            {
                ctx.LineTo(points[1], true, false);
            }
            else
            {
                for (var i = 0; i < points.Count - 1; i++)
                {
                    var p0 = points[Math.Max(0, i - 1)];
                    var p1 = points[i];
                    var p2 = points[i + 1];
                    var p3 = points[Math.Min(points.Count - 1, i + 2)];
                    var c1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
                    var c2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
                    ctx.BezierTo(c1, c2, p2, true, false);
                }
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static void DrawLegend(DrawingContext dc, double x, double y, Brush brush, string name)
    {
        dc.DrawRectangle(brush, null, new Rect(x, y + 5, 12, 3));
        DrawText(dc, name, x + 17, y, ThemeService.Brush("TextSecondaryBrush"));
    }

    private static void DrawText(DrawingContext dc, string text, double x, double y, Brush brush, bool textRight = false)
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
