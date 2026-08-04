using System.Buffers.Binary;
using System.Text;

namespace FanControl.Shared.Contracts;

/// <summary>命名管道帧：4 字节小端长度前缀 + UTF-8 JSON 载荷。</summary>
public static class PipeFraming
{
    public const int MaxFrameSize = 4 * 1024 * 1024;

    public static async Task WriteFrameAsync(
        Stream stream,
        string json,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(json);

        var payload = Encoding.UTF8.GetBytes(json);
        if (payload.Length > MaxFrameSize)
        {
            throw new InvalidOperationException($"帧载荷超过上限 {MaxFrameSize} 字节。");
        }

        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);

        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<string?> ReadFrameAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[4];
        var headerRead = await ReadExactlyAsync(stream, header, header.Length, cancellationToken)
            .ConfigureAwait(false);

        if (headerRead == 0)
        {
            return null; // 对端已关闭
        }

        if (headerRead < header.Length)
        {
            throw new EndOfStreamException("管道帧头不完整。");
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 0 or > MaxFrameSize)
        {
            throw new InvalidOperationException($"非法帧长度：{length}。");
        }

        if (length == 0)
        {
            return string.Empty;
        }

        var payload = new byte[length];
        var payloadRead = await ReadExactlyAsync(stream, payload, length, cancellationToken)
            .ConfigureAwait(false);

        if (payloadRead < length)
        {
            throw new EndOfStreamException("管道帧载荷不完整。");
        }

        return Encoding.UTF8.GetString(payload);
    }

    private static async Task<int> ReadExactlyAsync(
        Stream stream,
        byte[] buffer,
        int count,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, count - total), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
