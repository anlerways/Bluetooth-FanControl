using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using FanControl.Service.Host;
using FanControl.UI.Localization;
using FanControl.UI.Views;

namespace FanControl.UI;

public partial class MainWindow : Window
{
    private readonly FanControlRuntime _runtime;
    private readonly DispatcherTimer _statusTimer;
    private readonly Dictionary<string, object> _views = new();
    private bool _hasData;

    public MainWindow(FanControlRuntime runtime)
    {
        _runtime = runtime;
        InitializeComponent();

        NavList.SelectedIndex = 0;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();

        var memoryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        memoryTimer.Tick += (_, _) =>
        {
            if (!IsVisible)
            {
                // 先强制回收托管垃圾，再裁剪工作集，效果更好
                GC.Collect();
                GC.WaitForPendingFinalizers();
                MemoryHelper.Trim();
            }
        };
        memoryTimer.Start();

        SourceInitialized += (_, _) => ApplyDarkTitleBar(ThemeService.IsDark);
        Closing += MainWindow_Closing;
        Loaded += (_, _) => ThemeService.ApplyThemeType(((App)Application.Current).Runtime.CurrentConfig.Theme);

        // 后台驻留时清空趋势曲线；回到前台时重新开始记录
        IsVisibleChanged += (_, _) =>
        {
            if (_views.TryGetValue("dashboard", out var view) && view is DashboardView dashboard)
            {
                dashboard.ClearTrend();
            }
        };
    }

    private void NavList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not System.Windows.Controls.ListBoxItem { Tag: string tag })
        {
            return;
        }

        PageTitle.Text = tag switch
        {
            "curve" => LocalizationManager.Get("Page.Curve"),
            "settings" => LocalizationManager.Get("Page.Settings"),
            "about" => LocalizationManager.Get("Page.About"),
            _ => LocalizationManager.Get("Page.Dashboard"),
        };

        if (!_views.TryGetValue(tag, out var view))
        {
            view = tag switch
            {
                "curve" => new CurveEditorView(_runtime),
                "settings" => new SettingsView(_runtime),
                "about" => new AboutView(),
                _ => new DashboardView(_runtime),
            };
            _views[tag] = view;
        }

        // 复用视图：切回仪表盘时保留趋势历史，切回曲线页时保留未保存的编辑
        PageHost.Content = view;
    }

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        ((App)Application.Current).RequestExit();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        var app = (App)Application.Current;
        if (!app.IsExiting && !app.IsRestarting)
        {
            e.Cancel = true;
            Hide();
            MemoryHelper.Trim();
        }
    }

    private void UpdateStatus()
    {
        if (_runtime.LatestPacket is not null)
        {
            _hasData = true;
        }

        if (_runtime.LastError is not null)
        {
            StatusText.Text = string.Format(LocalizationManager.Get("Status.TempError"), _runtime.LastError);
            StatusText.Foreground = ThemeService.Brush("ErrorBrush");
        }
        else if (_hasData)
        {
            StatusText.Text = LocalizationManager.Get("Status.Running");
            StatusText.Foreground = ThemeService.Brush("TextSecondaryBrush");
        }
        else
        {
            StatusText.Text = LocalizationManager.Get("Status.Waiting");
            StatusText.Foreground = ThemeService.Brush("TextSecondaryBrush");
        }
    }

    internal void ApplyDarkTitleBar(bool dark)
    {
        if (!IsLoaded || PresentationSource.FromVisual(this) is not HwndSource source)
        {
            return;
        }

        var value = dark ? 1 : 0;
        DwmSetWindowAttribute(source.Handle, 20, ref value, sizeof(int));
    }

    [DllImport("DwmApi")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
