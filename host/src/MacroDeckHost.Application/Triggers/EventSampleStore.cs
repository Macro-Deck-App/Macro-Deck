using System.Collections.Concurrent;

namespace MacroDeckHost.Application.Triggers;

public sealed class EventSampleStore
{
	private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, object?>> _samples
		= new(StringComparer.Ordinal);

	public void Record(EventOccurrence occurrence) => _samples[occurrence.EventId] = occurrence.Parameters;

	public IReadOnlyDictionary<string, object?>? TryGetLast(string qualifiedEventId)
		=> _samples.TryGetValue(qualifiedEventId, out var parameters) ? parameters : null;
}
