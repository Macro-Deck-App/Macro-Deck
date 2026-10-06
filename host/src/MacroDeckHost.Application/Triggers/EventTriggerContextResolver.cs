using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Triggers;

public readonly record struct EventTriggerSource(
	string? FlowsSource,
	VariableScope Scope,
	string? ScopeRefId,
	Guid? OwnerWidgetId);

public interface IEventTriggerContextResolver
{
	EventTriggerSource? ResolveSource(EventTriggerOwner owner);

	Task<bool> MatchesAsync(
		EventSubscription subscription,
		EventTriggerSource source,
		string eventId,
		IReadOnlyDictionary<string, object?> parameters);
}

public sealed class EventTriggerContextResolver : IEventTriggerContextResolver
{
	private readonly IFolderCache _folderCache;
	private readonly IAutomationCache _automationCache;
	private readonly IEventRegistry _registry;
	private readonly IEventSubscriptionMatcher _matcher;
	private readonly IVariableTemplateRenderer _variableRenderer;

	public EventTriggerContextResolver(
		IFolderCache folderCache,
		IAutomationCache automationCache,
		IEventRegistry registry,
		IEventSubscriptionMatcher matcher,
		IVariableTemplateRenderer variableRenderer)
	{
		_folderCache = folderCache;
		_automationCache = automationCache;
		_registry = registry;
		_matcher = matcher;
		_variableRenderer = variableRenderer;
	}

	public EventTriggerSource? ResolveSource(EventTriggerOwner owner)
	{
		switch (owner.Kind)
		{
			case EventTriggerOwnerKind.Widget:
			{
				var widget = _folderCache.GetAllFolders()
					.SelectMany(folder => folder.Widgets)
					.FirstOrDefault(w => w.Id == owner.Id);

				return widget is null
					? null
					: new EventTriggerSource(widget.Data, VariableScope.Widget, widget.Id.ToString(), widget.Id);
			}

			case EventTriggerOwnerKind.Automation:
			{
				var automation = _automationCache.GetById(owner.Id);
				return automation is null || !automation.Enabled
					? null
					: new EventTriggerSource(WidgetFlowsJson.ToSource(automation.Flows),
						VariableScope.Global,
						null,
						null);
			}

			default:
				return null;
		}
	}

	public async Task<bool> MatchesAsync(
		EventSubscription subscription,
		EventTriggerSource source,
		string eventId,
		IReadOnlyDictionary<string, object?> parameters)
	{
		var descriptor = _registry.Find(eventId);
		if (descriptor is null)
		{
			return false;
		}

		var context = await _variableRenderer.CreateContextAsync(source.Scope, source.ScopeRefId);

		return _matcher.Matches(subscription, descriptor, context.WithEvent(parameters));
	}
}
