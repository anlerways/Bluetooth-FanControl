using FanControl.Shared.Enums;
using LibreHardwareMonitor.Hardware;

namespace FanControl.Service.Hardware;

/// <summary>
/// LibreHardwareMonitor 数据源：读取 CPU/GPU 传感器温度（与 G-Helper 同级的真实硬件读取）。
/// </summary>
public sealed class LibreHardwareMonitorTemperatureProvider : ITemperatureProvider, IDisposable
{
    public TemperatureSource Source => TemperatureSource.LibreHardwareMonitor;

    public Task<TemperatureSnapshot> ReadAsync(
        CancellationToken cancellationToken = default,
        string? gpuSelection = null)
    {
        return Task.Run(
            () =>
            {
                var computer = LhmComputerHost.Get();
                double? cpu = null;
                double? gpu = null;
                double? rpm = null;
                var hardwareCount = 0;

                foreach (var hardware in computer.Hardware)
                {
                    hardwareCount++;
                    hardware.Update();
                    Collect(hardware, ref cpu, ref gpu, ref rpm, gpuSelection);

                    foreach (var subHardware in hardware.SubHardware)
                    {
                        subHardware.Update();
                        Collect(subHardware, ref cpu, ref gpu, ref rpm, gpuSelection);
                    }
                }

                // 找不到传感器时对应值置空，不抛异常（由调用方按 NaN 处理）
                return new TemperatureSnapshot(cpu, gpu, rpm);
            },
            cancellationToken);
    }

    private static void Collect(
        IHardware hardware,
        ref double? cpu,
        ref double? gpu,
        ref double? rpm,
        string? gpuSelection)
    {
        double? cpuPackage = null;
        double? cpuMax = null;
        double? gpuMax = null;
        double? rpmMax = null;

        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType == SensorType.Fan && sensor.Value is float fanRaw)
            {
                rpmMax = Math.Max(rpmMax ?? double.MinValue, fanRaw);
                continue;
            }

            if (sensor.SensorType != SensorType.Temperature || sensor.Value is not float rawValue)
            {
                continue;
            }

            var value = (double)rawValue;

            switch (hardware.HardwareType)
            {
                case HardwareType.Cpu:
                    cpuMax = Math.Max(cpuMax ?? double.MinValue, value);

                    // 参考 Hardware-Monitor：Intel 12-14 代优先读 "CPU Package"，AMD 读 Tctl/Tdie
                    if (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)
                        || sensor.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase)
                        || sensor.Name.Contains("T die", StringComparison.OrdinalIgnoreCase))
                    {
                        cpuPackage = Math.Max(cpuPackage ?? double.MinValue, value);
                    }

                    break;
                case HardwareType.GpuNvidia:
                case HardwareType.GpuAmd:
                case HardwareType.GpuIntel:
                    if (GpuMatches(hardware, gpuSelection))
                    {
                        gpuMax = Math.Max(gpuMax ?? double.MinValue, value);
                    }

                    break;
            }
        }

        if (cpuPackage is not null)
        {
            cpu = Math.Max(cpu ?? double.MinValue, cpuPackage.Value);
        }
        else if (cpuMax is not null)
        {
            cpu = Math.Max(cpu ?? double.MinValue, cpuMax.Value);
        }

        if (gpuMax is not null)
        {
            gpu = Math.Max(gpu ?? double.MinValue, gpuMax.Value);
        }

        if (rpmMax is not null)
        {
            rpm = Math.Max(rpm ?? double.MinValue, rpmMax.Value);
        }
    }

    /// <summary>
    /// 按 GPU 选择过滤：Auto/空=所有 GPU 都算；否则按硬件名称包含选择词，
    /// 或按厂商关键词（NVIDIA/AMD/Intel）匹配硬件类型。
    /// </summary>
    private static bool GpuMatches(IHardware hardware, string? selection)
    {
        if (string.IsNullOrWhiteSpace(selection)
            || selection.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (hardware.Name.Contains(selection.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var sel = selection.ToLowerInvariant();
        return (sel.Contains("nvidia") && hardware.HardwareType == HardwareType.GpuNvidia)
            || (sel.Contains("amd") && hardware.HardwareType == HardwareType.GpuAmd)
            || (sel.Contains("intel") && hardware.HardwareType == HardwareType.GpuIntel);
    }

    public void Dispose()
    {
        LhmComputerHost.Close();
    }
}
