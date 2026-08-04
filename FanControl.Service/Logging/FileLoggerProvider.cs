using Microsoft.Extensions.Logging;

namespace FanControl.Service.Logging;

/// <summary>
/// 文件日志：写入所选目录（用户数据目录或安装目录）\Logs\fancontrol-日期.log。
/// 支持运行时切换目录/开关/保留数量；日志文件最多保留 MaxLogFiles 个，超出自动清除最旧的。
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly object _sync = new();
    private string _directory;
    private bool _enabled = true;
    private int _maxLogFiles = 20;

    public FileLoggerProvider(string directory)
    {
        _directory = directory;
        Prune();
    }

    /// <summary>运行时更新日志设置（设置页/首启弹窗后立即生效）。</summary>
    public void Configure(string directory, bool enabled, int maxLogFiles)
    {
        lock (_sync)
        {
            _directory = directory;
            _enabled = enabled;
            _maxLogFiles = Math.Max(1, maxLogFiles);
            PruneLocked();
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(this, categoryName);
    }

    public void Dispose()
    {
    }

    internal void Write(string category, LogLevel level, string message)
    {
        lock (_sync)
        {
            if (!_enabled)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(_directory);
                var path = Path.Combine(_directory, $"fancontrol-{DateTime.Now:yyyyMMdd}.log");
                var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] [{category}] {message}{Environment.NewLine}";
                File.AppendAllText(path, line);
                PruneLocked();
            }
            catch
            {
                // 日志写入失败不影响主流程
            }
        }
    }

    private void Prune()
    {
        lock (_sync)
        {
            PruneLocked();
        }
    }

    /// <summary>仅保留最新 _maxLogFiles 个常规日志（fancontrol-yyyyMMdd.log，不含崩溃日志）。</summary>
    private void PruneLocked()
    {
        try
        {
            if (!Directory.Exists(_directory))
            {
                return;
            }

            var files = Directory.GetFiles(_directory, "fancontrol-*.log")
                .Where(f => !Path.GetFileName(f).Contains("-crash-", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f)
                .ToList();

            foreach (var oldFile in files.Skip(_maxLogFiles))
            {
                File.Delete(oldFile);
            }
        }
        catch
        {
            // 清理失败不影响主流程
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _owner;
        private readonly string _category;

        public FileLogger(FileLoggerProvider owner, string category)
        {
            _owner = owner;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Information;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (exception is not null)
            {
                message += $" {exception}";
            }

            _owner.Write(_category, logLevel, message);
        }
    }
}
