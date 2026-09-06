using MacroDeckHost.Application.Actions;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Application.Widgets;

/// <summary>
/// The widget-flow half of running an Action Button trigger: the implicit short-press state advance
/// followed by the bounded flow execution. Extracted from
/// <c>ExecuteActionButtonTriggerRequestMessageHandler</c> so a UI session dispatch (which never goes
/// through that transport message) and the REST/WebSocket handler share exactly one implementation of
/// this behaviour.
/// </summary>
public interface IWidgetTriggerService
{
	/// <summary>
	/// Advances the widget's active state first when <paramref name="triggerType" /> is
	/// <see cref="WidgetTriggerTypes.ShortPress" /> and the button has not turned cycling off, then runs
	/// the widget's flow for that trigger, bounded to 5 seconds. The advance never links to
	/// <paramref name="cancellationToken" /> and never escapes - a failure is logged and the flow still
	/// runs - so a press is always dispatched even if the state write threw.
	/// </summary>
	Task<ActionExecutionDispatch> ExecuteAsync(
		WidgetEntity widget,
		string triggerType,
		string? originClientId,
		Guid? originDeviceId,
		CancellationToken cancellationToken);
}

/// <summary>
/// Registered as a singleton - unlike its scoped <see cref="IActionButtonStateService" /> collaborator -
/// so a singleton UI session provider can depend on it directly, the same way
/// <see cref="WidgetIconResources" /> bridges a scoped service for its own singleton callers: a scope is
/// opened per <see cref="ExecuteAsync" /> call rather than the scoped service being captured.
/// </summary>
public sealed class WidgetTriggerService : IWidgetTriggerService
{
	// Matches the 5s bound the REST/WebSocket handler enforced before this extraction - a press must not
	// hang a client indefinitely on a stuck flow.
	private static readonly TimeSpan _pressBound = TimeSpan.FromSeconds(5);

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IActionExecutionCoordinator _coordinator;
	private readonly ILogger _logger;

	public WidgetTriggerService(
		IServiceScopeFactory scopeFactory,
		IActionExecutionCoordinator coordinator,
		ILogger logger)
	{
		_scopeFactory = scopeFactory;
		_coordinator = coordinator;
		_logger = logger.ForContext<WidgetTriggerService>();
	}

	public async Task<ActionExecutionDispatch> ExecuteAsync(
		WidgetEntity widget,
		string triggerType,
		string? originClientId,
		Guid? originDeviceId,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(widget);
		ArgumentException.ThrowIfNullOrWhiteSpace(triggerType);

		if (string.Equals(triggerType, WidgetTriggerTypes.ShortPress, StringComparison.OrdinalIgnoreCase) &&
			CyclesStatesOnPress(widget))
		{
			// Advance before the flow runs so the flow's own vars.state and any onStateChange handler
			// observe the state this press just switched to. Never linked to the caller's token and never
			// allowed to escape: a press must still be dispatched even if the state write threw.
			try
			{
				using var scope = _scopeFactory.CreateScope();
				var stateService = scope.ServiceProvider.GetRequiredService<IActionButtonStateService>();

				var advance = await stateService.AdvanceAsync(widget.Id, CancellationToken.None).ConfigureAwait(false);

				if (!advance.Success)
				{
					_logger.Warning("Implicit state advance for widget {WidgetId} did not succeed: {Error}",
						widget.Id,
						advance.Error);
				}
			}
			catch (Exception ex)
			{
				_logger.Warning(ex, "Implicit state advance for widget {WidgetId} threw", widget.Id);
			}
		}

		return await _coordinator.RunBoundedAsync(new FlowExecutionRequest
			{
				FlowsSource = widget.Data,
				Trigger = TriggerSelector.ByType(triggerType),
				Scope = VariableScope.Widget,
				ScopeRefId = widget.Id.ToString(),
				OwnerWidgetId = widget.Id,
				OriginClientId = originClientId,
				OriginDeviceId = originDeviceId
			},
			_pressBound,
			cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Whether this press is the button's own way of stepping to its next state. Mirrors
	/// <c>ActionButtonWidgetData.CanAdvanceState</c>, which decides whether the tile declares a press
	/// event at all - the provider/mapping cases are still refused by
	/// <see cref="IActionButtonStateService.AdvanceAsync" /> either way.
	/// </summary>
	private static bool CyclesStatesOnPress(WidgetEntity widget)
	{
		if (widget.Type != WidgetTypeIds.ActionButton)
		{
			return false;
		}

		var model = ActionButtonStateModel.Read(widget.Data);
		return model.StateMode && model.CycleStatesOnPress;
	}
}
