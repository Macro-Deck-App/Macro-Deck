using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class ActionButtonStateVariableWidgetsDeletedHandler
	: INotificationHandler<WidgetsDeletedNotification>
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly WidgetDerivedStateStore _derivedState;
	private readonly WidgetOptimisticStateStore? _optimisticStates;

	public ActionButtonStateVariableWidgetsDeletedHandler(
		IServiceScopeFactory scopeFactory,
		WidgetDerivedStateStore derivedState,
		WidgetOptimisticStateStore? optimisticStates = null)
	{
		_scopeFactory = scopeFactory;
		_derivedState = derivedState;
		_optimisticStates = optimisticStates;
	}

	public async ValueTask Handle(WidgetsDeletedNotification notification, CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var variables = scope.ServiceProvider.GetRequiredService<IVariableService>();

		foreach (var widgetId in notification.WidgetIds)
		{
			// See the single-widget handler: the widget's whole variable scope goes with it.
			await variables.DeleteByScopeInstance(VariableScope.Widget, widgetId.ToString());
			_derivedState.Remove(widgetId);
			_optimisticStates?.ClearWidget(widgetId);
		}
	}
}
