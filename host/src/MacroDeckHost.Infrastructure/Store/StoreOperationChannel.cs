using System.Threading.Channels;

namespace MacroDeckHost.Infrastructure.Store;

/// <summary>Queues operation ids for the single worker in <c>StoreOperationBackgroundService</c>. Unbounded
/// because an install request must never be dropped for lack of buffer space - the queue only ever holds
/// as many entries as there are pending installs, which is bounded by user action, not by throughput.
/// </summary>
public sealed class StoreOperationChannel
{
	private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

	public ChannelWriter<Guid> Writer => _channel.Writer;

	public ChannelReader<Guid> Reader => _channel.Reader;
}
