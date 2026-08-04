using System.Globalization;
using System.Text.RegularExpressions;
using FanControl.Shared.Enums;
using Microsoft.Win32;

namespace FanControl.Service.Hardware;

/// <summary>
/// AIDA64 风扇转速数据源：读取注册表 SensorValues 中 Value.FAN* 键（值形如 "4900 RPM"）。
/// 需在 AIDA64 → 硬件检测工具 → 外部程序 中勾选"允许监控数据写入注册表"。
/// </summary>
public sealed class Aida64FanSpeedProvider : IFanSpeedProvider
{
    private const string RegistryPath = @"Software\FinalWire\AIDA64\SensorValues";

    public FanSpeedSource Source => FanSpeedSource.Aida64;

    public Task<double?> ReadAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
                if (key is null)
                {
                    return null;
                }

                double? best = null;
                foreach (var name in key.GetValueNames())
                {
                    if (!name.StartsWith("Value.FAN", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("PWM", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var raw = key.GetValue(name)?.ToString();
                    if (TryParseRpm(raw, out var rpm))
                    {
                        best = Math.Max(best ?? double.MinValue, rpm);
                    }
                }

                return best;
            },
            cancellationToken);
    }

    private static bool TryParseRpm(string? raw, out double rpm)
    {
        rpm = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        // 注册表值可能形如 "4900 RPM" / "4900" / "4900.0"
        var cleaned = Regex.Replace(raw, "[^0-9.]", string.Empty);
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out rpm)
            && rpm is >= 0 and <= 30000;
    }
}
