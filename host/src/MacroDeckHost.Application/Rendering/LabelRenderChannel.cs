using System.Threading.Channels;

namespace MacroDeckHost.Application.Rendering;

public readonly record struct LabelRender(Guid WidgetId, bool OnlyIfChanged);

public sealed class LabelRenderChannel
{
	private readonly Channel<LabelRender> _channel = Channel.CreateUnbounded<LabelRender>(new UnboundedChannelOptions
	{
		SingleReader = true
	});

	public void Enqueue(Guid widgetId)
	{
		_channel.Writer.TryWrite(new LabelRender(widgetId, false));
	}

	public void EnqueueIfChanged(Guid widgetId)
	{
		_channel.Writer.TryWrite(new LabelRender(widgetId, true));
	}

	public ChannelReader<LabelRender> Reader => _channel.Reader;
}
