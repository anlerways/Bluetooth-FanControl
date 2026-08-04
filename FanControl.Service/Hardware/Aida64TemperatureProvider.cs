using System.Globalization;
using System.Text.RegularExpressions;
using FanControl.Shared.Enums;
using Microsoft.Win32;

namespace FanControl.Service.Hardware;

/// <summary>
/// AIDA64 数据源：读取注册表 SensorValues（参考 Hardware-Monitor 项目）。
/// 需在 AIDA64 → 硬件检测工具 → 外部程序 中勾选"允许监控数据写入注册表"。
/// </summary>
public sealed class Aida64TemperatureProvider : ITemperatureProvider
{
    private const string RegistryPath = @"Software\FinalWire\AIDA64\SensorValues";

    public TemperatureSource Source => TemperatureSource.Aida64;

    public Task<TemperatureSnapshot> ReadAsync(
        CancellationToken cancellationToken = default,
        string? gpuSelection = null)
    {
        return Task.Run(
            () =>
            {
                // AIDA64 未开启写入或未运行时返回空值，不抛异常（由调用方按 NaN 处理）
                double? cpu = null;
                double? gpu = null;
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
                if (key is not null)
                {
                    cpu = ReadFirstValue(key, "Value.TC");
                    gpu = ReadFirstValue(key, "Value.TG");
                }

                return new TemperatureSnapshot(cpu, gpu, FanRpm: null);
            },
            cancellationToken);
    }

    private static double? ReadFirstValue(RegistryKey key, string prefix)
    {
        foreach (var name in key.GetValueNames())
        {
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var raw = key.GetValue(name)?.ToString();
            if (TryParseTemperature(raw, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryParseTemperature(string? raw, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        // 注册表值可能形如 "45°C" / "45" / "45.0"
        var cleaned = Regex.Replace(raw, "[^0-9.\\-]", string.Empty);
        if (!double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        return value is >= 0 and <= 150;
    }
}
