using System.IO.Pipes;
using System.Text.Json;

namespace FanControl.Shared.Contracts;

/// <summary>
/// 命名管道客户端：连接后台服务并收发 IpcMessage / IpcResponse。
/// 连接带 5 秒超时，避免服务未运行时界面无限等待。
/// </summary>
public sealed class NamedPipeClient : IDisposable
{
    private const string PipeName = "FanControl.ipc";
    private const int ConnectTimeoutMilliseconds = 5000;
    private NamedPipeClientStream? _stream;

    public bool IsConnected => _stream?.IsConnected == true;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_stream?.IsConnected == true)
        {
            return;
        }

        Disconnect();

        _stream = new NamedPipeClientStream(
            ".",
            PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ConnectTimeoutMilliseconds);

        try
        {
            await _stream.ConnectAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Disconnect();
            throw new TimeoutException($"连接服务超时（{ConnectTimeoutMilliseconds}ms），请确认后台服务已启动。");
        }
    }

    public async Task<IpcResponse> SendAsync(
        IpcMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_stream?.IsConnected != true)
        {
            await ConnectAsync(cancellationToken);
        }

        var requestJson = JsonSerializer.Serialize(message, JsonDefaults.Options);
        await PipeFraming.WriteFrameAsync(_stream!, requestJson, cancellationToken);

        var responseJson = await PipeFraming.ReadFrameAsync(_stream!, cancellationToken);
        if (responseJson is null)
        {
            Disconnect();
            return IpcResponse.Fail(message.RequestId, "管道已断开。");
        }

        return JsonSerializer.Deserialize<IpcResponse>(responseJson, JsonDefaults.Options)
            ?? IpcResponse.Fail(message.RequestId, "响应解析失败。");
    }

    public void Disconnect()
    {
        _stream?.Dispose();
        _stream = null;
    }

    public void Dispose()
    {
        Disconnect();
    }
}
