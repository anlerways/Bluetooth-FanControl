using FanControl.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace FanControl.Service.Communication;

/// <summary>按配置选择通信方式（策略工厂）。</summary>
public sealed class CommunicationChannelFactory
{
    private readonly IServiceProvider _services;

    public CommunicationChannelFactory(IServiceProvider services)
    {
        _services = services;
    }

    public ICommunicationChannel Create(CommunicationType type) =>
        type switch
        {
            CommunicationType.Com => Get<ComChannel>(),
            CommunicationType.Ble => Get<BleChannel>(),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "未知的通信方式。"),
        };

    private ICommunicationChannel Get<T>()
        where T : ICommunicationChannel
        => _services.GetRequiredService<T>();
}
