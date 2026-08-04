using FanControl.Shared.Enums;

namespace FanControl.Shared.Models;

/// <summary>
/// 系统级配置：配置存放位置（首次启动引导选择，结果保存于 %AppData%\FanControl\system.json）。
/// </summary>
public sealed record SystemConfig
{
    public ConfigLocation ConfigLocation { get; init; } = ConfigLocation.UserData;

    public string? UserDataDirectory { get; init; }

    // 日志文件存放位置（与配置位置独立选择，首次启动弹窗引导，之后可在设置中修改）
    public ConfigLocation LogLocation { get; init; } = ConfigLocation.UserData;

    // 日志开关
    public bool LogEnabled { get; init; } = true;

    // 日志文件最大保留数（超出自动清除最旧的，防止堆积）
    public int MaxLogFiles { get; init; } = 20;
}
