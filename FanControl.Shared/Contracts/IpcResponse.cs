namespace FanControl.Shared.Contracts;

/// <summary>IPC 响应信封：成功标志 + 错误信息 + 可选 JSON 载荷。</summary>
public sealed record IpcResponse(
    string? RequestId,
    bool Success,
    string? Error,
    string? PayloadJson)
{
    public static IpcResponse Ok(string? requestId = null, string? payloadJson = null) =>
        new(requestId, true, null, payloadJson);

    public static IpcResponse Fail(string? requestId = null, string? error = null) =>
        new(requestId, false, error, null);
}
