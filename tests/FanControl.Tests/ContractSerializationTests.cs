using System.Text.Json;
using FanControl.Shared.Contracts;
using FanControl.Shared.Enums;
using FanControl.Shared.Models;

namespace FanControl.Tests;

public class ContractSerializationTests
{
    private static readonly JsonSerializerOptions Options = JsonDefaults.Options;

    [Fact]
    public void AppConfig_JsonRoundTrip_PreservesValues()
    {
        var config = new AppConfig
        {
            TemperatureSource = TemperatureSource.LibreHardwareMonitor,
            FanSpeedSource = FanSpeedSource.AtkAcpi,
            FanControlMode = FanControlMode.Mixed,
            CommunicationType = CommunicationType.Ble,
            BleDeviceName = "ESP32-Fan",
            Theme = ThemeType.Dark,
            Curve = new List<CurvePoint> { new(25, 10), new(80, 90) },
            RpmCurve = new List<RpmCurvePoint> { new(1000, 10), new(5000, 50) },
            SmoothingEnabled = false,
        };

        var restored = JsonSerializer.Deserialize<AppConfig>(
            JsonSerializer.Serialize(config, Options),
            Options);

        Assert.NotNull(restored);
        Assert.Equal(config.TemperatureSource, restored.TemperatureSource);
        Assert.Equal(config.FanSpeedSource, restored.FanSpeedSource);
        Assert.Equal(config.FanControlMode, restored.FanControlMode);
        Assert.Equal(config.CommunicationType, restored.CommunicationType);
        Assert.Equal(config.BleDeviceName, restored.BleDeviceName);
        Assert.Equal(config.Theme, restored.Theme);
        Assert.True(config.Curve.SequenceEqual(restored.Curve));
        Assert.True(config.RpmCurve.SequenceEqual(restored.RpmCurve));
        Assert.Equal(config.SmoothingEnabled, restored.SmoothingEnabled);
    }

    [Fact]
    public void AppConfig_Defaults_UseSafeFallbacks()
    {
        var config = new AppConfig();

        Assert.Equal(TemperatureSource.LibreHardwareMonitor, config.TemperatureSource);
        Assert.Equal(FanSpeedSource.AtkAcpi, config.FanSpeedSource);
        Assert.Equal(FanControlMode.CpuTemp, config.FanControlMode);
        Assert.Equal(CommunicationType.Ble, config.CommunicationType);
        Assert.Equal("COM3", config.ComPort);
        Assert.Equal(115200, config.ComBaudRate);
        Assert.Equal(1000, config.PollIntervalMilliseconds);
        Assert.Equal(2, config.BlePollIntervalSeconds);
        Assert.Equal(5, config.BleReconnectIntervalSeconds);
        Assert.True(config.AutoReconnectBle);
        Assert.Equal(0.5, config.PwmSmoothing);
        Assert.True(config.SmoothingEnabled);
        Assert.True(config.NotifyOnBleDisconnect);
        Assert.True(config.NotifyOnTemperatureError);
        Assert.Equal("zh-CN", config.Language);
        Assert.Equal(TemperatureUnit.Celsius, config.TemperatureUnit);
        Assert.True(config.Curve.Count >= 2);
    }

    [Fact]
    public void SystemConfig_JsonRoundTrip_PreservesValues()
    {
        var config = new SystemConfig
        {
            ConfigLocation = ConfigLocation.InstallDirectory,
            UserDataDirectory = @"D:\FanControl\data",
        };

        var restored = JsonSerializer.Deserialize<SystemConfig>(
            JsonSerializer.Serialize(config, Options),
            Options);

        Assert.NotNull(restored);
        Assert.Equal(config, restored);
    }

    [Fact]
    public void IpcMessage_JsonRoundTrip_PreservesValues()
    {
        var message = new IpcMessage(IpcCommandType.SetCurve, "req-1", "[]");

        var restored = JsonSerializer.Deserialize<IpcMessage>(
            JsonSerializer.Serialize(message, Options),
            Options);

        Assert.NotNull(restored);
        Assert.Equal(IpcCommandType.SetCurve, restored.Command);
        Assert.Equal("req-1", restored.RequestId);
        Assert.Equal("[]", restored.PayloadJson);
    }

    [Fact]
    public void IpcResponse_Factories_ProduceExpectedEnvelope()
    {
        var ok = IpcResponse.Ok("r1", "{}");
        var fail = IpcResponse.Fail("r2", "boom");

        Assert.True(ok.Success);
        Assert.Null(ok.Error);
        Assert.Equal("r1", ok.RequestId);
        Assert.False(fail.Success);
        Assert.Equal("boom", fail.Error);
    }

    [Fact]
    public void IpcCommandType_Values_AreStable()
    {
        Assert.Equal(0, (int)IpcCommandType.Ping);
        Assert.Equal(1, (int)IpcCommandType.GetConfig);
        Assert.Equal(2, (int)IpcCommandType.SetConfig);
        Assert.Equal(3, (int)IpcCommandType.SetMode);
        Assert.Equal(4, (int)IpcCommandType.SetCurve);
        Assert.Equal(5, (int)IpcCommandType.SetCommunicationType);
        Assert.Equal(6, (int)IpcCommandType.GetSnapshot);
        Assert.Equal(7, (int)IpcCommandType.Restart);
        Assert.Equal(8, (int)IpcCommandType.Shutdown);
    }
}
