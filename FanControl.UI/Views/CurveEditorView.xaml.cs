using System.Windows;
using System.Windows.Controls;
using FanControl.Service.Host;
using FanControl.Shared.Models;
using FanControl.UI.Controls;
using FanControl.UI.Localization;

namespace FanControl.UI.Views;

public partial class CurveEditorView : UserControl
{
    private readonly FanControlRuntime _runtime;
    private readonly List<EditablePoint> _tempPoints = new();
    private readonly List<EditablePoint> _rpmPoints = new();
    private bool _syncing;
    private bool _initialized;
    private bool _isRpmCurve;
    private bool _chartInitialized;

    public CurveEditorView(FanControlRuntime runtime)
    {
        _runtime = runtime;
        InitializeComponent();

        CurveCombo.ItemsSource = new[]
        {
            LocalizationManager.Get("Curve.TypeTemp"),
            LocalizationManager.Get("Curve.TypeRpm"),
        };
        CurveCombo.SelectionChanged += CurveCombo_SelectionChanged;

        Chart.SelectedChanged += (_, _) => SyncChartToChart();
        Chart.PointsChanged += (_, _) =>
        {
            RefreshSelectedInfo();
            SyncChartToChart();
        };

        Loaded += (_, _) =>
        {
            Chart.TemperatureUnit = _runtime.CurrentConfig.TemperatureUnit;
            RefreshSelectedInfo();
            if (!_initialized)
            {
                _initialized = true;
                LoadPoints();
            }
        };
    }

    private void LoadPoints()
    {
        _tempPoints.Clear();
        _tempPoints.AddRange(
            _runtime.CurrentConfig.Curve.Select(p => new EditablePoint(p.TemperatureCelsius, p.PwmPercent)));
        _rpmPoints.Clear();
        _rpmPoints.AddRange(
            _runtime.CurrentConfig.RpmCurve.Select(p => new EditablePoint(p.Rpm, p.PwmPercent)));

        ShowCurve(isRpm: false);
        CurveCombo.SelectedIndex = 0;
        StatusText.Text = LocalizationManager.Get("Settings.Loaded");
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        Chart.AddPointAt(_isRpmCurve ? 3000 : 60, 50);
        RefreshList();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        Chart.RemoveSelected();
        RefreshList();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_isRpmCurve)
        {
            Chart.SetPoints(new[]
            {
                new EditablePoint(0, 0),
                new EditablePoint(2000, 25),
                new EditablePoint(4000, 45),
                new EditablePoint(6000, 65),
                new EditablePoint(8000, 85),
                new EditablePoint(10000, 100),
            });
        }
        else
        {
            Chart.SetPoints(new[]
            {
                new EditablePoint(30, 20),
                new EditablePoint(50, 35),
                new EditablePoint(70, 60),
                new EditablePoint(90, 100),
            });
        }

        RefreshList();
        StatusText.Text = LocalizationManager.Get("Curve.ResetDone");
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CommitCurrent();

            var temperaturePoints = _tempPoints
                .Select(p => new CurvePoint(p.Temperature, p.Pwm))
                .OrderBy(p => p.TemperatureCelsius)
                .ToList();
            var rpmPoints = _rpmPoints
                .Select(p => new RpmCurvePoint(p.Temperature, p.Pwm))
                .OrderBy(p => p.Rpm)
                .ToList();

            for (var i = 1; i < temperaturePoints.Count; i++)
            {
                if (temperaturePoints[i].TemperatureCelsius <= temperaturePoints[i - 1].TemperatureCelsius)
                {
                    StatusText.Text = LocalizationManager.Get("Curve.SaveFail");
                    return;
                }
            }

            for (var i = 1; i < rpmPoints.Count; i++)
            {
                if (rpmPoints[i].Rpm <= rpmPoints[i - 1].Rpm)
                {
                    StatusText.Text = LocalizationManager.Get("Curve.SaveFail");
                    return;
                }
            }

            // 防止误把空曲线保存覆盖已有曲线
            if (temperaturePoints.Count == 0 || rpmPoints.Count == 0)
            {
                StatusText.Text = LocalizationManager.Get("Curve.SaveFail");
                return;
            }

            await _runtime.ApplyConfigAsync(
                _runtime.CurrentConfig with
                {
                    Curve = temperaturePoints,
                    RpmCurve = rpmPoints,
                });
            StatusText.Text = LocalizationManager.Get("Curve.Saved");
        }
        catch (Exception ex)
        {
            StatusText.Text = string.Format(LocalizationManager.Get("Settings.SaveFailed"), ex.Message);
        }
    }

    private void RefreshList()
    {
        _syncing = true;
        try
        {
            ApplyXText(Chart.Points);
            PointList.ItemsSource = Chart.Points.OrderBy(p => p.Temperature).ToList();
            SyncChartToChart();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void SyncChartToChart()
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            PointList.SelectedItem = Chart.SelectedPoint;
            RefreshSelectedInfo();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void PointList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || PointList.SelectedItem is not EditablePoint point)
        {
            return;
        }

        Chart.SelectPoint(point);
        RefreshSelectedInfo();
    }

    private void RefreshSelectedInfo()
    {
        SelectedInfo.Text = Chart.SelectedPoint is { } selected
            ? _isRpmCurve
                ? string.Format(
                    LocalizationManager.Get("Curve.SelectedRpm"),
                    selected.Temperature.ToString("0.#"),
                    selected.Pwm.ToString("0.#"))
                : string.Format(
                    LocalizationManager.Get("Curve.Selected"),
                    TemperatureUnitHelper.ToDisplay(selected.Temperature, _runtime.CurrentConfig.TemperatureUnit).ToString("0.#"),
                    selected.Pwm.ToString("0.#"))
            : LocalizationManager.Get("Curve.NotSelected");
    }

    private void CurveCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || CurveCombo.SelectedIndex < 0)
        {
            return;
        }

        ShowCurve(isRpm: CurveCombo.SelectedIndex == 1);
    }

    /// <summary>把当前图表中的点回写到对应曲线的备份列表（切页/保存前调用，保留未保存编辑）。</summary>
    private void CommitCurrent()
    {
        if (_isRpmCurve)
        {
            _rpmPoints.Clear();
            _rpmPoints.AddRange(Chart.Points);
        }
        else
        {
            _tempPoints.Clear();
            _tempPoints.AddRange(Chart.Points);
        }
    }

    private void ShowCurve(bool isRpm)
    {
        // 首次加载时图表为空，回写会清空刚从配置读入的点，因此只在图表已初始化后回写
        if (_chartInitialized)
        {
            CommitCurrent();
        }

        _isRpmCurve = isRpm;
        Chart.SetRpmMode(isRpm);
        Chart.SetPoints(isRpm ? _rpmPoints : _tempPoints);
        _chartInitialized = true;
        RefreshList();
    }

    private void ApplyXText(IEnumerable<EditablePoint> points)
    {
        if (_isRpmCurve)
        {
            foreach (var point in points)
            {
                point.XText = $"{point.Temperature:0.#} RPM";
            }
        }
        else
        {
            var unit = _runtime.CurrentConfig.TemperatureUnit;
            var suffix = TemperatureUnitHelper.Suffix(unit);
            foreach (var point in points)
            {
                point.XText =
                    $"{TemperatureUnitHelper.ToDisplay(point.Temperature, unit):0.#} {suffix}";
            }
        }
    }
}
