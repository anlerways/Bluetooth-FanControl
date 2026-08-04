using FanControl.Service.Communication;
using FanControl.Service.Config;
using FanControl.Service.Fan;
using FanControl.Service.Hardware;
using FanControl.Service.Logging;
using FanControl.Shared.Enums;
using FanControl.Shared.Models;
using System.Diagnostics;
using System.Management;
using Microsoft.Extensions.Logging;

namespace FanControl.Service.Host;

/// <summary>
/// 单进程运行时：初始化配置，运行两条独立轮询循环：
/// 1) 采样循环（0.1-5 秒）：读温度 → 计算目标 PWM（含平滑）→ 串口直接发送 → 发布数据包；
/// 2) 通信循环（仅蓝牙）：互斥型轮询 —— 有连接按 1-30 秒发送 PWM，
///    无连接按 3-300 秒尝试重连（可关闭自动重连）。
/// </summary>
public sealed class FanControlRuntime : IAsyncDisposable
{
    private readonly AppState _appState;
    private readonly IConfigManager _configManager;
    private readonly TemperatureProviderFactory _providerFactory;
    private readonly FanSpeedProviderFactory _fanSpeedProviderFactory;
    private readonly CommunicationChannelFactory _channelFactory;
    private readonly FanController _fanController;
    private readonly FileLoggerProvider _fileLoggerProvider;
    private readonly ILogger<FanControlRuntime> _logger;
    private readonly CancellationTokenSource _cts = new();

    private ITemperatureProvider? _provider;
    private ITemperatureProvider? _gpuProvider;
    private IFanSpeedProvider? _fanSpeedProvider;
    private ICommunicationChannel? _comChannel;
    private ICommunicationChannel? _bleChannel;
    private Task? _samplingTask;
    private Task? _communicationTask;
    private bool _autoFallbackDone;
    private int _lastLoggedInterval = -1;
    private double? _smoothedPwm;
    private volatile float _latestSmoothedPwm = float.NaN;
    private volatile float _latestCpuCelsius = float.NaN;
    private volatile float _latestGpuCelsius = float.NaN;
    private string _commSignature = string.Empty;
    private volatile bool _commConfigDirty;
    private volatile bool _forceReconnect;
    private FanControlMode? _lastMode;
    private TemperatureSource? _lastSource;
    private bool _bleWasConnected;

    public FanControlRuntime(
        AppState appState,
        IConfigManager configManager,
        TemperatureProviderFactory providerFactory,
        FanSpeedProviderFactory fanSpeedProviderFactory,
        CommunicationChannelFactory channelFactory,
        FanController fanController,
        FileLoggerProvider fileLoggerProvider,
        ILogger<FanControlRuntime> logger)
    {
        _appState = appState;
        _configManager = configManager;
        _providerFactory = providerFactory;
        _fanSpeedProviderFactory = fanSpeedProviderFactory;
        _channelFactory = channelFactory;
        _fanController = fanController;
        _fileLoggerProvider = fileLoggerProvider;
        _logger = logger;
    }

    public AppConfig CurrentConfig => _appState.Current;

    public DataPacket? LatestPacket => _appState.LatestPacket;

    /// <summary>
    /// 枚举系统显卡名称（WMI → nvidia-smi 兜底），供设置页 GPU 选择下拉直接显示真实显卡名。
    /// </summary>
    public async Task<IReadOnlyList<string>> EnumerateGpuNamesAsync()
    {
        var names = new List<string>(EnumerateWmiVideoControllers());
        if (names.Count == 0)
        {
            names.AddRange(await EnumerateNvidiaGpuNamesAsync());
        }

        return names.Distinct().ToList();
    }

    private static IReadOnlyList<string> EnumerateWmiVideoControllers()
    {
        try
        {
            var names = new List<string>();
            using var searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                "SELECT Name FROM Win32_VideoController");
            using var collection = searcher.Get();
            foreach (ManagementBaseObject item in collection)
            {
                if (item["Name"] is string name && !string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name.Trim());
                }
            }

            return names;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static async Task<IReadOnlyList<string>> EnumerateNvidiaGpuNamesAsync()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(
                    "nvidia-smi",
                    "--query-gpu=name --format=csv,noheader,nounits")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            if (!process.Start())
            {
                return Array.Empty<string>();
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            return output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>最近一次采样/计算失败的原因（成功时清空）。</summary>
    public string? LastError { get; private set; }

    /// <summary>新数据包到达时触发（后台线程），UI 需自行切回 UI 线程。</summary>
    public event EventHandler? DataChanged;

    /// <summary>温度数据源切换时触发（后台线程），UI 用于清空趋势曲线。</summary>
    public event EventHandler? TemperatureSourceChanged;

    /// <summary>通信链路状态（设置页显示用）。</summary>
    public CommunicationState ConnectionState { get; private set; } = CommunicationState.Waiting;

    public event EventHandler? ConnectionStateChanged;

    /// <summary>蓝牙连接状态变化时触发（true=已连接，false=断开）。</summary>
    public event EventHandler<bool>? BleConnectionChanged;

    /// <summary>温度采样开始失败（上次成功/无错误）时触发，用于异常通知。</summary>
    public event EventHandler? TemperatureErrorOccurred;

    public async Task StartAsync()
    {
        await _appState.InitializeAsync();
        ApplyLoggingSettings(await _configManager.LoadSystemConfigAsync());
        _logger.LogInformation(
            "监控循环启动：数据源 {Source}，通信 {Channel}，采样 {Sample}ms",
            CurrentConfig.TemperatureSource,
            CurrentConfig.CommunicationType,
            CurrentConfig.PollIntervalMilliseconds);
        _samplingTask = Task.Run(() => RunSamplingLoopAsync(_cts.Token));
        _communicationTask = Task.Run(() => RunCommunicationLoopAsync(_cts.Token));
    }

    public Task ApplyConfigAsync(AppConfig config)
    {
        return _appState.ApplyAsync(config);
    }

    /// <summary>读取系统配置（日志位置/开关/保留数量等）。</summary>
    public Task<SystemConfig> LoadSystemConfigAsync()
    {
        return _configManager.LoadSystemConfigAsync();
    }

    /// <summary>保存系统配置并立即应用日志设置（目录/开关/保留数量）。</summary>
    public async Task SaveSystemConfigAsync(SystemConfig system)
    {
        await _configManager.SaveSystemConfigAsync(system);
        ApplyLoggingSettings(system);
    }

    private void ApplyLoggingSettings(SystemConfig system)
    {
        _fileLoggerProvider.Configure(
            _configManager.GetLogDirectory(system),
            system.LogEnabled,
            Math.Max(1, system.MaxLogFiles));
    }

    /// <summary>手动重建连接：立即断开并重试（托盘/设置页触发，任意线程可调用）。</summary>
    public void RequestReconnect()
    {
        _forceReconnect = true;
        _commConfigDirty = true;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();

        foreach (var task in new[] { _samplingTask, _communicationTask })
        {
            if (task is null)
            {
                continue;
            }

            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // 正常停止
            }
            catch
            {
                // 停止阶段异常不影响退出
            }
        }

        _cts.Dispose();

        try
        {
            foreach (var channel in new[] { _comChannel, _bleChannel })
            {
                if (channel?.IsConnected == true)
                {
                    await channel.DisconnectAsync();
                }
            }

            if (_provider is IDisposable disposable)
            {
                disposable.Dispose();
            }

            if (_fanSpeedProvider is IDisposable disposableFan)
            {
                disposableFan.Dispose();
            }
        }
        catch
        {
            // 忽略释放异常
        }
    }

    /// <summary>采样循环：按 0.1-5 秒读取温度、计算（含平滑）并发布；串口通道在此直接发送。</summary>
    private async Task RunSamplingLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var sampleIntervalMs = Math.Clamp(_appState.Current.PollIntervalMilliseconds, 100, 5000);
            if (sampleIntervalMs != _lastLoggedInterval)
            {
                _lastLoggedInterval = sampleIntervalMs;
                _logger.LogInformation("采样间隔已更新为 {Interval}ms", sampleIntervalMs);
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var config = _appState.Current;
                await EnsureCommConfigAsync(config);
                var provider = GetOrCreateProvider(config.TemperatureSource);
                var snapshot = await provider.ReadAsync(cancellationToken, config.GpuSelection);

                // ATKACPI 在华硕新机型上多数不支持温度方法：主源完全无数据时自动切到 LHM
                if (!_autoFallbackDone
                    && config.TemperatureSource == TemperatureSource.AtkAcpi
                    && snapshot.CpuTemperatureCelsius is null
                    && snapshot.GpuTemperatureCelsius is null)
                {
                    _autoFallbackDone = true;
                    _logger.LogWarning("ATKACPI 当前机型无温度数据，自动切换到 LibreHardwareMonitor。");
                    await _appState.ApplyAsync(
                        _appState.Current with
                        {
                            TemperatureSource = TemperatureSource.LibreHardwareMonitor,
                        });
                }

                // CPU 与 GPU 获取方式分开。单个源失败只影响该温度，不中断整轮采样/绘图。
                var gpuSource = config.GpuTemperatureSource ?? TemperatureSource.NvidiaSmiAdl;
                if (gpuSource == TemperatureSource.NvidiaSmiAdl)
                {
                    // 专用链路：NVIDIA nvidia-smi → AMD ADL
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
                            .ReadAsync(cancellationToken, config.GpuSelection);
                        snapshot = snapshot with { GpuTemperatureCelsius = gpuSnapshot.GpuTemperatureCelsius };
                    }
                    catch (Exception gpuEx)
                    {
                        _logger.LogWarning(gpuEx, "GPU 温度读取失败（{Source}），继续使用主源数据。", gpuSource);
                    }
                }

                // 转速数据源独立选择：与温度源相同时直接复用快照（避免重复读取），
                // 不同则调用所选转速 Provider；读取失败不中断本轮采样。
                // 两个枚举按相同编号对齐（AtkAcpi=0 … Simulated=99），故按数值比较。
                var fanRpm = snapshot.FanRpm;
                if ((int)config.FanSpeedSource != (int)config.TemperatureSource)
                {
                    try
                    {
                        fanRpm = await GetOrCreateFanSpeedProvider(config.FanSpeedSource)
                            .ReadAsync(cancellationToken);
                    }
                    catch (Exception rpmEx)
                    {
                        _logger.LogWarning(rpmEx, "风扇转速读取失败（{Source}），本轮无转速数据。", config.FanSpeedSource);
                        fanRpm = snapshot.FanRpm;
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

                var smoothedPwm = ApplySmoothing(config, targetPwm);
                // 温度无有效数据（NaN）时保持上次 PWM，不把 NaN/末点 100% 发给固件
                if (!double.IsNaN(smoothedPwm ?? double.NaN))
                {
                    _latestSmoothedPwm = (float)smoothedPwm!.Value;
                }

                _latestCpuCelsius = snapshot.CpuTemperatureCelsius is double c
                    ? (float)c
                    : float.NaN;
                _latestGpuCelsius = snapshot.GpuTemperatureCelsius is double g
                    ? (float)g
                    : float.NaN;

                _appState.Publish(new DataPacket(
                    snapshot.CpuTemperatureCelsius ?? double.NaN,
                    snapshot.GpuTemperatureCelsius ?? double.NaN,
                    fanRpm ?? 0,
                    _latestSmoothedPwm,
                    config.FanControlMode.ToString(),
                    DateTimeOffset.Now,
                    targetPwm ?? double.NaN));

                LastError = null;
                try
                {
                    DataChanged?.Invoke(this, EventArgs.Empty);
                }
                catch
                {
                    // 订阅方（UI）异常不影响采样循环状态
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var firstError = LastError is null;
                LastError = ex.Message;
                _logger.LogWarning(ex, "温度采样/控制计算失败（数据源 {Source}）。", _appState.Current.TemperatureSource);

                if (firstError)
                {
                    TemperatureErrorOccurred?.Invoke(this, EventArgs.Empty);
                }

                // ATKACPI 在华硕新机型上多数不支持温度方法（G-Helper 也弃用），
                // 首次失败时自动切换到 LibreHardwareMonitor 并持久化，避免用户卡在不可用数据源上。
                if (!_autoFallbackDone
                    && _appState.Current.TemperatureSource == TemperatureSource.AtkAcpi
                    && ex is NotSupportedException or InvalidOperationException)
                {
                    _autoFallbackDone = true;
                    _logger.LogWarning("ATKACPI 当前机型不支持温度读取，自动切换到 LibreHardwareMonitor。");
                    await _appState.ApplyAsync(
                        _appState.Current with
                        {
                            TemperatureSource = TemperatureSource.LibreHardwareMonitor,
                        });
                }
            }

            var sampleMs = (int)stopwatch.ElapsedMilliseconds;
            var remaining = sampleIntervalMs - sampleMs;
            if (remaining > 0)
            {
                try
                {
                    await Task.Delay(remaining, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            // 实际周期明显超过目标时记录偏差，便于定位"设置间隔与实际刷新不一致"
            var cycleMs = (int)stopwatch.ElapsedMilliseconds;
            if (cycleMs > sampleIntervalMs * 1.5 + 500)
            {
                _logger.LogWarning(
                    "实际采样周期 {Cycle}ms 超过目标 {Interval}ms（本轮采样耗时 {Sample}ms）",
                    cycleMs,
                    sampleIntervalMs,
                    sampleMs);
            }
        }
    }

    /// <summary>
    /// 通信循环（仅蓝牙）：互斥型轮询 —— 已连接按 1-30 秒发送 PWM；
    /// 未连接按 3-300 秒尝试重连（可关闭）。串口由采样循环负责，此处仅空转。
    /// </summary>
    /// <summary>
    /// 通信循环：COM 与 BLE 统一复用同一套轮询时间——
    /// 已连接按 BlePollIntervalSeconds 发送 PWM+温度+首次下发时间；
    /// 未连接按 BleReconnectIntervalSeconds 尝试重连（BLE 受自动重连开关控制，COM 始终重连）。
    /// </summary>
    private async Task RunCommunicationLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var config = _appState.Current;
            _commConfigDirty = false;
            await EnsureCommConfigAsync(config);
            var channel = GetChannel(config.CommunicationType);

            // 连接状态变化事件（断连通知用）
            if (channel.IsConnected != _bleWasConnected)
            {
                _bleWasConnected = channel.IsConnected;
                try
                {
                    BleConnectionChanged?.Invoke(this, _bleWasConnected);
                }
                catch
                {
                    // 订阅方异常不影响通信循环
                }
            }

            var pollInterval = channel.IsConnected
                ? TimeSpan.FromSeconds(Math.Clamp(config.BlePollIntervalSeconds, 1, 30))
                : TimeSpan.FromSeconds(Math.Clamp(config.BleReconnectIntervalSeconds, 1, 60));

            // 手动重建：断开当前通道并立即重连（不受自动重连开关限制）
            var forceReconnect = _forceReconnect;
            _forceReconnect = false;
            if (forceReconnect)
            {
                await DisconnectChannelsAsync();
                channel = GetChannel(config.CommunicationType);
            }

            try
            {
                if (channel.IsConnected)
                {
                    // 连接状态下每个轮询周期都下发时间：固件重启/首条丢失时也能在下一轮重新校时
                    await channel.SendTimeAsync(DateTime.Now, cancellationToken);

                    if (!float.IsNaN(_latestSmoothedPwm))
                    {
                        await channel.SendAsync(new PwmCommand(_latestSmoothedPwm), cancellationToken);
                        await channel.SendTemperaturesAsync(
                            _latestCpuCelsius,
                            float.IsNaN(_latestGpuCelsius) ? null : _latestGpuCelsius,
                            cancellationToken);
                    }

                    SetConnectionState(CommunicationState.Connected);
                }
                else
                {
                    var shouldReconnect = config.AutoReconnectBle || forceReconnect;

                    if (shouldReconnect)
                    {
                        await channel.ConnectAsync(cancellationToken);
                    }

                    SetConnectionState(CommunicationState.Reconnecting);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                SetConnectionState(CommunicationState.Reconnecting);
            }

            // 等待下个周期；配置变更时立即退出等待，马上按新配置重连
            var waitUntil = DateTime.UtcNow + pollInterval;
            while (DateTime.UtcNow < waitUntil && !_commConfigDirty)
            {
                try
                {
                    await Task.Delay(250, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>PWM 指数平滑：温度剧烈波动时转速平缓过渡，系数越小越平滑。</summary>
    private double? ApplySmoothing(AppConfig config, double? targetPwm)
    {
        // 无有效目标（null 或 NaN，如温度源无数据）时保持上次平滑值，不污染平滑状态
        if (targetPwm is not double target || double.IsNaN(target))
        {
            return null;
        }

        // 平滑开关关闭或手动模式：直接使用目标 PWM，不做平滑
        if (!config.SmoothingEnabled || config.FanControlMode == FanControlMode.Manual)
        {
            _smoothedPwm = target;
            return target;
        }

        if (_lastMode != config.FanControlMode || _lastSource != config.TemperatureSource)
        {
            _lastMode = config.FanControlMode;
            _lastSource = config.TemperatureSource;
            _smoothedPwm = null;
        }

        var factor = Math.Clamp(config.PwmSmoothing, 0.05, 1.0);
        if (factor >= 1.0 || _smoothedPwm is null)
        {
            _smoothedPwm = target;
        }
        else
        {
            _smoothedPwm = _smoothedPwm.Value + (target - _smoothedPwm.Value) * factor;
        }

        return _smoothedPwm;
    }

    private ITemperatureProvider GetOrCreateProvider(TemperatureSource source)
    {
        if (_provider is null || _provider.Source != source)
        {
            _provider = _providerFactory.Create(source);
            try
            {
                TemperatureSourceChanged?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                // 订阅方异常不影响采样循环状态
            }
        }

        return _provider;
    }

    /// <summary>
    /// GPU 独立温度源：与 CPU 源分开缓存，避免两个 Provider 轮流重建触发
    /// TemperatureSourceChanged（会导致仪表盘趋势图每轮被清空而闪烁）。
    /// </summary>
    private ITemperatureProvider GetOrCreateGpuProvider(TemperatureSource source)
    {
        if (_gpuProvider is null || _gpuProvider.Source != source)
        {
            _gpuProvider = _providerFactory.Create(source);
        }

        return _gpuProvider;
    }

    private IFanSpeedProvider GetOrCreateFanSpeedProvider(FanSpeedSource source)
    {
        if (_fanSpeedProvider is null || _fanSpeedProvider.Source != source)
        {
            _fanSpeedProvider = _fanSpeedProviderFactory.Create(source);
        }

        return _fanSpeedProvider;
    }

    private ICommunicationChannel GetChannel(CommunicationType type)
    {
        if (type == CommunicationType.Com)
        {
            return _comChannel ??= _channelFactory.Create(CommunicationType.Com);
        }

        return _bleChannel ??= _channelFactory.Create(CommunicationType.Ble);
    }

    /// <summary>
    /// 通信配置变更检测：切换端口/波特率/BLE 名/通信方式后，
    /// 断开旧通道并重置重连状态，下一周期按新配置重新握手。
    /// </summary>
    private async Task EnsureCommConfigAsync(AppConfig config)
    {
        var signature = $"{config.CommunicationType}|{config.ComPort}|{config.ComBaudRate}|{config.BleDeviceName}";
        if (signature == _commSignature)
        {
            return;
        }

        _commSignature = signature;
        _logger.LogInformation("通信配置变更（{Signature}），断开旧通道并重新握手。", signature);
        _commConfigDirty = true;

        await DisconnectChannelsAsync();
        _bleWasConnected = false;
        SetConnectionState(CommunicationState.Waiting);
    }

    private async Task DisconnectChannelsAsync()
    {
        foreach (var channel in new[] { _comChannel, _bleChannel })
        {
            if (channel?.IsConnected == true)
            {
                try
                {
                    await channel.DisconnectAsync();
                }
                catch
                {
                    // 释放失败不影响后续
                }
            }
        }

        _comChannel = null;
        _bleChannel = null;
    }

    private void SetConnectionState(CommunicationState state)
    {
        if (ConnectionState == state)
        {
            return;
        }

        ConnectionState = state;
        try
        {
            ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // 订阅方异常不影响通信循环
        }
    }
}
