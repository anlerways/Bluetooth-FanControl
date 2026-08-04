using System.Text.Json;
using FanControl.Service.Host;
using FanControl.Shared.Contracts;
using FanControl.Shared.Enums;
using FanControl.Shared.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FanControl.Service.Ipc;

/// <summary>IPC 命令处理器：更新 AppState、持久化配置并返回响应。</summary>
public sealed class IpcCommandHandler
{
    private readonly AppState _appState;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<IpcCommandHandler> _logger;

    public IpcCommandHandler(
        AppState appState,
        IHostApplicationLifetime lifetime,
        ILogger<IpcCommandHandler> logger)
    {
        _appState = appState;
        _lifetime = lifetime;
        _logger = logger;
    }

    public async Task<IpcResponse> HandleAsync(
        IpcMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            return message.Command switch
            {
                IpcCommandType.Ping => IpcResponse.Ok(message.RequestId, "\"pong\""),

                IpcCommandType.GetConfig => IpcResponse.Ok(
                    message.RequestId,
                    JsonSerializer.Serialize(_appState.Current, JsonDefaults.Options)),

                IpcCommandType.SetConfig => await SetConfigAsync(message, cancellationToken),
                IpcCommandType.SetMode => await SetModeAsync(message, cancellationToken),
                IpcCommandType.SetCurve => await SetCurveAsync(message, cancellationToken),
                IpcCommandType.SetCommunicationType => await SetCommunicationTypeAsync(message, cancellationToken),

                IpcCommandType.GetSnapshot => _appState.LatestPacket is null
                    ? IpcResponse.Fail(message.RequestId, "暂无数据快照。")
                    : IpcResponse.Ok(
                        message.RequestId,
                        JsonSerializer.Serialize(_appState.LatestPacket, JsonDefaults.Options)),

                IpcCommandType.Restart or IpcCommandType.Shutdown => HandleShutdown(message),

                _ => IpcResponse.Fail(message.RequestId, $"未支持的命令：{message.Command}"),
            };
        }
        catch (JsonException ex)
        {
            return IpcResponse.Fail(message.RequestId, $"载荷 JSON 无效：{ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IPC 命令处理失败：{Command}", message.Command);
            return IpcResponse.Fail(message.RequestId, ex.Message);
        }
    }

    private async Task<IpcResponse> SetConfigAsync(IpcMessage message, CancellationToken cancellationToken)
    {
        var config = DeserializePayload<AppConfig>(message);
        await _appState.ApplyAsync(config, cancellationToken);
        return IpcResponse.Ok(message.RequestId);
    }

    private async Task<IpcResponse> SetModeAsync(IpcMessage message, CancellationToken cancellationToken)
    {
        var mode = DeserializePayload<FanControlMode>(message);
        await _appState.ApplyAsync(_appState.Current with { FanControlMode = mode }, cancellationToken);
        return IpcResponse.Ok(message.RequestId);
    }

    private async Task<IpcResponse> SetCurveAsync(IpcMessage message, CancellationToken cancellationToken)
    {
        var curve = DeserializePayload<List<CurvePoint>>(message);
        await _appState.ApplyAsync(_appState.Current with { Curve = curve }, cancellationToken);
        return IpcResponse.Ok(message.RequestId);
    }

    private async Task<IpcResponse> SetCommunicationTypeAsync(
        IpcMessage message,
        CancellationToken cancellationToken)
    {
        var type = DeserializePayload<CommunicationType>(message);
        await _appState.ApplyAsync(_appState.Current with { CommunicationType = type }, cancellationToken);
        return IpcResponse.Ok(message.RequestId);
    }

    private IpcResponse HandleShutdown(IpcMessage message)
    {
        _logger.LogInformation("收到 {Command}，服务将停止（外部托管负责重启）。", message.Command);
        _lifetime.StopApplication();
        return IpcResponse.Ok(message.RequestId);
    }

    private static T DeserializePayload<T>(IpcMessage message) =>
        JsonSerializer.Deserialize<T>(message.PayloadJson ?? "null", JsonDefaults.Options)
        ?? throw new InvalidOperationException("载荷不能为空。");
}
