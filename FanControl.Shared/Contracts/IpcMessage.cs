namespace FanControl.Shared.Contracts;

/// <summary>IPC 请求信封：命令 + 可选请求标识 + 可选 JSON 载荷。</summary>
public sealed record IpcMessage(
    IpcCommandType Command,
    string? RequestId,
    string? PayloadJson);
