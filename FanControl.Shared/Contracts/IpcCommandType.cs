namespace FanControl.Shared.Contracts;

/// <summary>IPC 命令类型骨架（M3 定义完整协议）。</summary>
public enum IpcCommandType
{
    Ping = 0,
    GetConfig = 1,
    SetConfig = 2,
    SetMode = 3,
    SetCurve = 4,
    SetCommunicationType = 5,
    GetSnapshot = 6,
    Restart = 7,
    Shutdown = 8,
}
