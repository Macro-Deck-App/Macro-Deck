namespace MacroDeckHost.Application.Triggers;

public sealed record EventOccurrence(
	string EventId,
	IReadOnlyDictionary<string, object?> Parameters,
	EventTarget? Target = null)
{
	public static readonly IReadOnlyDictionary<string, object?> NoParameters
		= new Dictionary<string, object?>(StringComparer.Ordinal);
}

public readonly record struct EventTarget(EventTriggerOwner Owner, string TriggerId);
