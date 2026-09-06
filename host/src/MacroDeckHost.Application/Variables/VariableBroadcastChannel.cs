using System.Threading.Channels;

namespace MacroDeckHost.Application.Variables;

public sealed class VariableBroadcastChannel
{
	private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
	{
		SingleReader = true
	});

	public void Enqueue(Guid variableId) => _channel.Writer.TryWrite(variableId);

	public ChannelReader<Guid> Reader => _channel.Reader;
}
