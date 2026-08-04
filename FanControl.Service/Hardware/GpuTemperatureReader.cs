using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FanControl.Service.Hardware;

/// <summary>
/// GPU 温度读取（WMI / ATKACPI 模式共用）：NVIDIA nvidia-smi → AMD ADL。
/// 支持按厂商（NVIDIA/AMD/Intel）或 GPU 名称关键词选择读哪块卡。
/// </summary>
internal static class GpuTemperatureReader
{
    public static double? Read(string? selection = null)
    {
        // 明确选择 AMD 时跳过 NVIDIA；否则尝试 nvidia-smi（可按名称逐卡匹配）
        if (IsAuto(selection) || !VendorOf(selection, "amd"))
        {
            var nvidia = ReadNvidiaSmiTemperature(selection);
            if (nvidia is not null)
            {
                return nvidia;
            }
        }

        // 指定了 NVIDIA/Intel 或指定具体名称但 nvidia-smi 未匹配到时，不再用 ADL 猜
        if (!IsAuto(selection) && !VendorOf(selection, "amd"))
        {
            return null;
        }

        return AmdAdl.ReadTemperature();
    }

    private static bool IsAuto(string? selection)
        => string.IsNullOrWhiteSpace(selection)
           || selection.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase);

    private static bool VendorOf(string? selection, string vendor)
    {
        if (string.IsNullOrWhiteSpace(selection))
        {
            return false;
        }

        var sel = selection.ToLowerInvariant();
        return vendor switch
        {
            "nvidia" => sel.Contains("nvidia"),
            "amd" => sel.Contains("amd") || sel.Contains("radeon"),
            "intel" => sel.Contains("intel"),
            _ => false,
        };
    }

    private static bool NameMatches(string gpuName, string? selection)
    {
        if (IsAuto(selection))
        {
            return true;
        }

        return gpuName.Contains(selection!.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static double? ReadNvidiaSmiTemperature(string? selection)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(
                    "nvidia-smi",
                    "--query-gpu=name,temperature.gpu --format=csv,noheader,nounits")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            if (!process.Start())
            {
                return null;
            }

            string? line;
            while ((line = process.StandardOutput.ReadLine()) is not null)
            {
                var comma = line.IndexOf(',');
                var name = comma >= 0 ? line[..comma].Trim() : string.Empty;
                var tempText = comma >= 0 ? line[(comma + 1)..].Trim() : line.Trim();

                if (NameMatches(name, selection)
                    && double.TryParse(tempText, out var temperature)
                    && temperature is >= 0 and <= 150)
                {
                    return temperature;
                }
            }

            if (!process.WaitForExit(2000))
            {
                process.Kill();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>AMD ADL 最小实现：仅读取 GPU 温度（atiadlxx.dll）。</summary>
    private static class AmdAdl
    {
        private const string Dll = "atiadlxx.dll";

        [DllImport(Dll)]
        private static extern int ADL_Main_Control_Create(IntPtr callback, int enumAdapters);

        [DllImport(Dll)]
        private static extern int ADL_Main_Control_Destroy();

        [DllImport(Dll)]
        private static extern int ADL_Adapter_NumberOfAdapters_Get(int enumAdapters, ref int numAdapters);

        [DllImport(Dll)]
        private static extern int ADL_Adapter_ID_Get(int adapterIndex, ref int adapterId);

        [StructLayout(LayoutKind.Sequential)]
        private struct ADLTemperature
        {
            public int Size;
            public int Temperature;
        }

        [DllImport(Dll)]
        private static extern int ADL_Overdrive5_Temperature_Get(
            int adapterId,
            int thermalControllerIndex,
            ref ADLTemperature temperature);

        public static double? ReadTemperature()
        {
            try
            {
                if (ADL_Main_Control_Create(IntPtr.Zero, 1) != 0)
                {
                    return null;
                }

                try
                {
                    var count = 0;
                    if (ADL_Adapter_NumberOfAdapters_Get(1, ref count) != 0 || count <= 0)
                    {
                        return null;
                    }

                    for (var i = 0; i < count; i++)
                    {
                        var adapterId = 0;
                        if (ADL_Adapter_ID_Get(i, ref adapterId) != 0)
                        {
                            continue;
                        }

                        var temperature = new ADLTemperature
                        {
                            Size = Marshal.SizeOf<ADLTemperature>(),
                        };
                        if (ADL_Overdrive5_Temperature_Get(adapterId, 0, ref temperature) == 0
                            && temperature.Temperature > 0)
                        {
                            return temperature.Temperature / 1000.0;
                        }
                    }
                }
                finally
                {
                    ADL_Main_Control_Destroy();
                }
            }
            catch
            {
                // ADL 不可用时返回 null
            }

            return null;
        }
    }
}
