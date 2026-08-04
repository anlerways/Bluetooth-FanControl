using System.Management;
using FanControl.Shared.Enums;

namespace FanControl.Service.Hardware;

/// <summary>
/// WMI 风扇转速数据源：查询 Win32_Fan.DesiredSpeed。
/// 大多数笔记本此字段为空（EC 不暴露），作为可选兜底方案。
/// </summary>
public sealed class WmiFanSpeedProvider : IFanSpeedProvider
{
    public FanSpeedSource Source => FanSpeedSource.Wmi;

    public Task<double?> ReadAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                using var searcher = new ManagementObjectSearcher(
                    "root\\CIMV2",
                    "SELECT * FROM Win32_Fan");
                using var collection = searcher.Get();

                double? result = null;
                foreach (ManagementBaseObject item in collection)
                {
                    var rawObject = item["DesiredSpeed"];
                    if (rawObject is null || rawObject is DBNull)
                    {
                        continue;
                    }

                    var raw = Convert.ToDouble(rawObject);
                    if (raw is >= 0 and <= 30000)
                    {
                        result = raw;
                        break;
                    }
                }

                return result;
            },
            cancellationToken);
    }
}
