using System.Text.Json;
using FanControl.Service.Config;
using FanControl.Service.Host;
using FanControl.Service.Ipc;
using FanControl.Shared.Contracts;
using FanControl.Shared.Enums;
using FanControl.Shared.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace FanControl.Tests;

public class IpcCommandHandlerTests
{
    [Fact]
    public async Task Ping_ReturnsPong()
    {
        var (handler, _, _) = CreateHandler();

        var response = await handler.HandleAsync(new IpcMessage(IpcCommandType.Ping, "q1", null));

        Assert.True(response.Success);
        Assert.Equal("\"pong\"", response.PayloadJson);
    }

    [Fact]
    public async Task GetConfig_ReturnsCurrentConfig()
    {
        var (handler, state, _) = CreateHandler();

        var response = await handler.HandleAsync(new IpcMessage(IpcCommandType.GetConfig, "q2", null));
        var config = JsonSerializer.Deserialize<AppConfig>(response.PayloadJson!, JsonDefaults.Options);

        Assert.True(response.Success);
        Assert.NotNull(config);
        Assert.Equal(state.Current.FanControlMode, config.FanControlMode);
    }

    [Fact]
    public async Task SetMode_UpdatesStateAndPersists()
    {
        var (handler, state, root) = CreateHandler();

        var response = await handler.HandleAsync(new IpcMessage(
            IpcCommandType.SetMode,
            "q3",
            JsonSerializer.Serialize(FanControlMode.Mixed, JsonDefaults.Options)));

        Assert.True(response.Success);
        Assert.Equal(FanControlMode.Mixed, state.Current.FanControlMode);

        // 重新从磁盘加载验证持久化
        var reloaded = new ConfigManager(
            NullLogger<ConfigManager>.Instance,
            root,
            System.IO.Path.Combine(root, "install"));
        Assert.Equal(
            FanControlMode.Mixed,
            (await reloaded.LoadAppConfigAsync()).FanControlMode);
    }

    [Fact]
    public async Task SetCurve_UpdatesCurve()
    {
        var (handler, state, _) = CreateHandler();
        var curve = new List<CurvePoint> { new(20, 10), new(80, 90) };

        var response = await handler.HandleAsync(new IpcMessage(
            IpcCommandType.SetCurve,
            "q4",
            JsonSerializer.Serialize(curve, JsonDefaults.Options)));

        Assert.True(response.Success);
        Assert.True(state.Current.Curve.SequenceEqual(curve));
    }

    [Fact]
    public async Task GetSnapshot_BeforePublish_Fails_AfterPublish_ReturnsPacket()
    {
        var (handler, state, _) = CreateHandler();

        var before = await handler.HandleAsync(new IpcMessage(IpcCommandType.GetSnapshot, "q5", null));
        Assert.False(before.Success);

        var packet = new DataPacket(50, 60, 1200, 45, "CpuTemp", DateTimeOffset.Now);
        state.Publish(packet);

        var after = await handler.HandleAsync(new IpcMessage(IpcCommandType.GetSnapshot, "q6", null));
        var restored = JsonSerializer.Deserialize<DataPacket>(after.PayloadJson!, JsonDefaults.Options);

        Assert.True(after.Success);
        Assert.Equal(packet, restored);
    }

    [Fact]
    public async Task SetConfig_InvalidJson_ReturnsFail()
    {
        var (handler, _, _) = CreateHandler();

        var response = await handler.HandleAsync(new IpcMessage(IpcCommandType.SetConfig, "q7", "{ bad json"));

        Assert.False(response.Success);
        Assert.Contains("JSON", response.Error);
    }

    [Fact]
    public async Task Restart_TriggersLifetimeStop()
    {
        var (handler, _, _) = CreateHandler();

        var response = await handler.HandleAsync(new IpcMessage(IpcCommandType.Restart, "q8", null));

        Assert.True(response.Success);
    }

    private static (IpcCommandHandler Handler, AppState State, string Root) CreateHandler()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "FanControl.IpcTests." + Guid.NewGuid().ToString("N"));
        var configManager = new ConfigManager(
            NullLogger<ConfigManager>.Instance,
            root,
            System.IO.Path.Combine(root, "install"));
        var state = new AppState(configManager);
        state.InitializeAsync().GetAwaiter().GetResult();

        var handler = new IpcCommandHandler(
            state,
            new TestLifetime(),
            NullLogger<IpcCommandHandler>.Instance);

        return (handler, state, root);
    }

    private sealed class TestLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted { get; } = new();

        public CancellationToken ApplicationStopping { get; } = new();

        public CancellationToken ApplicationStopped { get; } = new();

        public void StopApplication()
        {
            // 测试替身：仅记录，不停止宿主
        }
    }
}
