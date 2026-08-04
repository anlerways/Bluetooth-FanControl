using LibreHardwareMonitor.Hardware;

namespace FanControl.Service.Hardware;

/// <summary>
/// LibreHardwareMonitor 单例宿主：温度/转速两个 Provider 共用同一个 Computer 实例，
/// 避免重复枚举硬件（LHM 同一实例重复 Open 会冲突，多实例成本也高）。
/// </summary>
internal static class LhmComputerHost
{
    private static readonly object Sync = new();
    private static Computer? _computer;

    public static Computer Get()
    {
        lock (Sync)
        {
            if (_computer is null)
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    // 风扇转速通常挂在主板/嵌入式控制器上，需要开启
                    IsMotherboardEnabled = true,
                    IsControllerEnabled = true,
                    IsStorageEnabled = false,
                    IsNetworkEnabled = false,
                };
                _computer.Open();
            }

            return _computer;
        }
    }

    public static void Close()
    {
        lock (Sync)
        {
            _computer?.Close();
            _computer = null;
        }
    }
}
