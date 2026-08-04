using FanControl.Shared.Enums;

namespace FanControl.Shared.Models;

/// <summary>应用业务配置：数据源、控制模式、通信参数、曲线、主题。</summary>
public sealed record AppConfig
{
    public TemperatureSource TemperatureSource { get; init; } = TemperatureSource.LibreHardwareMonitor;

    // GPU 温度获取方式（默认 NVIDIA-SMI / ADL）：NvidiaSmiAdl / LibreHardwareMonitor / AtkAcpi / Aida64
    // null 兼容旧配置，运行时代视为 NvidiaSmiAdl
    public TemperatureSource? GpuTemperatureSource { get; init; } = TemperatureSource.NvidiaSmiAdl;

    // GPU 选择：Auto=自动（多卡取最高温）；或指定厂商/名称关键词（NVIDIA / AMD / Intel / 如 "RTX 4060"）
    public string GpuSelection { get; init; } = "Auto";

    // 风扇转速数据源（独立于温度数据源；华硕机型建议 ATKACPI，G-Helper 同款端点）
    public FanSpeedSource FanSpeedSource { get; init; } = FanSpeedSource.AtkAcpi;

    public FanControlMode FanControlMode { get; init; } = FanControlMode.CpuTemp;

    public CommunicationType CommunicationType { get; init; } = CommunicationType.Ble;

    public string ComPort { get; init; } = "COM3";

    public int ComBaudRate { get; init; } = 115200;

    public string BleDeviceName { get; init; } = string.Empty;

    // 温度采样轮询：0.1-5 秒
    public int PollIntervalMilliseconds { get; init; } = 1000;

    // 已连接时的发送轮询：1-30 秒
    public int BlePollIntervalSeconds { get; init; } = 2;

    // 断开后的重连轮询：1-60 秒
    public int BleReconnectIntervalSeconds { get; init; } = 5;

    // 自动重连开关（COM 与 BLE 共用）
    public bool AutoReconnectBle { get; init; } = true;

    // 风扇平滑系数：0.1-1.0（1.0 = 无平滑），抑制温度剧烈波动导致的转速抖动
    public double PwmSmoothing { get; init; } = 0.5;

    // 通知开关
    public bool NotifyOnBleDisconnect { get; init; } = true;

    public bool NotifyOnTemperatureError { get; init; } = true;

    // 界面语言（zh-CN / en-US）
    public string Language { get; init; } = "zh-CN";

    // 温度显示单位
    public TemperatureUnit TemperatureUnit { get; init; } = TemperatureUnit.Celsius;

    public ThemeType Theme { get; init; } = ThemeType.System;

    public double ManualPwmPercent { get; init; } = 50;

    // 风扇转速平滑开关：关闭时（含手动模式）PWM 直接取目标值，不做平滑
    public bool SmoothingEnabled { get; init; } = true;

    public IReadOnlyList<CurvePoint> Curve { get; init; } =
        new List<CurvePoint>
        {
            new(30, 20),
            new(50, 35),
            new(70, 60),
            new(90, 100),
        };

    // 转速-PWM 曲线（X = RPM 0-10000，Y = PWM%）
    public IReadOnlyList<RpmCurvePoint> RpmCurve { get; init; } =
        new List<RpmCurvePoint>
        {
            new(0, 0),
            new(2000, 25),
            new(4000, 45),
            new(6000, 65),
            new(8000, 85),
            new(10000, 100),
        };
}
