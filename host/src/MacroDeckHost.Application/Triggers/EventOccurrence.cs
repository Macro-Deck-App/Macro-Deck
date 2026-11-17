using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Triggers;

public sealed record EventOccurrence(
	string EventId,
	IReadOnlyDictionary<string, object?> Parameters,
	EventTarget? Target = null,
	Guid? McpTokenId = null,
	int CascadeDepth = 0,
	EventVariableSource? Variable = null)
{
	public static readonly IReadOnlyDictionary<string, object?> NoParameters
		= new Dictionary<string, object?>(StringComparer.Ordinal);
}

public readonly record struct EventTarget(EventTriggerOwner Owner, string TriggerId);

public readonly record struct EventVariableSource(VariableScope Scope, string? ScopeRefId)
{
	public static EventVariableSource Of(VariableEntity variable)
	{
		return new EventVariableSource(variable.Scope, variable.ScopeRefId);
	}
}
