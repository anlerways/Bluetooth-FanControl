using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using FanControl.Service.Host;
using FanControl.Service.Tray;
using FanControl.Shared.Enums;
using FanControl.Shared.Models;
using FanControl.UI.Localization;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace FanControl.UI.Views;

public partial class SettingsView : UserControl
{
    private static readonly TemperatureSource[] SourceValues =
    {
        TemperatureSource.AtkAcpi,
        TemperatureSource.Wmi,
        TemperatureSource.LibreHardwareMonitor,
        TemperatureSource.Aida64,
    };

    private static readonly TemperatureSource[] GpuSourceValues =
    {
        TemperatureSource.NvidiaSmiAdl, // 默认：NVIDIA-SMI / ADL
        TemperatureSource.LibreHardwareMonitor,
        TemperatureSource.AtkAcpi,
        TemperatureSource.Aida64,
    };

    // 显卡枚举失败时的兜底选项（按厂商）
    private static readonly string[] GpuFallbackValues =
    {
        "Auto",
        "NVIDIA",
        "AMD",
        "Intel",
    };

    private static readonly FanSpeedSource[] RpmSourceValues =
    {
        FanSpeedSource.AtkAcpi,
        FanSpeedSource.Wmi,
        FanSpeedSource.LibreHardwareMonitor,
        FanSpeedSource.Aida64,
    };

    private static readonly FanControlMode[] ModeValues =
    {
        FanControlMode.Manual,
        FanControlMode.CpuTemp,
        FanControlMode.GpuTemp,
        FanControlMode.MixedAvg,
        FanControlMode.Mixed,
        FanControlMode.TargetRpm,
    };

    private static readonly CommunicationType[] CommValues =
    {
        CommunicationType.Ble,
        CommunicationType.Com,
    };

    private static readonly ThemeType[] ThemeValues =
    {
        ThemeType.System,
        ThemeType.Light,
        ThemeType.Dark,
    };

    private readonly FanControlRuntime _runtime;
    private AppConfig _config = new();
    private SystemConfig _systemConfig = new();
    private bool _loading;
    private bool _initialized;
    private bool _stateSubscribed;
    private readonly DispatcherTimer _autoSaveTimer;

    public SettingsView(FanControlRuntime runtime)
    {
        _runtime = runtime;
        InitializeComponent();

        SourceCombo.ItemsSource = new[]
        {
            LocalizationManager.Get("Option.SourceAtk"),
            LocalizationManager.Get("Option.SourceWmi"),
            LocalizationManager.Get("Option.SourceLhm"),
            LocalizationManager.Get("Option.SourceAida"),
        };
        GpuSourceCombo.ItemsSource = new[]
        {
            LocalizationManager.Get("Option.GpuSourceNvidiaAdl"),
            LocalizationManager.Get("Option.SourceLhm"),
            LocalizationManager.Get("Option.SourceAtk"),
            LocalizationManager.Get("Option.SourceAida"),
        };
        RpmSourceCombo.ItemsSource = new[]
        {
            LocalizationManager.Get("Option.SourceAtk"),
            LocalizationManager.Get("Option.SourceWmi"),
            LocalizationManager.Get("Option.SourceLhm"),
            LocalizationManager.Get("Option.SourceAida"),
        };
        ModeCombo.ItemsSource = new[]
        {
            LocalizationManager.Get("Option.ModeManual"),
            LocalizationManager.Get("Option.ModeCpu"),
            LocalizationManager.Get("Option.ModeGpu"),
            LocalizationManager.Get("Option.ModeMixedAvg"),
            LocalizationManager.Get("Option.ModeMixed"),
            LocalizationManager.Get("Option.ModeRpm"),
        };
        CommCombo.ItemsSource = new[]
        {
            LocalizationManager.Get("Option.CommBle"),
            LocalizationManager.Get("Option.CommCom"),
        };
        ThemeCombo.ItemsSource = new[]
        {
            LocalizationManager.Get("Option.ThemeSystem"),
            LocalizationManager.Get("Option.ThemeLight"),
            LocalizationManager.Get("Option.ThemeDark"),
        };
        LangCombo.ItemsSource = new[]
        {
            LocalizationManager.Get("Settings.LangZh"),
            LocalizationManager.Get("Settings.LangEn"),
        };
        UnitCombo.ItemsSource = new[]
        {
            LocalizationManager.Get("Option.UnitCelsius"),
            LocalizationManager.Get("Option.UnitFahrenheit"),
        };
        LogLocationCombo.ItemsSource = new[]
        {
            LocalizationManager.Get("Option.LogData"),
            LocalizationManager.Get("Option.LogInstall"),
        };

        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _autoSaveTimer.Tick += async (_, _) =>
        {
            _autoSaveTimer.Stop();
            await SaveAsync();
        };

        WireAutoSave();
        Loaded += async (_, _) =>
        {
            // 只加载一次：切回设置页不重置控件，避免改动被加载过程覆盖/吞掉
            if (!_initialized)
            {
                _initialized = true;
                await LoadAsync();
            }
        };

        // 页面切走时若有待保存的改动，立即刷盘（防 400ms 防抖窗口内切页丢失）
        Unloaded += (_, _) =>
        {
            if (_autoSaveTimer.IsEnabled)
            {
                _autoSaveTimer.Stop();
                _ = SaveAsync();
            }

            if (_stateSubscribed)
            {
                _stateSubscribed = false;
                _runtime.ConnectionStateChanged -= OnConnectionStateChanged;
            }
        };

        _runtime.ConnectionStateChanged += OnConnectionStateChanged;
        _stateSubscribed = true;
        UpdateConnectionState();
    }

    private void WireAutoSave()
    {
        SourceCombo.SelectionChanged += (_, _) => MarkDirty();
        GpuSourceCombo.SelectionChanged += (_, _) => MarkDirty();
        GpuCombo.SelectionChanged += (_, _) => MarkDirty();
        RpmSourceCombo.SelectionChanged += (_, _) => MarkDirty();
        ModeCombo.SelectionChanged += (_, _) => MarkDirty();
        CommCombo.SelectionChanged += (_, _) => MarkDirty();
        ThemeCombo.SelectionChanged += (_, _) => MarkDirty();
        ComPortBox.SelectionChanged += (_, _) => MarkDirty();
        BaudBox.TextChanged += (_, _) => MarkDirty();
        BleBox.SelectionChanged += (_, _) => MarkDirty();
        ManualSlider.ValueChanged += (_, _) => MarkDirty();
        SmoothSlider.ValueChanged += (_, _) => MarkDirty();
        SmoothToggle.Checked += (_, _) => MarkDirty();
        SmoothToggle.Unchecked += (_, _) => MarkDirty();
        BlePollSlider.ValueChanged += (_, _) => MarkDirty();
        BleReconnectSlider.ValueChanged += (_, _) => MarkDirty();
        PollSlider.ValueChanged += (_, _) => MarkDirty();
        BleAutoToggle.Checked += (_, _) => MarkDirty();
        BleAutoToggle.Unchecked += (_, _) => MarkDirty();
        NotifyBleToggle.Checked += (_, _) => MarkDirty();
        NotifyBleToggle.Unchecked += (_, _) => MarkDirty();
        NotifyErrorToggle.Checked += (_, _) => MarkDirty();
        NotifyErrorToggle.Unchecked += (_, _) => MarkDirty();
        UnitCombo.SelectionChanged += (_, _) => MarkDirty();
        LogLocationCombo.SelectionChanged += (_, _) => MarkDirty();
        LogToggle.Checked += (_, _) => MarkDirty();
        LogToggle.Unchecked += (_, _) => MarkDirty();
    }

    /// <summary>任一设置变更后延时 400ms 自动保存（防拖动滑块时频繁写盘）。</summary>
    private void MarkDirty()
    {
        if (_loading)
        {
            return;
        }

        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            _config = _runtime.CurrentConfig;
            _systemConfig = await _runtime.LoadSystemConfigAsync();

            SourceCombo.SelectedIndex = IndexOf(SourceValues, _config.TemperatureSource);
            GpuSourceCombo.SelectedIndex = IndexOf(
                GpuSourceValues,
                _config.GpuTemperatureSource ?? TemperatureSource.NvidiaSmiAdl);

            // GPU 选择下拉直接显示真实显卡名（枚举失败时回退到厂商预设）
            var gpuNames = await _runtime.EnumerateGpuNamesAsync();
            var gpuItems = new List<string> { "Auto" };
            gpuItems.AddRange(gpuNames.Count > 0 ? gpuNames : GpuFallbackValues.Skip(1));
            GpuCombo.ItemsSource = gpuItems;
            GpuCombo.SelectedItem = gpuItems.Contains(_config.GpuSelection)
                ? _config.GpuSelection
                : "Auto";

            RpmSourceCombo.SelectedIndex = IndexOf(RpmSourceValues, _config.FanSpeedSource);
            ModeCombo.SelectedIndex = IndexOf(ModeValues, _config.FanControlMode);
            CommCombo.SelectedIndex = IndexOf(CommValues, _config.CommunicationType);
            ThemeCombo.SelectedIndex = IndexOf(ThemeValues, _config.Theme);

            ManualSlider.Value = Math.Clamp(_config.ManualPwmPercent, 0, 100);
            ManualValue.Text = $"{_config.ManualPwmPercent:0} %";
            var ports = GetComPorts();
            if (!ports.Contains(_config.ComPort))
            {
                ports.Add(_config.ComPort);
            }

            ComPortBox.ItemsSource = ports;
            ComPortBox.SelectedItem = _config.ComPort;

            BaudBox.Text = _config.ComBaudRate.ToString();

            var bleNames = await GetBleDeviceNamesAsync();
            if (!string.IsNullOrEmpty(_config.BleDeviceName) && !bleNames.Contains(_config.BleDeviceName))
            {
                bleNames.Add(_config.BleDeviceName);
            }

            BleBox.ItemsSource = bleNames;
            BleBox.SelectedItem = string.IsNullOrEmpty(_config.BleDeviceName) ? null : _config.BleDeviceName;
            PollSlider.Value = Math.Clamp(_config.PollIntervalMilliseconds / 1000.0, 0.1, 5);
            PollValue.Text = $"{PollSlider.Value:0.0}";
            SmoothSlider.Value = Math.Clamp(_config.PwmSmoothing, 0.1, 1);
            SmoothValue.Text = $"{SmoothSlider.Value:0.00}";
            SmoothToggle.IsChecked = _config.SmoothingEnabled;
            SmoothSlider.IsEnabled = _config.SmoothingEnabled;
            BlePollSlider.Value = Math.Clamp(_config.BlePollIntervalSeconds, 1, 30);
            BlePollValue.Text = $"{BlePollSlider.Value:0}";
            BleReconnectSlider.Value = Math.Clamp(_config.BleReconnectIntervalSeconds, 1, 60);
            BleReconnectValue.Text = $"{BleReconnectSlider.Value:0}";
            BleAutoToggle.IsChecked = _config.AutoReconnectBle;
            NotifyBleToggle.IsChecked = _config.NotifyOnBleDisconnect;
            NotifyErrorToggle.IsChecked = _config.NotifyOnTemperatureError;

            AutostartToggle.IsChecked = await AutostartManager.IsEnabledAsync();
            LangCombo.SelectedIndex = LocalizationManager.CurrentLanguage == "en-US" ? 1 : 0;
            UnitCombo.SelectedIndex = _config.TemperatureUnit == TemperatureUnit.Fahrenheit ? 1 : 0;
            LogLocationCombo.SelectedIndex =
                _systemConfig.LogLocation == ConfigLocation.InstallDirectory ? 1 : 0;
            LogToggle.IsChecked = _systemConfig.LogEnabled;
            StatusText.Text = LocalizationManager.Get("Settings.Loaded");
        }
        catch (Exception ex)
        {
            StatusText.Text = string.Format(LocalizationManager.Get("Settings.LoadFailed"), ex.Message);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            var updated = _config with
            {
                TemperatureSource = ValueAt(SourceValues, SourceCombo.SelectedIndex),
                GpuTemperatureSource = ValueAt(GpuSourceValues, GpuSourceCombo.SelectedIndex),
                GpuSelection = GpuCombo.SelectedItem?.ToString() ?? "Auto",
                FanSpeedSource = ValueAt(RpmSourceValues, RpmSourceCombo.SelectedIndex),
                FanControlMode = ValueAt(ModeValues, ModeCombo.SelectedIndex),
                CommunicationType = ValueAt(CommValues, CommCombo.SelectedIndex),
                ComPort = ComPortBox.SelectedItem?.ToString() ?? _config.ComPort,
                ComBaudRate = int.TryParse(BaudBox.Text, out var baud) ? baud : 115200,
                BleDeviceName = BleBox.SelectedItem?.ToString() ?? string.Empty,
                ManualPwmPercent = ManualSlider.Value,
                PollIntervalMilliseconds = (int)Math.Round(PollSlider.Value * 1000),
                PwmSmoothing = SmoothSlider.Value,
                SmoothingEnabled = SmoothToggle.IsChecked == true,
                BlePollIntervalSeconds = (int)Math.Round(BlePollSlider.Value),
                BleReconnectIntervalSeconds = (int)Math.Round(BleReconnectSlider.Value),
                AutoReconnectBle = BleAutoToggle.IsChecked == true,
                NotifyOnBleDisconnect = NotifyBleToggle.IsChecked == true,
                NotifyOnTemperatureError = NotifyErrorToggle.IsChecked == true,
                Language = LangCombo.SelectedIndex == 1 ? "en-US" : "zh-CN",
                TemperatureUnit = UnitCombo.SelectedIndex == 1 ? TemperatureUnit.Fahrenheit : TemperatureUnit.Celsius,
                Theme = ValueAt(ThemeValues, ThemeCombo.SelectedIndex),
            };

            await _runtime.ApplyConfigAsync(updated);
            _config = updated;

            var systemUpdated = _systemConfig with
            {
                LogLocation = LogLocationCombo.SelectedIndex == 1
                    ? ConfigLocation.InstallDirectory
                    : ConfigLocation.UserData,
                LogEnabled = LogToggle.IsChecked == true,
            };
            await _runtime.SaveSystemConfigAsync(systemUpdated);
            _systemConfig = systemUpdated;
            StatusText.Text = LocalizationManager.Get("Settings.AutoSaved");
        }
        catch (Exception ex)
        {
            StatusText.Text = string.Format(LocalizationManager.Get("Settings.SaveFailed"), ex.Message);
        }
    }

    private async void Reload_Click(object sender, RoutedEventArgs e)
        => await LoadAsync();

    private void Reconnect_Click(object sender, RoutedEventArgs e)
    {
        _runtime.RequestReconnect();
        StatusText.Text = LocalizationManager.Get("Settings.ReconnectRequested");
    }

    private async void Autostart_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        try
        {
            if (AutostartToggle.IsChecked == true)
            {
                await AutostartManager.EnableAsync();
                StatusText.Text = LocalizationManager.Get("Autostart.On");
            }
            else
            {
                await AutostartManager.DisableAsync();
                StatusText.Text = LocalizationManager.Get("Autostart.Off");
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = string.Format(LocalizationManager.Get("Autostart.Failed"), ex.Message);
            _loading = true;
            AutostartToggle.IsChecked = !(AutostartToggle.IsChecked == true);
            _loading = false;
        }
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeCombo.SelectedIndex < 0)
        {
            return;
        }

        var theme = ThemeValues[Math.Min(ThemeCombo.SelectedIndex, ThemeValues.Length - 1)];
        ThemeService.ApplyThemeType(theme);

        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.ApplyDarkTitleBar(ThemeService.IsDark);
        }
    }

    private async void LangCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LangCombo.SelectedIndex < 0)
        {
            return;
        }

        var language = LangCombo.SelectedIndex == 1 ? "en-US" : "zh-CN";
        var savedLanguage = _config.Language;

        // 先落盘，再切运行时文化并重建窗口，全部界面即时生效
        _autoSaveTimer.Stop();
        _config = _config with { Language = language };
        try
        {
            await _runtime.ApplyConfigAsync(_config);
            LocalizationManager.SetLanguage(language);
            ((App)Application.Current).RestartWindowForLanguage();
        }
        catch (Exception ex)
        {
            StatusText.Text = string.Format(LocalizationManager.Get("Settings.SaveFailed"), ex.Message);
            LangCombo.SelectedIndex = savedLanguage == "en-US" ? 1 : 0;
        }
    }

    private void ManualSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ManualValue is null)
        {
            return; // XAML 加载阶段 Value 尚未初始化完成
        }

        ManualValue.Text = $"{ManualSlider.Value:0} %";
    }

    private void PollSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PollValue is null)
        {
            return; // XAML 加载阶段 Value 尚未初始化完成
        }

        PollValue.Text = $"{PollSlider.Value:0.0}";
    }

    private void SmoothSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SmoothValue is null)
        {
            return;
        }

        SmoothValue.Text = $"{SmoothSlider.Value:0.00}";
    }

    private void SmoothToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (SmoothSlider is null)
        {
            return;
        }

        SmoothSlider.IsEnabled = SmoothToggle.IsChecked == true;
    }

    private void BlePollSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BlePollValue is null)
        {
            return;
        }

        BlePollValue.Text = $"{BlePollSlider.Value:0}";
    }

    private void BleReconnectSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BleReconnectValue is null)
        {
            return;
        }

        BleReconnectValue.Text = $"{BleReconnectSlider.Value:0}";
    }

    private static int IndexOf<T>(IReadOnlyList<T> values, T value)
    {
        var index = Array.IndexOf(values.ToArray(), value);
        return index < 0 ? 0 : index;
    }

    private static T ValueAt<T>(IReadOnlyList<T> values, int index)
    {
        if (index < 0 || index >= values.Count)
        {
            throw new InvalidOperationException($"无效的选项索引：{index}。");
        }

        return values[index];
    }

    /// <summary>枚举系统串口，未找到当前配置端口时补入（允许保留已有配置）。</summary>
    private static ObservableCollection<string> GetComPorts()
    {
        var ports = new ObservableCollection<string>();
        try
        {
            foreach (var port in SerialPort.GetPortNames().OrderBy(p => p))
            {
                ports.Add(port);
            }
        }
        catch
        {
            // 枚举失败时保持空列表
        }

        return ports;
    }

    /// <summary>枚举系统已配对蓝牙设备名（桌面应用可直接使用 WinRT 枚举）。</summary>
    private static async Task<ObservableCollection<string>> GetBleDeviceNamesAsync()
    {
        var names = new ObservableCollection<string>();
        try
        {
            var selector = BluetoothDevice.GetDeviceSelector();
            var enumeration = DeviceInformation.FindAllAsync(selector).AsTask();
            var finished = await Task.WhenAny(enumeration, Task.Delay(TimeSpan.FromSeconds(5)));
            if (finished != enumeration)
            {
                return names; // 枚举超时，返回空列表（保留当前配置值）
            }

            foreach (var name in enumeration.Result.Select(d => d.Name)
                         .Where(n => !string.IsNullOrWhiteSpace(n))
                         .Distinct()
                         .OrderBy(n => n))
            {
                names.Add(name);
            }
        }
        catch
        {
            // 蓝牙枚举不可用时保持空列表
        }

        return names;
    }

    private void OnConnectionStateChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(UpdateConnectionState);

    private void UpdateConnectionState()
    {
        var transport = _runtime.CurrentConfig.CommunicationType == CommunicationType.Ble
            ? LocalizationManager.Get("Option.CommBle")
            : LocalizationManager.Get("Option.CommCom");
        var stateText = _runtime.ConnectionState switch
        {
            CommunicationState.Connected => LocalizationManager.Get("ConnState.Connected"),
            CommunicationState.Reconnecting => LocalizationManager.Get("ConnState.Reconnecting"),
            _ => LocalizationManager.Get("ConnState.Waiting"),
        };

        ConnStateText.Text = $"{transport} · {stateText}";
        ConnStateText.Foreground = _runtime.ConnectionState == CommunicationState.Connected
            ? ThemeService.Brush("GpuBrush")
            : ThemeService.Brush("TextPrimaryBrush");
    }
}
