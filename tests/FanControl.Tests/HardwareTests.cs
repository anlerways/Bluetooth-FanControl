using FanControl.Service.Hardware;
using FanControl.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace FanControl.Tests;

public class HardwareTests
{
    [Fact]
    public async Task SimulatedProvider_ReturnsPlausibleTemperatures()
    {
        var provider = new SimulatedTemperatureProvider();

        var snapshot = await provider.ReadAsync();

        Assert.Equal(TemperatureSource.Simulated, provider.Source);
        Assert.NotNull(snapshot.CpuTemperatureCelsius);
        Assert.InRange(snapshot.CpuTemperatureCelsius!.Value, 20, 90);
        Assert.NotNull(snapshot.GpuTemperatureCelsius);
    }

    [Fact]
    public void Factory_ReturnsProviderForEachSource()
    {
        var services = new ServiceCollection();
        services.AddSingleton<AtkAcpiTemperatureProvider>();
        services.AddSingleton<WmiTemperatureProvider>();
        services.AddSingleton<LibreHardwareMonitorTemperatureProvider>();
        services.AddSingleton<Aida64TemperatureProvider>();
        services.AddSingleton<SimulatedTemperatureProvider>();
        services.AddSingleton<TemperatureProviderFactory>();
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<TemperatureProviderFactory>();

        Assert.IsType<AtkAcpiTemperatureProvider>(factory.Create(TemperatureSource.AtkAcpi));
        Assert.IsType<WmiTemperatureProvider>(factory.Create(TemperatureSource.Wmi));
        Assert.IsType<LibreHardwareMonitorTemperatureProvider>(
            factory.Create(TemperatureSource.LibreHardwareMonitor));
        Assert.IsType<Aida64TemperatureProvider>(factory.Create(TemperatureSource.Aida64));
        Assert.IsType<SimulatedTemperatureProvider>(factory.Create(TemperatureSource.Simulated));
    }

    [Fact]
    public void FanSpeedFactory_ReturnsProviderForEachSource()
    {
        var services = new ServiceCollection();
        services.AddSingleton<AtkAcpiFanSpeedProvider>();
        services.AddSingleton<WmiFanSpeedProvider>();
        services.AddSingleton<LibreHardwareMonitorFanSpeedProvider>();
        services.AddSingleton<Aida64FanSpeedProvider>();
        services.AddSingleton<SimulatedFanSpeedProvider>();
        services.AddSingleton<FanSpeedProviderFactory>();
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<FanSpeedProviderFactory>();

        Assert.IsType<AtkAcpiFanSpeedProvider>(factory.Create(FanSpeedSource.AtkAcpi));
        Assert.IsType<WmiFanSpeedProvider>(factory.Create(FanSpeedSource.Wmi));
        Assert.IsType<LibreHardwareMonitorFanSpeedProvider>(
            factory.Create(FanSpeedSource.LibreHardwareMonitor));
        Assert.IsType<Aida64FanSpeedProvider>(factory.Create(FanSpeedSource.Aida64));
        Assert.IsType<SimulatedFanSpeedProvider>(factory.Create(FanSpeedSource.Simulated));
    }

    [Fact]
    public void FanSpeedFactory_UnknownSource_Throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton<FanSpeedProviderFactory>();
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<FanSpeedProviderFactory>();

        Assert.Throws<ArgumentOutOfRangeException>(() => factory.Create((FanSpeedSource)999));
    }

    [Fact]
    public void Factory_UnknownSource_Throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TemperatureProviderFactory>();
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<TemperatureProviderFactory>();

        Assert.Throws<ArgumentOutOfRangeException>(() => factory.Create((TemperatureSource)999));
    }

    [Fact]
    public void AtkAcpiProvider_ExposesExpectedSource()
    {
        using var atk = new AtkAcpiTemperatureProvider();

        Assert.Equal(TemperatureSource.AtkAcpi, atk.Source);
    }

    [Fact]
    public void AtkAcpiFanSpeedProvider_ExposesExpectedSource()
    {
        using var atk = new AtkAcpiFanSpeedProvider();

        Assert.Equal(FanSpeedSource.AtkAcpi, atk.Source);
    }

    [Fact]
    public async Task Aida64Provider_WithoutRegistry_ReturnsEmpty()
    {
        var aida = new Aida64TemperatureProvider();

        var snapshot = await aida.ReadAsync();

        Assert.Null(snapshot.CpuTemperatureCelsius);
        Assert.Null(snapshot.GpuTemperatureCelsius);
    }

    [Fact]
    public void LibreHardwareMonitorProvider_ExposesExpectedSource()
    {
        using var provider = new LibreHardwareMonitorTemperatureProvider();

        Assert.Equal(TemperatureSource.LibreHardwareMonitor, provider.Source);
    }
}
