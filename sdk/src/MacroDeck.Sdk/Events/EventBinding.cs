using System.Text.Json;

namespace MacroDeck.Sdk.Events;

/// <summary>
/// One trigger a user bound to one of this integration's events: a widget flow or an enabled
/// automation, with the values authored on its configuration parameters.
/// </summary>
public sealed class EventBinding
{
	/// <summary>Provider-local event id, without the <c>providerId::</c> prefix.</summary>
	public required string EventId { get; init; }

	/// <summary>
	/// The configured values keyed by parameter name. Only parameters the user set are present; the
	/// binding may additionally carry a filter the host evaluates on its own, so a matching value does
	/// not guarantee the trigger fires.
	/// </summary>
	public IReadOnlyDictionary<string, EventBindingValue> Parameters { get; init; }
		= new Dictionary<string, EventBindingValue>(StringComparer.Ordinal);
}

/// <summary>A configured value and the comparison the trigger applies to it.</summary>
/// <param name="Value">
/// The value exactly as the editor stored it: a scalar, a structured value such as a keyboard combo
/// object, or a variable reference such as <c>{"$var":"scene"}</c>. A reference is never resolved here;
/// read <see cref="ResolvedValue" /> for what it currently holds. <c>null</c> for the state operators,
/// which carry no value.
/// </param>
/// <param name="Operator">
/// <c>==</c>, <c>!=</c>, <c>&gt;</c>, <c>&lt;</c>, <c>&gt;=</c>, <c>&lt;=</c>, or one of the state
/// operators <c>isEmpty</c>, <c>isNotEmpty</c>, <c>isAvailable</c>, <c>isNotAvailable</c>.
/// </param>
public sealed record EventBindingValue(JsonElement? Value, string Operator)
{
	/// <summary>
	/// What a <c>$var</c> reference in <see cref="Value" /> currently resolves to, in the shape the host
	/// compares against when it matches an occurrence (a Numeric variable arrives as its stored text). Seen
	/// from the trigger's own widget or automation, so a scoped variable resolves as it does there.
	/// <c>null</c> when <see cref="Value" /> is not a <c>$var</c> reference (a literal, a template, an
	/// <c>$event</c> reference), when the variable does not exist or has no value, and on a host that predates
	/// this property. Read <c>ResolvedValue ?? Value</c> to get the effective value of either kind.
	/// </summary>
	public JsonElement? ResolvedValue { get; init; }
}
