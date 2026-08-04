using FanControl.Service.Config;
using FanControl.Shared.Enums;
using FanControl.Shared.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace FanControl.Tests;

public class ConfigManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "FanControl.Tests." + Guid.NewGuid().ToString("N"));

    private ConfigManager CreateManager(string? installDirectory = null) =>
        new(
            NullLogger<ConfigManager>.Instance,
            _root,
            installDirectory ?? Path.Combine(_root, "install"));

    [Fact]
    public async Task LoadSystemConfig_MissingFile_ReturnsDefaults()
    {
        var manager = CreateManager();

        var config = await manager.LoadSystemConfigAsync();

        Assert.Equal(ConfigLocation.UserData, config.ConfigLocation);
    }

    [Fact]
    public async Task SaveThenLoadSystemConfig_RoundTrip()
    {
        var manager = CreateManager();
        var expected = new SystemConfig
        {
            ConfigLocation = ConfigLocation.InstallDirectory,
            UserDataDirectory = @"D:\some\dir",
        };

        await manager.SaveSystemConfigAsync(expected);
        var actual = await manager.LoadSystemConfigAsync();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GetLogDirectory_RespectsLocation()
    {
        var manager = CreateManager(Path.Combine(_root, "install"));

        Assert.Equal(
            Path.Combine(_root, "install", "Logs"),
            manager.GetLogDirectory(new SystemConfig
            {
                LogLocation = ConfigLocation.InstallDirectory,
            }));
        Assert.Equal(
            Path.Combine(_root, "user-data", "Logs"),
            manager.GetLogDirectory(new SystemConfig
            {
                LogLocation = ConfigLocation.UserData,
                UserDataDirectory = Path.Combine(_root, "user-data"),
            }));
        Assert.Equal(
            Path.Combine(_root, "data", "Logs"),
            manager.GetLogDirectory(new SystemConfig
            {
                LogLocation = ConfigLocation.UserData,
            }));
    }

    [Fact]
    public async Task LoadAppConfig_MissingFile_ReturnsDefaults()
    {
        var manager = CreateManager();

        var config = await manager.LoadAppConfigAsync();

        Assert.Equal(TemperatureSource.LibreHardwareMonitor, config.TemperatureSource);
        Assert.Equal(FanControlMode.CpuTemp, config.FanControlMode);
        Assert.Equal("COM3", config.ComPort);
    }

    [Fact]
    public async Task SaveThenLoadAppConfig_UserDataRoundTrip()
    {
        var manager = CreateManager();
        await manager.SaveSystemConfigAsync(new SystemConfig
        {
            ConfigLocation = ConfigLocation.UserData,
            UserDataDirectory = Path.Combine(_root, "user-data"),
        });

        var expected = new AppConfig
        {
            TemperatureSource = TemperatureSource.AtkAcpi,
            FanControlMode = FanControlMode.Mixed,
            CommunicationType = CommunicationType.Ble,
            BleDeviceName = "ESP32-Fan",
            Theme = ThemeType.Dark,
        };
        await manager.SaveAppConfigAsync(expected);

        var actual = await manager.LoadAppConfigAsync();

        Assert.Equal(expected.TemperatureSource, actual.TemperatureSource);
        Assert.Equal(expected.FanControlMode, actual.FanControlMode);
        Assert.Equal(expected.CommunicationType, actual.CommunicationType);
        Assert.Equal(expected.BleDeviceName, actual.BleDeviceName);
        Assert.Equal(expected.Theme, actual.Theme);
    }

    [Fact]
    public async Task SaveThenLoadAppConfig_InstallDirectoryMode()
    {
        var installDir = Path.Combine(_root, "install");
        var manager = CreateManager(installDir);
        await manager.SaveSystemConfigAsync(new SystemConfig
        {
            ConfigLocation = ConfigLocation.InstallDirectory,
        });

        var expected = new AppConfig { FanControlMode = FanControlMode.SystemFan };
        await manager.SaveAppConfigAsync(expected);

        Assert.True(File.Exists(Path.Combine(installDir, "appconfig.json")));
        Assert.Equal(
            FanControlMode.SystemFan,
            (await manager.LoadAppConfigAsync()).FanControlMode);
    }

    [Fact]
    public async Task CorruptJson_ReturnsDefaults()
    {
        var manager = CreateManager();
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "system.json"), "{ not json !!");
        Directory.CreateDirectory(Path.Combine(_root, "data"));
        await File.WriteAllTextAsync(Path.Combine(_root, "data", "appconfig.json"), "###");

        var system = await manager.LoadSystemConfigAsync();
        var app = await manager.LoadAppConfigAsync();

        Assert.Equal(ConfigLocation.UserData, system.ConfigLocation);
        Assert.Equal(TemperatureSource.LibreHardwareMonitor, app.TemperatureSource);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
