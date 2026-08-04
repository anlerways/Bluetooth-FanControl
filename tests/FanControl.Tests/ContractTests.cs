using System.Text.Json;
using FanControl.Shared.Models;

namespace FanControl.Tests;

public class ContractTests
{
    [Fact]
    public void DataPacket_JsonRoundTrip_PreservesValues()
    {
        var packet = new DataPacket(
            52.5,
            61.0,
            1200,
            45.0,
            "CpuTemp",
            DateTimeOffset.Parse("2026-08-02T10:00:00+08:00"));

        var json = JsonSerializer.Serialize(packet);
        var restored = JsonSerializer.Deserialize<DataPacket>(json);

        Assert.Equal(packet, restored);
    }
}
