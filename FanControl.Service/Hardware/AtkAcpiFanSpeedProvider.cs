using System.Runtime.InteropServices;
using FanControl.Shared.Enums;

namespace FanControl.Service.Hardware;

/// <summary>
/// ATKACPI 风扇转速数据源（G-Helper AsusACPI.GetFan 同款）：
/// DSTS 读取 CPU_Fan/GPU_Fan/Mid_Fan 端点，返回值 &amp; 0xFFFF，超过 120 或异常视为不支持，
/// RPM = 值 × 100。华硕机型实测 CPU/GPU 端点可用（如 49 → 4900 RPM）。
/// </summary>
public sealed class AtkAcpiFanSpeedProvider : IFanSpeedProvider, IDisposable
{
    private const string DeviceName = @"\\.\ATKACPI";
    private const uint Dsts = 0x53545344;          // "DSTS"
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

    public FanSpeedSource Source => FanSpeedSource.AtkAcpi;

    public Task<double?> ReadAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => ReadFanRpm(CpuFan) ?? ReadFanRpm(GpuFan) ?? ReadFanRpm(MidFan),
            cancellationToken);
    }

    /// <summary>G-Helper GetFan 同款校验：raw &amp; 0xFFFF，>120 或（0 且 raw&lt;0）判为不支持。</summary>
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

            return BitConverter.ToInt32(outBuffer, 0) - 65536;
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
