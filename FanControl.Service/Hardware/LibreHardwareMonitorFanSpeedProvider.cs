using FanControl.Shared.Enums;
using LibreHardwareMonitor.Hardware;

namespace FanControl.Service.Hardware;

/// <summary>
/// LibreHardwareMonitor 风扇转速数据源：枚举 SensorType.Fan 传感器，取最大值。
/// 与温度 Provider 共用同一 LHM 实例（LhmComputerHost）。
/// </summary>
public sealed class LibreHardwareMonitorFanSpeedProvider : IFanSpeedProvider
{
    public FanSpeedSource Source => FanSpeedSource.LibreHardwareMonitor;

    public Task<double?> ReadAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                var computer = LhmComputerHost.Get();
                double? rpm = null;

                foreach (var hardware in computer.Hardware)
                {
                    hardware.Update();
                    rpm = Merge(rpm, CollectFan(hardware));

                    foreach (var subHardware in hardware.SubHardware)
                    {
                        subHardware.Update();
                        rpm = Merge(rpm, CollectFan(subHardware));
                    }
                }

                return rpm;
            },
            cancellationToken);
    }

    private static double? CollectFan(IHardware hardware)
    {
        double? rpmMax = null;

        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType == SensorType.Fan && sensor.Value is float fanRaw)
            {
                rpmMax = Math.Max(rpmMax ?? double.MinValue, fanRaw);
            }
        }

        return rpmMax;
    }

    private static double? Merge(double? current, double? candidate)
    {
        if (candidate is null)
        {
            return current;
        }

        return Math.Max(current ?? double.MinValue, candidate.Value);
    }
}
