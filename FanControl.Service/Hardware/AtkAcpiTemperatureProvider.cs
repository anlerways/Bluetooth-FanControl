using System.Diagnostics;
using System.Runtime.InteropServices;
using FanControl.Shared.Enums;

namespace FanControl.Service.Hardware;

/// <summary>
/// 华硕 ATKACPI 数据源（参考 G-Helper 的 AsusACPI）：
/// 通过 DeviceIoControl 调用 DSTS 方法，参数为温度设备号（Temp_CPU/Temp_GPU），
/// 返回值减 0x10000 即摄氏度；GPU 设备号在多数机型不可用，回退 nvidia-smi / AMD ADL；
/// CPU 读取失败时回退系统热区性能计数器（与 G-Helper 相同链路）。
/// </summary>
public sealed class AtkAcpiTemperatureProvider : ITemperatureProvider, IDisposable
{
    private const string DeviceName = @"\\.\ATKACPI";
    private const uint Dsts = 0x53545344;          // "DSTS"
    private const uint TempCpu = 0x00120094;
    private const uint TempGpu = 0x00120097;
    // 风扇转速端点（G-Helper AsusACPI 同款）：DSTS 返回值 × 100 = RPM
    private const uint CpuFan = 0x00110013;
    private const uint GpuFan = 0x00110014;
    private const uint MidFan = 0x00110031;
    private const uint IoControlCode = 0x0022240C;

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 1;
    private const uint FileShareWrite = 2;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x80;

    private readonly object _sync = new();
    private IntPtr _handle = IntPtr.Zero;

    public TemperatureSource Source => TemperatureSource.AtkAcpi;

    public Task<TemperatureSnapshot> ReadAsync(
        CancellationToken cancellationToken = default,
        string? gpuSelection = null)
    {
        return Task.Run(
            () =>
            {
                var cpu = ReadTemperature(TempCpu);
                if (cpu is null)
                {
                    // G-Helper 同款回退：ATK 读不到时使用系统热区性能计数器
                    cpu = ReadCpuFromPerformanceCounter();
                }

                // GPU 链路与 G-Helper 一致：厂商接口（nvidia-smi / AMD ADL）优先，
                // ATKACPI Temp_GPU 兜底（多数华硕机型不支持该设备号，本机实测不可用）
                var gpu = GpuTemperatureReader.Read(gpuSelection) ?? ReadTemperature(TempGpu);

                // 风扇转速：CPU → GPU → Mid 逐级回退（G-Helper GetFan 同款校验）
                var fanRpm = ReadFanRpm(CpuFan)
                    ?? ReadFanRpm(GpuFan)
                    ?? ReadFanRpm(MidFan);

                // 温度读不到时置空，不抛异常（由调用方按 NaN 处理 / 触发自动回退）
                return new TemperatureSnapshot(cpu, gpu, fanRpm);
            },
            cancellationToken);
    }

    /// <summary>G-Helper GetFan 同款：DSTS 原始值 &amp; 0xFFFF，超过 120 或异常视为不支持。</summary>
    private double? ReadFanRpm(uint deviceId)
    {
        var raw = ReadDeviceRaw(deviceId);
        if (raw is null)
        {
            return null;
        }

        var fan = raw.Value & 0xFFFF;
        if (fan > 120 || (fan == 0 && raw < 0))
        {
            return null;
        }

        return fan * 100.0;
    }

    private double? ReadTemperature(uint deviceId)
    {
        var raw = ReadDeviceRaw(deviceId);
        return raw is >= 0 and <= 150 ? raw : null;
    }

    private int? ReadDeviceRaw(uint deviceId)
    {
        try
        {
            var handle = GetHandle();
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            var inBuffer = new byte[16];
            BitConverter.GetBytes(Dsts).CopyTo(inBuffer, 0);
            BitConverter.GetBytes(8u).CopyTo(inBuffer, 4);
            BitConverter.GetBytes(deviceId).CopyTo(inBuffer, 8);

            var outBuffer = new byte[16];
            uint returned = 0;

            if (!DeviceIoControl(
                    handle,
                    IoControlCode,
                    inBuffer,
                    (uint)inBuffer.Length,
                    outBuffer,
                    (uint)outBuffer.Length,
                    ref returned,
                    IntPtr.Zero))
            {
                return null;
            }

            var value = BitConverter.ToInt32(outBuffer, 0) - 65536;
            return value;
        }
        catch
        {
            return null;
        }
    }

    private IntPtr GetHandle()
    {
        lock (_sync)
        {
            if (_handle == IntPtr.Zero)
            {
                _handle = CreateFile(
                    DeviceName,
                    GenericRead | GenericWrite,
                    FileShareRead | FileShareWrite,
                    IntPtr.Zero,
                    OpenExisting,
                    FileAttributeNormal,
                    IntPtr.Zero);
            }

            return _handle == new IntPtr(-1) ? IntPtr.Zero : _handle;
        }
    }

    private static double? ReadCpuFromPerformanceCounter()
    {
        try
        {
            using var counter = new PerformanceCounter(
                "Thermal Zone Information",
                "Temperature",
                @"\_TZ.THRM",
                true);
            var value = counter.NextValue();
            return value > 0 && value < 500 ? value - 273.15 : null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_handle != IntPtr.Zero && _handle != new IntPtr(-1))
            {
                CloseHandle(_handle);
            }

            _handle = IntPtr.Zero;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr device,
        uint ioControlCode,
        byte[] inBuffer,
        uint inBufferSize,
        byte[] outBuffer,
        uint outBufferSize,
        ref uint bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
