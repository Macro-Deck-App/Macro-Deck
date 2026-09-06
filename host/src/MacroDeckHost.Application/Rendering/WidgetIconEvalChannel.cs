using System.Threading.Channels;

namespace MacroDeckHost.Application.Rendering;

/// <summary>Kept separate from <see cref="WidgetStateEvalChannel" /> - the icon-provider poll has nothing
/// to do with the state provider's optimistic-state machinery, the most delicate code in that file.</summary>
public sealed class WidgetIconEvalChannel
{
	private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
	{
		SingleReader = true
	});

	public void Enqueue(Guid widgetId) => _channel.Writer.TryWrite(widgetId);

	public ChannelReader<Guid> Reader => _channel.Reader;
}
