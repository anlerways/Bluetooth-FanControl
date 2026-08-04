using System.Windows;
using System.Windows.Threading;
using FanControl.Service.Host;
using FanControl.UI.Localization;

namespace FanControl.UI.Views;

public partial class DashboardView : System.Windows.Controls.UserControl
{
    private readonly FanControlRuntime _runtime;
    private readonly DispatcherTimer _timer;
    private DateTimeOffset _lastTimestamp;
    private bool _hasData;

    public DashboardView(FanControlRuntime runtime)
    {
        _runtime = runtime;
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => RefreshData();

        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();

        Loaded += (_, _) =>
        {
            // 数据到达即时刷新：不能在后台线程访问 UI 属性，直接切回 UI 线程
            _runtime.DataChanged += OnRuntimeDataChanged;
            _runtime.TemperatureSourceChanged += OnSourceChanged;
        };

        Unloaded += (_, _) =>
        {
            _timer.Stop();
            // 解绑事件，避免语言切换重建窗口后旧视图被运行时事件长期持有（内存泄漏）
            _runtime.DataChanged -= OnRuntimeDataChanged;
            _runtime.TemperatureSourceChanged -= OnSourceChanged;
        };
    }

    private void OnRuntimeDataChanged(object? sender, EventArgs e)
    {
        // 只能在 UI 线程访问 Window.GetWindow；这里只入队，可见性判断放在 RefreshData 里（UI 线程执行）
        Dispatcher.BeginInvoke(RefreshData);
    }

    private void OnSourceChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(() => Trend.Clear());

    /// <summary>进入后台/切回前台时清空趋势，重新开始记录。</summary>
    public void ClearTrend() => Trend.Clear();

    private void RefreshData()
    {
        // 窗口隐藏到托盘时不更新 UI（标签/图表/字符串都会产生分配），保持后台内存稳定
        if (Window.GetWindow(this) is not { IsVisible: true })
        {
            return;
        }

        var packet = _runtime.LatestPacket;

        if (packet is not null && packet.Timestamp != _lastTimestamp)
        {
            _lastTimestamp = packet.Timestamp;
            _hasData = true;

            var unit = _runtime.CurrentConfig.TemperatureUnit;
            var suffix = TemperatureUnitHelper.Suffix(unit);
            CpuValue.Text = double.IsNaN(packet.CpuTemperatureCelsius)
                ? "--"
                : $"{TemperatureUnitHelper.ToDisplay(packet.CpuTemperatureCelsius, unit):0.0} {suffix}";
            GpuValue.Text = double.IsNaN(packet.GpuTemperatureCelsius)
                ? "--"
                : $"{TemperatureUnitHelper.ToDisplay(packet.GpuTemperatureCelsius, unit):0.0} {suffix}";
            PwmValue.Text = double.IsNaN(packet.PwmPercent)
                ? "--"
                : $"{packet.PwmPercent:0} %";
            RpmValue.Text = packet.FanRpm > 0 ? $"{packet.FanRpm:0} RPM" : "--";
            ModeValue.Text = ModeText(packet.FanControlMode);
            StatusValue.Text = LocalizationManager.Get("Dash.Normal");

            Trend.TemperatureUnit = unit;
            Trend.AddPoint(packet.CpuTemperatureCelsius, packet.GpuTemperatureCelsius, packet.PwmPercent);
            StatusText.Text = string.Format(LocalizationManager.Get("Dash.Running"), DateTime.Now.ToString("HH:mm:ss"));
            StatusText.Foreground = ThemeService.Brush("TextSecondaryBrush");
        }

        if (!_hasData && _runtime.LastError is not null)
        {
            StatusText.Text = string.Format(LocalizationManager.Get("Status.TempError"), _runtime.LastError);
            StatusText.Foreground = ThemeService.Brush("ErrorBrush");
            StatusValue.Text = LocalizationManager.Get("Dash.Abnormal");
        }
    }

    private static string ModeText(string mode) => mode switch
    {
        "Manual" => LocalizationManager.Get("Mode.Manual"),
        "CpuTemp" => LocalizationManager.Get("Mode.CpuTemp"),
        "GpuTemp" => LocalizationManager.Get("Mode.GpuTemp"),
        "MixedAvg" => LocalizationManager.Get("Mode.MixedAvg"),
        "Mixed" => LocalizationManager.Get("Mode.Mixed"),
        "TargetRpm" => LocalizationManager.Get("Mode.TargetRpm"),
        "SystemFan" => LocalizationManager.Get("Mode.SystemFan"),
        _ => mode,
    };
}
