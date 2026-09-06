using System.Threading.Channels;

namespace MacroDeckHost.Application.Rendering;

public sealed class LabelRenderChannel
{
	private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
	{
		SingleReader = true
	});

	public void Enqueue(Guid widgetId) => _channel.Writer.TryWrite(widgetId);

	public ChannelReader<Guid> Reader => _channel.Reader;
}
