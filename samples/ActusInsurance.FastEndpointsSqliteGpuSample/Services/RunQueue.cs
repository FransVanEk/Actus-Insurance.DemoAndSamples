using System.Threading.Channels;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Services;

/// <summary>In-memory channel queue for async run scheduling.</summary>
public sealed class RunQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true });

    public ChannelWriter<Guid> Writer => _channel.Writer;
    public ChannelReader<Guid> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(Guid runId, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(runId, ct);
}
