using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Tests.UnitTests.Adb;

internal sealed class RecordingPublisher : IEventPublisher
{
	public List<(string EventId, IReadOnlyDictionary<string, object?>? Parameters)> Published { get; } = [];

	public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
		=> Published.Add((eventId, parameters));
}
