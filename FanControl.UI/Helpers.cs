using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace FanControl.UI;

internal static class MemoryHelper
{
    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    /// <summary>裁剪进程工作集（G-Helper 同款做法），后台驻留时回收内存。</summary>
    public static void Trim()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            SetProcessWorkingSetSize(process.Handle, new IntPtr(-1), new IntPtr(-1));
        }
        catch
        {
            // 忽略裁剪失败
        }
    }
}

internal static class CrashLog
{
    public static void Write(string tag, Exception? ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FanControl",
                "Logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"fancontrol-crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {tag}{Environment.NewLine}{ex}{Environment.NewLine}");
        }
        catch
        {
            // 崩溃日志写入失败时不再尝试
        }
    }
}

/// <summary>温度显示单位换算。</summary>
internal static class TemperatureUnitHelper
{
    public static double ToDisplay(double celsius, FanControl.Shared.Enums.TemperatureUnit unit)
    {
        return unit == FanControl.Shared.Enums.TemperatureUnit.Fahrenheit
            ? celsius * 9.0 / 5.0 + 32.0
            : celsius;
    }

    public static string Suffix(FanControl.Shared.Enums.TemperatureUnit unit) =>
        unit == FanControl.Shared.Enums.TemperatureUnit.Fahrenheit ? "°F" : "°C";
}
