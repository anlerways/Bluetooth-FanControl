using FanControl.Service.Communication;
using FanControl.Service.Fan;
using FanControl.Service.Hardware;
using FanControl.Shared.Enums;
using FanControl.Shared.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FanControl.Service.Host;

/// <summary>
/// 后台监控循环：每次读取 AppState 当前配置，采样温度 → 计算目标 PWM → 发送，
/// 并把最新数据包发布到 AppState（供 IPC GetSnapshot 使用）。
/// 数据源/通信方式变化时自动重建 Provider/Channel。
/// </summary>
public sealed class HardwareHostedService : BackgroundService
{
    private readonly AppState _appState;
    private readonly TemperatureProviderFactory _providerFactory;
    private readonly CommunicationChannelFactory _communicationChannelFactory;
    private readonly FanController _fanController;
    private readonly ILogger<HardwareHostedService> _logger;

    private ITemperatureProvider? _provider;
    private ITemperatureProvider? _gpuProvider;
    private ICommunicationChannel? _channel;

    public HardwareHostedService(
        AppState appState,
        TemperatureProviderFactory providerFactory,
        CommunicationChannelFactory communicationChannelFactory,
        FanController fanController,
        ILogger<HardwareHostedService> logger)
    {
        _appState = appState;
        _providerFactory = providerFactory;
        _communicationChannelFactory = communicationChannelFactory;
        _fanController = fanController;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FanControl.Service 已启动。");

        var interval = TimeSpan.FromMilliseconds(
            Math.Max(200, _appState.Current.PollIntervalMilliseconds));

        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var config = _appState.Current;
                    var provider = GetOrCreateProvider(config.TemperatureSource);
                    var channel = GetOrCreateChannel(config.CommunicationType);

                    var snapshot = await provider.ReadAsync(stoppingToken, config.GpuSelection);

                    // CPU 与 GPU 获取方式分开。单个源失败只影响该温度，不中断整轮采样。
                    var gpuSource = config.GpuTemperatureSource ?? TemperatureSource.NvidiaSmiAdl;
                    if (gpuSource == TemperatureSource.NvidiaSmiAdl)
                    {
                        try
                        {
                            var gpu = GpuTemperatureReader.Read(config.GpuSelection);
                            snapshot = snapshot with { GpuTemperatureCelsius = gpu };
                        }
                        catch (Exception gpuEx)
                        {
                            _logger.LogWarning(gpuEx, "GPU 温度读取失败（NVIDIA-SMI / ADL），继续使用主源数据。");
                        }
                    }
                    else if (gpuSource != config.TemperatureSource)
                    {
                        try
                        {
                            var gpuSnapshot = await GetOrCreateGpuProvider(gpuSource)
                                .ReadAsync(stoppingToken, config.GpuSelection);
                            snapshot = snapshot with { GpuTemperatureCelsius = gpuSnapshot.GpuTemperatureCelsius };
                        }
                        catch (Exception gpuEx)
                        {
                            _logger.LogWarning(gpuEx, "GPU 温度读取失败（{Source}），继续使用主源数据。", gpuSource);
                        }
                    }

                    var targetPwm = _fanController.CalculateTargetPwm(
                        config.FanControlMode,
                        snapshot.CpuTemperatureCelsius ?? double.NaN,
                        snapshot.GpuTemperatureCelsius ?? snapshot.CpuTemperatureCelsius ?? double.NaN,
                        config.ManualPwmPercent,
                        config.Curve,
                        snapshot.FanRpm ?? 0,
                        config.RpmCurve);

                    // 无有效温度（NaN）时不发送 PWM，保持固件当前占空比
                    if (targetPwm is double pwm && !double.IsNaN(pwm))
                    {
                        await SendPwmAsync(channel, pwm, stoppingToken);
                    }

                    _appState.Publish(new DataPacket(
                        snapshot.CpuTemperatureCelsius ?? double.NaN,
                        snapshot.GpuTemperatureCelsius ?? double.NaN,
                        snapshot.FanRpm ?? 0,
                        targetPwm ?? double.NaN,
                        config.FanControlMode.ToString(),
                        DateTimeOffset.Now));

                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "温度采样/控制计算失败，本次循环跳过。");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止
        }
        finally
        {
            await TryDisconnectAsync(_channel);
        }

        _logger.LogInformation("FanControl.Service 已停止。");
    }

    private ITemperatureProvider GetOrCreateProvider(TemperatureSource source)
    {
        if (_provider is null || _provider.Source != source)
        {
            _provider = _providerFactory.Create(source);
            _logger.LogInformation("温度数据源切换为：{Source}", source);
        }

        return _provider;
    }

    /// <summary>GPU 独立温度源：与 CPU 源分开缓存，避免每个采样周期轮流重建。</summary>
    private ITemperatureProvider GetOrCreateGpuProvider(TemperatureSource source)
    {
        if (_gpuProvider is null || _gpuProvider.Source != source)
        {
            _gpuProvider = _providerFactory.Create(source);
            _logger.LogInformation("GPU 温度源切换为：{Source}", source);
        }

        return _gpuProvider;
    }

    private ICommunicationChannel GetOrCreateChannel(CommunicationType type)
    {
        if (_channel is null || _channel.Type != type)
        {
            _channel = _communicationChannelFactory.Create(type);
            _logger.LogInformation("通信方式切换为：{Type}", type);
        }

        return _channel;
    }

    private async Task SendPwmAsync(ICommunicationChannel channel, double pwm, CancellationToken cancellationToken)
    {
        try
        {
            if (!channel.IsConnected)
            {
                await channel.ConnectAsync(cancellationToken);
            }

            await channel.SendAsync(new PwmCommand(pwm), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "PWM 发送失败（{Type}），下个周期将重试连接。", channel.Type);
        }
    }

    private async Task TryDisconnectAsync(ICommunicationChannel? channel)
    {
        try
        {
            if (channel?.IsConnected == true)
            {
                await channel.DisconnectAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "断开通信通道失败。");
        }
    }
}
