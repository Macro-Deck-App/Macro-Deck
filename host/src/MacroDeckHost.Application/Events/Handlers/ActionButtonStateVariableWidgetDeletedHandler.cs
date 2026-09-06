using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class ActionButtonStateVariableWidgetDeletedHandler
	: INotificationHandler<WidgetDeletedNotification>
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly WidgetDerivedStateStore _derivedState;
	private readonly WidgetOptimisticStateStore? _optimisticStates;

	public ActionButtonStateVariableWidgetDeletedHandler(
		IServiceScopeFactory scopeFactory,
		WidgetDerivedStateStore derivedState,
		WidgetOptimisticStateStore? optimisticStates = null)
	{
		_scopeFactory = scopeFactory;
		_derivedState = derivedState;
		_optimisticStates = optimisticStates;
	}

	public async ValueTask Handle(WidgetDeletedNotification notification, CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var variables = scope.ServiceProvider.GetRequiredService<IVariableService>();

		// The whole scope instance, not just the host-managed toggle state: a variable the user or a
		// plugin scoped to this widget has nowhere left to be resolved from once the widget is gone.
		await variables.DeleteByScopeInstance(VariableScope.Widget, notification.WidgetId.ToString());
		_derivedState.Remove(notification.WidgetId);
		_optimisticStates?.ClearWidget(notification.WidgetId);
	}
}
