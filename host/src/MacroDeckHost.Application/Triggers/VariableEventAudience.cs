using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Triggers;

public interface IVariableEventAudience
{
	bool Reaches(EventTriggerOwner owner, EventOccurrence occurrence);
}

public sealed class VariableEventAudience : IVariableEventAudience
{
	private readonly VariableRegistry _registry;

	public VariableEventAudience(VariableRegistry registry)
	{
		_registry = registry;
	}

	public bool Reaches(EventTriggerOwner owner, EventOccurrence occurrence)
	{
		if (occurrence.Variable is not { } source
			|| !occurrence.Parameters.TryGetValue("variable", out var value)
			|| value is not string name)
		{
			return true;
		}

		var listeningWidget = owner.Kind == EventTriggerOwnerKind.Widget ? owner.Id.ToString() : null;
		var shadowedByListener = listeningWidget is not null
			&& _registry.FindByName(VariableScope.Widget, listeningWidget, name) is not null;

		return source.Scope switch
		{
			VariableScope.Global => !shadowedByListener,
			VariableScope.Widget when IsWidget(source.ScopeRefId, owner) => true,
			VariableScope.Widget => !shadowedByListener
				&& _registry.FindByName(VariableScope.Global, null, name) is null,
			_ => false
		};
	}

	private static bool IsWidget(string? scopeRefId, EventTriggerOwner owner)
	{
		return owner.Kind == EventTriggerOwnerKind.Widget
			&& Guid.TryParse(scopeRefId, out var widgetId)
			&& widgetId == owner.Id;
	}
}
