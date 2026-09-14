using System.Text.Json;
using MacroDeck.Sdk.Events;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// One entry of the <c>host.state</c> push for <see cref="HostApis.EventBindings" />. Mirrors the SDK's
/// <c>EventBinding</c>; the push is scoped to the receiving plugin's own events.
/// </summary>
public sealed record EventBindingDto
{
	/// <summary>Provider-local event id, unqualified.</summary>
	public required string EventId { get; init; }

	public IReadOnlyDictionary<string, EventBindingValueDto> Parameters { get; init; }
		= new Dictionary<string, EventBindingValueDto>(StringComparer.Ordinal);

	public EventBinding ToBinding()
		=> new()
		{
			EventId = EventId,
			Parameters = Parameters.ToDictionary(pair => pair.Key,
				pair => new EventBindingValue(pair.Value.Value, pair.Value.Operator),
				StringComparer.Ordinal)
		};
}

/// <summary>Mirrors the SDK's <c>EventBindingValue</c>. <see cref="Value" /> is absent for a state operator.</summary>
public sealed record EventBindingValueDto
{
	public JsonElement? Value { get; init; }

	public string Operator { get; init; } = "==";
}
