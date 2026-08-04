using System.IO.Pipes;
using System.Text.Json;
using FanControl.Shared.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FanControl.Service.Ipc;

/// <summary>
/// 命名管道服务：监听 \\.\pipe\FanControl.ipc，逐连接读取 IpcMessage 帧并回写 IpcResponse。
/// </summary>
public sealed class IpcHostedService : BackgroundService
{
    private const string PipeName = "FanControl.ipc";
    private const int MaxInstances = 4;

    private readonly IpcCommandHandler _handler;
    private readonly ILogger<IpcHostedService> _logger;

    public IpcHostedService(IpcCommandHandler handler, ILogger<IpcHostedService> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("命名管道服务已启动：{Pipe}", $@"\\.\pipe\{PipeName}");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    MaxInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(stoppingToken);

                _ = HandleConnectionAsync(server, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "管道等待连接失败，继续监听。");
                await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
            }
        }

        _logger.LogInformation("命名管道服务已停止。");
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream stream, CancellationToken stoppingToken)
    {
        using (stream)
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested && stream.IsConnected)
                {
                    var json = await PipeFraming.ReadFrameAsync(stream, stoppingToken);
                    if (json is null)
                    {
                        break;
                    }

                    var message = JsonSerializer.Deserialize<IpcMessage>(json, JsonDefaults.Options);
                    if (message is null)
                    {
                        continue;
                    }

                    var response = await _handler.HandleAsync(message, stoppingToken);
                    var responseJson = JsonSerializer.Serialize(response, JsonDefaults.Options);
                    await PipeFraming.WriteFrameAsync(stream, responseJson, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常关闭
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "管道连接处理异常。");
            }
        }
    }
}
