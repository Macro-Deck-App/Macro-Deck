using MacroDeck.Localization;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

namespace MacroDeckHost.Application.Rendering;

/// <summary>
/// The single place a reconciliation result turns into a client push. <see cref="WidgetStateReconciler" />
/// calls this on every reconcile, right after committing the state and before any <c>onStateChange</c>
/// flow runs, so a client can see what that flow paints while it runs rather than only its end result
/// (issue #678). Whether a call actually pushes is decided here, not by the caller.
/// </summary>
public interface IWidgetStatePublisher
{
	Task PublishIfChanged(Guid widgetId,
		WidgetStateReconciliation result,
		CancellationToken cancellationToken = default);
}

public sealed class WidgetStatePublisher : IWidgetStatePublisher
{
	private readonly IUiTransport _uiTransport;
	private readonly WidgetStateSubscriptionTracker _subscriptions;
	private readonly IWidgetRenderSignals _renderSignals;
	private readonly WidgetOptimisticStateStore? _optimisticStates;
	private readonly IDeviceSurfaceRenderSink? _deviceSurfaces;

	public WidgetStatePublisher(
		IUiTransport uiTransport,
		WidgetStateSubscriptionTracker subscriptions,
		IWidgetRenderSignals renderSignals,
		WidgetOptimisticStateStore? optimisticStates = null,
		IDeviceSurfaceRenderSink? deviceSurfaces = null)
	{
		_uiTransport = uiTransport;
		_subscriptions = subscriptions;
		_renderSignals = renderSignals;
		_optimisticStates = optimisticStates;
		_deviceSurfaces = deviceSurfaces;
	}

	public async Task PublishIfChanged(Guid widgetId,
		WidgetStateReconciliation result,
		CancellationToken cancellationToken = default)
	{
		if (result.OptimisticState is { } optimisticState &&
			_optimisticStates?.IsCurrent(optimisticState) == false)
		{
			return;
		}

		// A provider that renames a state or changes which states it offers moves the set without
		// moving the active id, so gating on Changed alone would leave every live deck rendering a
		// stale set - and a stale label for the state it is already showing - until it reconnected.
		if (result.StateId is null ||
			(!result.Changed && !result.SetChanged) ||
			!_subscriptions.HasSubscribers(widgetId.ToString()))
		{
			return;
		}

		var id = widgetId.ToString();
		var evt = new WidgetStateUpdatedEvent
		{
			WidgetId = id,
			StateId = result.StateId,
			StateLabel = result.StateLabel.IsEmpty ? LocalizedText.FromLiteral(result.StateId) : result.StateLabel,
			States = result.SetChanged ? result.States.ToList() : null
		};

		// Raised beside the client push, carrying the exact values it computed, so an open session for
		// this widget can never disagree with what a legacy client is pushed at the same moment.
		_renderSignals.RaiseStateChanged(evt);

		await _uiTransport.SendToGroup(WidgetStateGroups.For(id), evt, cancellationToken);

		if (_deviceSurfaces is not null)
		{
			await _deviceSurfaces.OnWidgetStateChanged(widgetId, cancellationToken);
		}
	}
}
