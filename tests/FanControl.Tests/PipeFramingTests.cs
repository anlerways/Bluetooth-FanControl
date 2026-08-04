using FanControl.Shared.Contracts;

namespace FanControl.Tests;

public class PipeFramingTests
{
    [Fact]
    public async Task WriteRead_RoundTrip_PreservesPayload()
    {
        await using var stream = new MemoryStream();
        const string payload = "{\"command\":2,\"requestId\":\"r1\"} 温度测试";

        await PipeFraming.WriteFrameAsync(stream, payload);
        stream.Position = 0;

        var read = await PipeFraming.ReadFrameAsync(stream);

        Assert.Equal(payload, read);
    }

    [Fact]
    public async Task WriteRead_EmptyPayload_ReturnsEmptyString()
    {
        await using var stream = new MemoryStream();

        await PipeFraming.WriteFrameAsync(stream, string.Empty);
        stream.Position = 0;

        Assert.Equal(string.Empty, await PipeFraming.ReadFrameAsync(stream));
    }

    [Fact]
    public async Task Read_EmptyStream_ReturnsNull()
    {
        await using var stream = new MemoryStream();

        Assert.Null(await PipeFraming.ReadFrameAsync(stream));
    }

    [Fact]
    public async Task Read_TruncatedPayload_Throws()
    {
        await using var stream = new MemoryStream();
        stream.Write(new byte[] { 10, 0, 0, 0 }); // 声明 10 字节但无载荷
        stream.Position = 0;

        await Assert.ThrowsAsync<EndOfStreamException>(
            () => PipeFraming.ReadFrameAsync(stream));
    }

    [Fact]
    public async Task Write_OverMaxSize_Throws()
    {
        await using var stream = new MemoryStream();
        var oversized = new string('a', PipeFraming.MaxFrameSize + 1);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => PipeFraming.WriteFrameAsync(stream, oversized));
    }
}
