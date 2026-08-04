using FanControl.Service.Config;
using FanControl.Shared.Models;

namespace FanControl.Service.Host;

/// <summary>运行时共享状态：当前配置 + 最新数据包（监控循环与 IPC 命令共享）。</summary>
public sealed class AppState
{
    private readonly IConfigManager _configManager;

    public AppState(IConfigManager configManager)
    {
        _configManager = configManager;
    }

    public AppConfig Current { get; private set; } = new();

    public DataPacket? LatestPacket { get; private set; }

    /// <summary>每次成功发布新数据包时触发（监控循环线程）。</summary>
    public event EventHandler? DataPublished;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Current = await _configManager.LoadAppConfigAsync(cancellationToken);
    }

    public async Task ApplyAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        Current = config;
        await _configManager.SaveAppConfigAsync(config, cancellationToken);
    }

    public void Publish(DataPacket packet)
    {
        LatestPacket = packet;
        DataPublished?.Invoke(this, EventArgs.Empty);
    }
}
