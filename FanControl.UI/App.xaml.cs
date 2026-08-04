using System.Windows;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Threading;
using FanControl.Service.Communication;
using FanControl.Service.Config;
using FanControl.Service.Fan;
using FanControl.Service.Hardware;
using FanControl.Service.Host;
using FanControl.Service.Logging;
using FanControl.Service.Tray;
using FanControl.Shared.Enums;
using FanControl.Shared.Models;
using FanControl.UI.Localization;
using FanControl.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FanControl.UI;

/// <summary>
/// 单进程主程序：WPF UI（G-Helper 风格）+ 托盘 + 后台监控循环。
/// 退出时依次释放托盘、监控循环（含串口/传感器）、DI 容器。
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;
    private FanControlRuntime? _runtime;
    private TrayHost? _tray;
    private MainWindow? _mainWindow;
    private bool _exiting;
    private EventWaitHandle? _showWindowEvent;
    private DispatcherTimer? _trayStatusTimer;
    internal bool IsRestarting { get; private set; }
    private string _lastTrayStatus = string.Empty;
    private DateTime _lastTempErrorNotify = DateTime.MinValue;
    private DateTime _lastBleDisconnectNotify = DateTime.MinValue;
    private static readonly TimeSpan NotifyCooldown = TimeSpan.FromMinutes(5);

    public FanControlRuntime Runtime =>
        _runtime ?? throw new InvalidOperationException("运行时尚未初始化。");

    public bool IsExiting => _exiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 开机自启（任务计划带 --autostart）：不弹 UAC、不显示主窗口，直接驻留托盘。
        var isAutoStart = e.Args.Contains("--autostart", StringComparer.OrdinalIgnoreCase);

        // 手动启动时需要管理员权限（访问硬件传感器/串口）：非管理员时通过 UAC 提升后重启（退出当前实例）。
        // 必须在单实例 EventWaitHandle 之前执行，避免两个实例抢同一个命名事件。
        if (!IsAdministrator() && !isAutoStart)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                });
            }
            catch
            {
                // 用户取消 UAC 提升：直接退出，不弹 740 硬错误
            }

            Shutdown();
            return;
        }

        // 单实例限制：已存在进程时通知其显示主窗口，然后退出当前进程
        _showWindowEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            @"Local\FanControl.ShowWindow",
            out var firstInstance);

        if (!firstInstance)
        {
            try
            {
                _showWindowEvent.Set();
            }
            catch
            {
                // 通知失败也照常退出
            }

            Shutdown();
            return;
        }

        var watcher = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    _showWindowEvent.WaitOne();
                }
                catch
                {
                    break;
                }

                Dispatcher.Invoke(ShowMainWindow);
            }
        })
        {
            IsBackground = true,
            Name = "FanControl.ShowWindowWatcher",
        };
        watcher.Start();

        DispatcherUnhandledException += (_, args) =>
        {
            CrashLog.Write("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            CrashLog.Write("AppDomain UnhandledException", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLog.Write("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        // 系统配置（日志位置/开关）先于日志系统读取：首启（无 system.json）弹窗引导选择，
        // 之后可在设置页修改并立即生效。
        SystemConfig systemConfig;
        var earlyConfigManager = new ConfigManager(NullLogger<ConfigManager>.Instance);
        try
        {
            systemConfig = await earlyConfigManager.LoadSystemConfigAsync();
            if (!File.Exists(earlyConfigManager.SystemConfigFilePath) && !isAutoStart)
            {
                var chosen = ShowFirstStartLogLocationDialog(earlyConfigManager);
                if (chosen is not null)
                {
                    systemConfig = chosen;
                    await earlyConfigManager.SaveSystemConfigAsync(chosen);
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("读取系统配置失败", ex);
            systemConfig = new SystemConfig();
        }

        var logDirectory = earlyConfigManager.GetLogDirectory(systemConfig);
        _services = BuildServices(logDirectory, systemConfig.LogEnabled, systemConfig.MaxLogFiles);
        _runtime = _services.GetRequiredService<FanControlRuntime>();

        try
        {
            await _runtime.StartAsync();
        }
        catch (Exception ex)
        {
            CrashLog.Write("运行时启动失败", ex);
        }

        ThemeService.ApplyThemeType(_runtime.CurrentConfig.Theme);
        LocalizationManager.SetLanguage(_runtime.CurrentConfig.Language);

        _mainWindow = new MainWindow(_runtime);
        _mainWindow.Closed += (_, _) =>
        {
            if (_exiting)
            {
                Shutdown();
            }
        };

        _tray = new TrayHost();
        _tray.Start(ShowMainWindow, RequestExit, () => _runtime?.RequestReconnect());

        _trayStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _trayStatusTimer.Tick += (_, _) => UpdateTrayStatus();
        _trayStatusTimer.Start();

        // 通知：蓝牙断连 / 温度获取异常（受设置页开关控制）
        _runtime.BleConnectionChanged += (_, connected) =>
        {
            if (!connected
                && _runtime.CurrentConfig.NotifyOnBleDisconnect
                && DateTime.Now - _lastBleDisconnectNotify >= NotifyCooldown)
            {
                _lastBleDisconnectNotify = DateTime.Now;
                _tray?.ShowNotification("FanControl", "蓝牙连接已断开，正在按重连轮询自动重试。");
            }
        };

        _runtime.TemperatureErrorOccurred += (_, _) =>
        {
            if (_runtime.CurrentConfig.NotifyOnTemperatureError
                && DateTime.Now - _lastTempErrorNotify >= NotifyCooldown)
            {
                _lastTempErrorNotify = DateTime.Now;
                _tray?.ShowNotification("FanControl", $"温度获取异常：{_runtime.LastError}");
            }
        };

        // 自启时不弹主界面（托盘常驻，双击托盘再显示）
        if (!isAutoStart)
        {
            _mainWindow.Show();
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>语言切换后重建主窗口，让所有 XAML 翻译重新求值、即时生效。</summary>
    public void RestartWindowForLanguage()
    {
        if (_mainWindow is null)
        {
            return;
        }

        IsRestarting = true;
        try
        {
            var oldWindow = _mainWindow;
            _mainWindow = new MainWindow(_runtime!);
            _mainWindow.Closed += (_, _) =>
            {
                if (_exiting)
                {
                    Shutdown();
                }
            };
            _mainWindow.Show();
            oldWindow.Close();
        }
        finally
        {
            IsRestarting = false;
        }
    }


    private void UpdateTrayStatus()
    {
        if (_tray is null)
        {
            return;
        }

        string text;
        var packet = _runtime?.LatestPacket;
        var transport = _runtime?.CurrentConfig.CommunicationType == CommunicationType.Ble
            ? "BLE"
            : "COM";
        var state = _runtime?.ConnectionState switch
        {
            CommunicationState.Connected => LocalizationManager.Get("ConnState.Connected"),
            CommunicationState.Reconnecting => LocalizationManager.Get("ConnState.Reconnecting"),
            _ => LocalizationManager.Get("ConnState.Waiting"),
        };
        var connectionLine = $"{transport} {state}";

        if (packet is null)
        {
            text = _runtime?.LastError is null
                ? connectionLine
                : $"{connectionLine}\n{string.Format(LocalizationManager.Get("Tray.TempError"), _runtime.LastError)}";
        }
        else
        {
            var unit = _runtime?.CurrentConfig.TemperatureUnit ?? FanControl.Shared.Enums.TemperatureUnit.Celsius;
            var cpu = double.IsNaN(packet.CpuTemperatureCelsius)
                ? "--"
                : $"{TemperatureUnitHelper.ToDisplay(packet.CpuTemperatureCelsius, unit):0.0}{TemperatureUnitHelper.Suffix(unit)}";
            var gpu = double.IsNaN(packet.GpuTemperatureCelsius)
                ? "--"
                : $"{TemperatureUnitHelper.ToDisplay(packet.GpuTemperatureCelsius, unit):0.0}{TemperatureUnitHelper.Suffix(unit)}";
            var rpm = packet.FanRpm > 0 ? $"{packet.FanRpm:0} RPM" : "--";
            var pwm = double.IsNaN(packet.PwmPercent) ? "--" : $"{packet.PwmPercent:0}%";
            text = $"{connectionLine}\nCPU {cpu}  GPU {gpu}\nPWM {pwm}  {rpm}";
        }

        if (text != _lastTrayStatus)
        {
            _lastTrayStatus = text;
            _tray.SetStatus(text);
        }
    }

    /// <summary>首启引导弹窗：选择日志文件存放位置（data / 安装目录）。</summary>
    private static SystemConfig? ShowFirstStartLogLocationDialog(ConfigManager configManager)
    {
        var dialog = new LogLocationDialog(configManager.InstallDirectory, configManager.DefaultUserDataDirectory)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private static ServiceProvider BuildServices(
        string logDirectory,
        bool logEnabled,
        int maxLogFiles)
    {
        var services = new ServiceCollection();
        var fileLogger = new FileLoggerProvider(logDirectory);
        fileLogger.Configure(logDirectory, logEnabled, Math.Max(1, maxLogFiles));
        services.AddSingleton(fileLogger);
        services.AddLogging(builder => builder.AddProvider(fileLogger));
        services.AddSingleton<IConfigManager, ConfigManager>();
        services.AddSingleton<AppState>();
        services.AddSingleton<FanController>();
        services.AddSingleton<TemperatureProviderFactory>();
        services.AddSingleton<FanSpeedProviderFactory>();
        services.AddSingleton<AtkAcpiTemperatureProvider>();
        services.AddSingleton<WmiTemperatureProvider>();
        services.AddSingleton<LibreHardwareMonitorTemperatureProvider>();
        services.AddSingleton<Aida64TemperatureProvider>();
        services.AddSingleton<SimulatedTemperatureProvider>();
        services.AddSingleton<AtkAcpiFanSpeedProvider>();
        services.AddSingleton<WmiFanSpeedProvider>();
        services.AddSingleton<LibreHardwareMonitorFanSpeedProvider>();
        services.AddSingleton<Aida64FanSpeedProvider>();
        services.AddSingleton<SimulatedFanSpeedProvider>();
        services.AddSingleton<CommunicationChannelFactory>();
        services.AddSingleton<ComChannel>();
        services.AddSingleton<BleChannel>();
        services.AddSingleton<FanControlRuntime>();
        return services.BuildServiceProvider();
    }

    private void ShowMainWindow()
    {
        Dispatcher.Invoke(() =>
        {
            if (_mainWindow is null)
            {
                return;
            }

            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        });
    }

    internal void RequestExit()
    {
        Dispatcher.Invoke(() =>
        {
            _exiting = true;
            _mainWindow?.Close();
        });
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);

        try
        {
            _tray?.Dispose();
            _trayStatusTimer?.Stop();
            _showWindowEvent?.Dispose();
            if (_runtime is not null)
            {
                await _runtime.DisposeAsync();
            }
        }
        finally
        {
            _services?.Dispose();
        }
    }
}
