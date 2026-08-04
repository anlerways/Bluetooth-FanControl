using System.Text.Json;
using FanControl.Shared.Contracts;
using FanControl.Shared.Enums;
using FanControl.Shared.Models;
using Microsoft.Extensions.Logging;

namespace FanControl.Service.Config;

/// <summary>
/// JSON 配置管理器。
/// - system.json 固定存放于 %AppData%\FanControl\system.json（定位文件本身）；
/// - appconfig.json 按 SystemConfig.ConfigLocation 写入安装目录或用户数据目录；
/// - 写入采用临时文件 + 原子替换，损坏/缺失时回退默认值并记录日志。
/// </summary>
public sealed class ConfigManager : IConfigManager
{
    private readonly ILogger<ConfigManager> _logger;
    private readonly string _installDirectory;

    public ConfigManager(
        ILogger<ConfigManager> logger,
        string? systemConfigDirectory = null,
        string? installDirectory = null)
    {
        _logger = logger;
        _installDirectory = installDirectory ?? AppContext.BaseDirectory;

        if (systemConfigDirectory is null)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            SystemConfigFilePath = Path.Combine(appData, "FanControl", "system.json");
            DefaultUserDataDirectory = Path.Combine(appData, "FanControl", "data");
        }
        else
        {
            SystemConfigFilePath = Path.Combine(systemConfigDirectory, "system.json");
            DefaultUserDataDirectory = Path.Combine(systemConfigDirectory, "data");
        }
    }

    public string SystemConfigFilePath { get; }

    public string DefaultUserDataDirectory { get; }

    /// <summary>程序安装目录（appconfig.json / 日志可存放于此）。</summary>
    public string InstallDirectory => _installDirectory;

    /// <summary>按系统配置解析日志目录（用户数据目录或安装目录下的 Logs）。</summary>
    public string GetLogDirectory(SystemConfig systemConfig)
    {
        var directory = systemConfig.LogLocation switch
        {
            ConfigLocation.InstallDirectory => _installDirectory,
            _ => string.IsNullOrWhiteSpace(systemConfig.UserDataDirectory)
                ? DefaultUserDataDirectory
                : systemConfig.UserDataDirectory,
        };

        return Path.Combine(directory, "Logs");
    }

    public async Task<SystemConfig> LoadSystemConfigAsync(CancellationToken cancellationToken = default)
    {
        var result = await TryReadJsonAsync<SystemConfig>(SystemConfigFilePath, cancellationToken);
        if (result is null)
        {
            if (File.Exists(SystemConfigFilePath))
            {
                _logger.LogWarning(
                    "system.json 读取失败或损坏，使用默认系统配置。路径: {Path}",
                    SystemConfigFilePath);
            }

            return new SystemConfig();
        }

        return result;
    }

    public async Task SaveSystemConfigAsync(SystemConfig config, CancellationToken cancellationToken = default)
    {
        await WriteJsonAsync(SystemConfigFilePath, config, cancellationToken);
    }

    public async Task<AppConfig> LoadAppConfigAsync(CancellationToken cancellationToken = default)
    {
        var path = GetAppConfigFilePath(await LoadSystemConfigAsync(cancellationToken));
        var result = await TryReadJsonAsync<AppConfig>(path, cancellationToken);
        if (result is null)
        {
            if (File.Exists(path))
            {
                _logger.LogWarning(
                    "appconfig.json 读取失败或损坏，使用默认应用配置。路径: {Path}",
                    path);
            }

            return new AppConfig();
        }

        return result;
    }

    public async Task SaveAppConfigAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        var path = GetAppConfigFilePath(await LoadSystemConfigAsync(cancellationToken));
        await WriteJsonAsync(path, config, cancellationToken);
    }

    private string GetAppConfigFilePath(SystemConfig systemConfig)
    {
        var directory = systemConfig.ConfigLocation switch
        {
            ConfigLocation.InstallDirectory => _installDirectory,
            ConfigLocation.UserData => string.IsNullOrWhiteSpace(systemConfig.UserDataDirectory)
                ? DefaultUserDataDirectory
                : systemConfig.UserDataDirectory,
            _ => throw new ArgumentOutOfRangeException(
                nameof(systemConfig),
                systemConfig.ConfigLocation,
                "未知的配置位置模式。"),
        };

        return Path.Combine(directory, "appconfig.json");
    }

    private static async Task<T?> TryReadJsonAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonDefaults.Options, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
        where T : class
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + ".tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(value, JsonDefaults.Options),
            cancellationToken);

        File.Move(temporaryPath, path, overwrite: true);
    }
}
