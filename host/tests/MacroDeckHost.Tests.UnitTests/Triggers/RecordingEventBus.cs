using System.Threading.Channels;
using MacroDeckHost.Application.Triggers;

namespace MacroDeckHost.Tests.UnitTests.Triggers;

internal sealed class RecordingEventBus : IEventBus
{
	private readonly Channel<EventOccurrence> _channel = Channel.CreateUnbounded<EventOccurrence>();

	public List<EventOccurrence> Published { get; } = [];

	public ChannelReader<EventOccurrence> Reader => _channel.Reader;

	public void Publish(EventOccurrence occurrence) => Published.Add(occurrence);
}
