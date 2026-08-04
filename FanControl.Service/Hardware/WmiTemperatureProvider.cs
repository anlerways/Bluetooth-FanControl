using System.Management;
using FanControl.Shared.Enums;

namespace FanControl.Service.Hardware;

/// <summary>
/// 原生 WMI 温度数据源：查询 MSAcpi_ThermalZoneTemperature（主板 ACPI 热区）。
/// 通常只能得到 CPU/机箱热区近似温度，GPU 温度不可用。
/// </summary>
public sealed class WmiTemperatureProvider : ITemperatureProvider
{
    public TemperatureSource Source => TemperatureSource.Wmi;

    public Task<TemperatureSnapshot> ReadAsync(
        CancellationToken cancellationToken = default,
        string? gpuSelection = null)
    {
        return Task.Run(
            () =>
            {
                var readings = QueryThermalZone();
                if (readings.Count == 0)
                {
                    readings = QueryTemperatureProbe();
                }

                // 无热区数据时 CPU 置空，不抛异常（由调用方按 NaN 处理）
                double? cpu = readings.Count > 0 ? readings.Max() : null;

                // 尝试通过 NVIDIA（nvidia-smi）/ AMD（ADL）接口补充 GPU 温度
                var gpu = GpuTemperatureReader.Read(gpuSelection);
                return new TemperatureSnapshot(cpu, gpu, FanRpm: null);
            },
            cancellationToken);
    }

    private static List<double> QueryThermalZone()
    {
        var readings = new List<double>();

        using var searcher = new ManagementObjectSearcher(
            "root\\WMI",
            "SELECT * FROM MSAcpi_ThermalZoneTemperature");
        using var collection = searcher.Get();

        foreach (ManagementBaseObject item in collection)
        {
            // CurrentTemperature 单位为 0.1 开尔文
            var raw = Convert.ToUInt32(item["CurrentTemperature"]);
            AddIfPlausible(readings, raw / 10.0 - 273.15);
        }

        return readings;
    }

    private static List<double> QueryTemperatureProbe()
    {
        var readings = new List<double>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                "SELECT * FROM Win32_TemperatureProbe");
            using var collection = searcher.Get();

            foreach (ManagementBaseObject item in collection)
            {
                var raw = Convert.ToDouble(item["CurrentReading"]);
                AddIfPlausible(readings, raw / 10.0 - 273.15);
            }
        }
        catch
        {
            // 部分系统不支持 Win32_TemperatureProbe
        }

        return readings;
    }

    private static void AddIfPlausible(List<double> readings, double celsius)
    {
        if (celsius is >= 0 and <= 150)
        {
            readings.Add(celsius);
        }
    }
}
