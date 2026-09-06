using System.Text.Json;

namespace MacroDeckHost.Application.Triggers;

public static class EventFilterOperators
{
	public const string Default = "==";

	public static string Normalize(string? candidate) => candidate switch
	{
		"==" or "!=" or ">" or "<" or ">=" or "<=" => candidate,

		// The state vocabulary, kept as its own arm: these ask about the payload value's own state and
		// carry no configured value of their own.
		"isEmpty" or "isNotEmpty" or "isAvailable" or "isNotAvailable" => candidate,
		_ => Default
	};
}

public sealed record EventConfigurationValue(JsonElement Value, string? Operator = null)
{
	public string Operator { get; init; } = EventFilterOperators.Normalize(Operator);
}

public sealed record EventSubscription(
	EventTriggerOwner Owner,
	string TriggerId,
	string EventId,
	IReadOnlyDictionary<string, EventConfigurationValue> Configuration,
	JsonElement? Filter)
{
	public static readonly IReadOnlyDictionary<string, EventConfigurationValue> NoConfiguration
		= new Dictionary<string, EventConfigurationValue>(StringComparer.Ordinal);

	public EventTarget Target => new(Owner, TriggerId);
}
