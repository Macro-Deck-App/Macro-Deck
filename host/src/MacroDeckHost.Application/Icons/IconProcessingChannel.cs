using System.Threading.Channels;

namespace MacroDeckHost.Application.Icons;

public abstract record IconWorkItem;

public sealed record ExtractBatchWorkItem(Guid BatchId) : IconWorkItem;

public sealed record ProcessIconWorkItem(Guid IconId) : IconWorkItem;

public sealed class IconProcessingChannel
{
	private readonly Channel<IconWorkItem> _channel = Channel.CreateUnbounded<IconWorkItem>();

	public void Enqueue(IconWorkItem item) => _channel.Writer.TryWrite(item);

	public ChannelReader<IconWorkItem> Reader => _channel.Reader;
}
