using System.IO;
using FanControl.Service.Logging;
using Microsoft.Extensions.Logging;

namespace FanControl.Tests;

public class FileLoggerTests
{
    private static string NewDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "fancontrol-log-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public void Provider_PrunesToMaxFiles()
    {
        var directory = NewDirectory();
        try
        {
            for (var i = 1; i <= 25; i++)
            {
                File.WriteAllText(Path.Combine(directory, $"fancontrol-{i:00000000}.log"), "old");
            }

            // 构造时即清理到最多 20 条
            using var provider = new FileLoggerProvider(directory);
            Assert.Equal(20, Directory.GetFiles(directory, "fancontrol-*.log").Length);

            // 写入新日志后仍不超过 20 条
            provider.CreateLogger("test").LogInformation("hello");
            Assert.Equal(20, Directory.GetFiles(directory, "fancontrol-*.log").Length);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Provider_Disabled_WritesNothing()
    {
        var directory = NewDirectory();
        try
        {
            using var provider = new FileLoggerProvider(directory);
            provider.Configure(directory, enabled: false, maxLogFiles: 20);

            provider.CreateLogger("test").LogInformation("should not be written");

            Assert.Empty(Directory.GetFiles(directory, "*.log"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
