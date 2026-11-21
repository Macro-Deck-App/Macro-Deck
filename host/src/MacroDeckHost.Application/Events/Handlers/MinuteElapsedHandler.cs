using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Variables.Templates;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class MinuteElapsedHandler : INotificationHandler<MinuteElapsedNotification>
{
	private readonly TemplateVariableSynchronizer _templates;
	private readonly IWidgetVariableIndex _index;
	private readonly LabelRenderChannel _labelRenders;
	private readonly LabelSubscriptionTracker _subscriptions;
	private readonly WidgetStateEvalChannel _stateEvaluations;
	private readonly IWidgetRenderSignals _renderSignals;
	private readonly IVariableChangeNotifier _variables;

	public MinuteElapsedHandler(
		TemplateVariableSynchronizer templates,
		IWidgetVariableIndex index,
		LabelRenderChannel labelRenders,
		LabelSubscriptionTracker subscriptions,
		WidgetStateEvalChannel stateEvaluations,
		IWidgetRenderSignals renderSignals,
		IVariableChangeNotifier variables)
	{
		_templates = templates;
		_index = index;
		_labelRenders = labelRenders;
		_subscriptions = subscriptions;
		_stateEvaluations = stateEvaluations;
		_renderSignals = renderSignals;
		_variables = variables;
	}

	public ValueTask Handle(MinuteElapsedNotification notification, CancellationToken cancellationToken)
	{
		_templates.NoteMinuteElapsed();

		foreach (var widgetId in _index.FindLabelReferences(TemplateClock.Name))
		{
			if (_subscriptions.HasAnySubscribers(widgetId.ToString()))
			{
				_labelRenders.EnqueueIfChanged(widgetId);
			}
		}

		foreach (var widgetId in _index.FindStateMappingReferences(TemplateClock.Name))
		{
			_stateEvaluations.Enqueue(widgetId);
		}

		_renderSignals.RaiseVariableChanged();
		_variables.Publish(TemplateClock.Name);
		return ValueTask.CompletedTask;
	}
}
