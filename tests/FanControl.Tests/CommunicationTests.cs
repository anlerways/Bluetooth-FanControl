using FanControl.Service.Communication;
using FanControl.Service.Config;
using FanControl.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FanControl.Tests;

public class CommunicationTests
{
    private static ServiceProvider BuildProvider()
    {
        var root = Path.Combine(Path.GetTempPath(), "FanControl.CommTests." + Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddSingleton<IConfigManager>(
            new ConfigManager(NullLogger<ConfigManager>.Instance, root, Path.Combine(root, "install")));
        services.AddSingleton<ILogger<ComChannel>>(NullLogger<ComChannel>.Instance);
        services.AddSingleton<ILogger<BleChannel>>(NullLogger<BleChannel>.Instance);
        services.AddSingleton<ComChannel>();
        services.AddSingleton<BleChannel>();
        services.AddSingleton<CommunicationChannelFactory>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Factory_ReturnsChannelForEachType()
    {
        using var provider = BuildProvider();
        var factory = provider.GetRequiredService<CommunicationChannelFactory>();

        Assert.IsType<ComChannel>(factory.Create(CommunicationType.Com));
        Assert.IsType<BleChannel>(factory.Create(CommunicationType.Ble));
    }

    [Fact]
    public void Factory_UnknownType_Throws()
    {
        using var provider = BuildProvider();
        var factory = provider.GetRequiredService<CommunicationChannelFactory>();

        Assert.Throws<ArgumentOutOfRangeException>(() => factory.Create((CommunicationType)999));
    }

    [Fact]
    public async Task ComChannel_SendWithoutConnect_ThrowsInvalidOperation()
    {
        using var provider = BuildProvider();
        var channel = provider.GetRequiredService<ComChannel>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => channel.SendAsync(new PwmCommand(45)));
    }

    [Fact]
    public async Task BleChannel_WithoutConfiguredDevice_ThrowsInvalidOperation()
    {
        var root = Path.Combine(Path.GetTempPath(), "FanControl.BleTests." + Guid.NewGuid().ToString("N"));
        var channel = new BleChannel(
            new ConfigManager(NullLogger<ConfigManager>.Instance, root, Path.Combine(root, "install")),
            NullLogger<BleChannel>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => channel.ConnectAsync());
        Assert.Contains("BLE 设备名", exception.Message);
    }
}
