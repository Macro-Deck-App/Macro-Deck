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
	private readonly EventTriggerContextResolver _contexts;
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
		_contexts = new EventTriggerContextResolver(folderCache, automationCache, registry, matcher, variableRenderer);
		_flowExecutor = flowExecutor;
		_logger = logger.ForContext<EventTriggerRunner>();
	}

	public async Task<FlowExecutionResult> Run(
		EventSubscription subscription,
		EventOccurrence occurrence,
		CancellationToken cancellationToken)
	{
		var source = _contexts.ResolveSource(subscription.Owner);
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

	private async Task<bool> ShouldRun(
		EventSubscription subscription,
		EventTriggerSource source,
		EventOccurrence occurrence)
	{
		if (occurrence.Target is not null)
		{
			return true;
		}

		return await _contexts.MatchesAsync(subscription, source, occurrence.EventId, occurrence.Parameters);
	}
}
