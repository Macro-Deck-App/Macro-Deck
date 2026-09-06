using System.Collections.Concurrent;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// Every <c>event.publish</c> a plugin under test has raised, in arrival order. Records only real
/// <c>event.publish</c> traffic - <c>state.update</c>, <c>log.publish</c> and <c>capability.result</c>
/// messages the same action may also produce never appear here, so a count against this collector is a
/// count of events actually published, not of everything that happened to be in flight.
/// </summary>
public sealed class PluginEventCollector
{
	private readonly ConcurrentQueue<PublishedEvent> _published = new();

	/// <summary>Every event published so far, in arrival order.</summary>
	public IReadOnlyList<PublishedEvent> Published => [.. _published];

	/// <summary>Waits until an event with id <paramref name="eventId" /> has been published.</summary>
	/// <exception cref="PluginTestTimeoutException">The event was never published before the deadline.</exception>
	public Task WaitForAsync(string eventId, TimeSpan? timeout = null)
	{
		ArgumentException.ThrowIfNullOrEmpty(eventId);

		return Wait.UntilAsync(() =>
				Published.Any(published => string.Equals(published.EventId, eventId, StringComparison.Ordinal)),
			timeout,
			because: $"event '{eventId}' was never published");
	}

	internal void Record(PublishedEvent publishedEvent) => _published.Enqueue(publishedEvent);
}
