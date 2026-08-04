namespace FanControl.Service.Communication;

/// <summary>发送给 ESP32 的 PWM 指令（0-100）。</summary>
public sealed record PwmCommand(double PwmPercent);
