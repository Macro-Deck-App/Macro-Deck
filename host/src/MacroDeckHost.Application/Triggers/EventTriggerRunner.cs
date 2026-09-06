using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using Serilog;

namespace MacroDeckHost.Application.Triggers;

public interface IEventTriggerRunner
{
	Task<FlowExecutionResult> Run(
		EventSubscription subscription,
		EventOccurrence occurrence,
		CancellationToken cancellationToken);
}

public sealed class EventTriggerRunner : IEventTriggerRunner
{
	private readonly IFolderCache _folderCache;
	private readonly IAutomationCache _automationCache;
	private readonly IEventRegistry _registry;
	private readonly IEventSubscriptionMatcher _matcher;
	private readonly IVariableTemplateRenderer _variableRenderer;
	private readonly IFlowExecutor _flowExecutor;
	private readonly ILogger _logger;

	public EventTriggerRunner(
		IFolderCache folderCache,
		IAutomationCache automationCache,
		IEventRegistry registry,
		IEventSubscriptionMatcher matcher,
		IVariableTemplateRenderer variableRenderer,
		IFlowExecutor flowExecutor,
		ILogger logger)
	{
		_folderCache = folderCache;
		_automationCache = automationCache;
		_registry = registry;
		_matcher = matcher;
		_variableRenderer = variableRenderer;
		_flowExecutor = flowExecutor;
		_logger = logger.ForContext<EventTriggerRunner>();
	}

	public async Task<FlowExecutionResult> Run(
		EventSubscription subscription,
		EventOccurrence occurrence,
		CancellationToken cancellationToken)
	{
		var source = ResolveSource(subscription.Owner);
		if (source is null)
		{
			return NotRun();
		}

		if (!await ShouldRun(subscription, source.Value, occurrence))
		{
			return NotRun();
		}

		_logger.Debug("Running event trigger {TriggerId} on {Owner} for {EventId}",
			subscription.TriggerId,
			subscription.Owner,
			occurrence.EventId);

		var result = await _flowExecutor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = source.Value.FlowsSource,
				Trigger = TriggerSelector.ById(subscription.TriggerId),
				Scope = source.Value.Scope,
				ScopeRefId = source.Value.ScopeRefId,
				OwnerWidgetId = source.Value.OwnerWidgetId,
				EventParameters = occurrence.Parameters,
				Origin = ExecutionOrigin.Host
			},
			cancellationToken);

		if (result.Status != FlowExecutionStatus.Succeeded)
		{
			_logger.Warning("Event trigger {TriggerId} on {Owner} for {EventId} finished as {Status} " +
				"(execution {ExecutionId}): {ErrorCode} {ErrorMessage}",
				subscription.TriggerId,
				subscription.Owner,
				occurrence.EventId,
				result.Status,
				result.ExecutionId,
				result.ErrorCode,
				// A LocalizedText would be destructured into its parts by Serilog; a log line wants the
				// diagnostic rendering instead.
				result.ErrorMessage.ToString());
		}

		return result;
	}

	private static FlowExecutionResult NotRun() => new()
	{
		ExecutionId = Guid.NewGuid(),
		Status = FlowExecutionStatus.Succeeded,
		MatchedFlows = 0
	};

	private FlowSource? ResolveSource(EventTriggerOwner owner)
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
					: new FlowSource(widget.Data, VariableScope.Widget, widget.Id.ToString(), widget.Id);
			}

			case EventTriggerOwnerKind.Automation:
			{
				var automation = _automationCache.GetById(owner.Id);
				return automation is null || !automation.Enabled
					? null
					: new FlowSource(WidgetFlowsJson.ToSource(automation.Flows), VariableScope.Global, null, null);
			}

			default:
				return null;
		}
	}

	private async Task<bool> ShouldRun(
		EventSubscription subscription,
		FlowSource source,
		EventOccurrence occurrence)
	{
		if (occurrence.Target is not null)
		{
			return true;
		}

		var descriptor = _registry.Find(occurrence.EventId);
		if (descriptor is null)
		{
			return false;
		}

		var context = await _variableRenderer.CreateContextAsync(source.Scope, source.ScopeRefId);

		return _matcher.Matches(subscription, descriptor, context.WithEvent(occurrence.Parameters));
	}

	private readonly record struct FlowSource(
		string? FlowsSource,
		VariableScope Scope,
		string? ScopeRefId,
		Guid? OwnerWidgetId);
}
