using System.Text.Json;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Widgets;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Devices.Surfaces;

/// <summary>
/// Resolves a reported interaction against the session's current surface and runs it through the
/// host's own trigger pipeline. Flow execution is never reimplemented here and never handed to the
/// provider: the same handler a client press goes through runs it, under the device's origin id.
/// </summary>
public sealed class DeviceInteractionRouter
{
	// A hardware press waits this long for a plugin tile's tree. Past it the press is absorbed rather than
	// running flows under a control that may be shown as disabled.
	private static readonly TimeSpan _treeDeadline = TimeSpan.FromSeconds(1);

	private static readonly string[] _shortPressPhases =
		[WidgetTriggerTypes.TouchStart, WidgetTriggerTypes.TouchEnd, WidgetTriggerTypes.ShortPress];

	private static readonly string[] _longPressPhases =
		[WidgetTriggerTypes.TouchStart, WidgetTriggerTypes.LongPress, WidgetTriggerTypes.TouchEnd];

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IHostLockState _lockState;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	public DeviceInteractionRouter(
		IServiceScopeFactory scopeFactory,
		IHostLockState lockState,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_scopeFactory = scopeFactory;
		_lockState = lockState;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<DeviceInteractionRouter>();
	}

	public async Task<DeviceInteractionOutcome> SubmitAsync(
		DeviceSurfaceSession session,
		DeviceInteraction interaction,
		CancellationToken cancellationToken)
	{
		if (_lockState.IsLocked)
		{
			return DeviceInteractionOutcome.Reject(DeviceSurfaceErrorCodes.HostLocked);
		}

		// A widget id the device is no longer rendering executes nothing, but never ends the session:
		// the device is expected to catch up on the next surface and press again.
		if (interaction.Target.WidgetId is not { Length: > 0 } widgetId ||
			session.LastPushed is not { } surface ||
			surface.Widgets.All(widget => !string.Equals(widget.Id, widgetId, StringComparison.Ordinal)))
		{
			return DeviceInteractionOutcome.Reject(DeviceSurfaceErrorCodes.WidgetNotOnSurface);
		}

		switch (interaction.Kind)
		{
			case DeviceInteractionKind.Press:
				await Presses(session).PressAsync(widgetId);
				return DeviceInteractionOutcome.Accepted;
			case DeviceInteractionKind.Release:
				await Presses(session).ReleaseAsync(widgetId);
				return DeviceInteractionOutcome.Accepted;
			case DeviceInteractionKind.ShortPress:
				return await ExecuteWholePressAsync(session, widgetId, WidgetTriggerTypes.ShortPress, _shortPressPhases);
			case DeviceInteractionKind.LongPress:
				return await ExecuteWholePressAsync(session, widgetId, WidgetTriggerTypes.LongPress, _longPressPhases);
			default:
				return DeviceInteractionOutcome.NotSupported;
		}
	}

	public async Task<DevicePressClaim> ClaimAsync(DeviceSurfaceSession session, string widgetId)
	{
		var type = session.LastPushed?.Widgets
			.FirstOrDefault(widget => string.Equals(widget.Id, widgetId, StringComparison.Ordinal))?.Type;
		if (type is null || WidgetTypeIds.BuiltIn.Contains(type, StringComparer.Ordinal))
		{
			return DevicePressClaim.None;
		}

		IUiSessionBroker? broker = null;
		string? sessionId = null;
		try
		{
			await using var scope = _scopeFactory.CreateAsyncScope();
			broker = scope.ServiceProvider.GetRequiredService<IUiSessionBroker>();

			// A principal of its own, so the opener never hands back a session a client or another press holds.
			var ticket = scope.ServiceProvider.GetRequiredService<IWidgetUiSessionOpener>()
				.Open(new OpenWidgetUiSessionRequest { WidgetId = widgetId },
					$"device:{session.DeviceId:N}:{Guid.NewGuid():N}",
					isAdmin: false);

			if (!ticket.Accepted)
			{
				// No provider at all means no tree to answer with, which is what a client falls back from too.
				return ticket.Code == UiSessionErrorCodes.ProviderUnavailable
					? DevicePressClaim.None
					: DevicePressClaim.Absorbing(null, null);
			}

			sessionId = ticket.SessionId;
			using var deadline = new CancellationTokenSource(_treeDeadline, _timeProvider);
			if (await broker.FirstTreeAsync(sessionId, deadline.Token) is { } tree)
			{
				return DevicePressClaim.From(broker, sessionId, JsonDocument.Parse(tree.Utf8));
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning(exception,
				"The tree of widget {WidgetId} did not answer device {DeviceId}'s press; it is absorbed",
				widgetId,
				session.DeviceId);
		}

		return DevicePressClaim.Absorbing(broker, sessionId);
	}

	/// <summary>
	/// Runs one widget trigger under the device's origin and reports what the host's pipeline made of it.
	/// A trigger that did not run comes back as a rejection rather than as an acceptance: a provider that
	/// is told "accepted" for a press that executed nothing has no way to learn otherwise.
	/// </summary>
	public async Task<DeviceInteractionOutcome> ExecuteTriggerAsync(
		DeviceSurfaceSession session,
		string widgetId,
		string triggerType,
		DevicePressClaim claim)
	{
		// The folder the widget lives in, not the one on screen: a widget pinned into the current folder
		// is rendered here but is only addressable under its own folder.
		if (session.OwningFolderIdOf(widgetId) is not { } folderId)
		{
			return DeviceInteractionOutcome.Reject(DeviceSurfaceErrorCodes.WidgetNotOnSurface);
		}

		if (claim.TakesPress)
		{
			if (_lockState.IsLocked)
			{
				return DeviceInteractionOutcome.Reject(DeviceSurfaceErrorCodes.HostLocked);
			}

			claim.Dispatch(triggerType);
			return DeviceInteractionOutcome.Accepted;
		}

		try
		{
			await using var scope = _scopeFactory.CreateAsyncScope();
			var handler = scope.ServiceProvider
				.GetRequiredService<IUiTransportMessageHandler<ExecuteActionButtonTriggerRequest,
					ExecuteActionButtonTriggerResponse>>();

			var response = await handler.Handle(new ExecuteActionButtonTriggerRequest
				{
					WidgetId = widgetId,
					FolderId = folderId,
					TriggerType = triggerType,
					OriginDeviceId = session.DeviceId
				},
				CancellationToken.None);

			if (response.Success)
			{
				return DeviceInteractionOutcome.Accepted;
			}

			_logger.Debug("Trigger {TriggerType} on widget {WidgetId} for device {DeviceId} did not succeed: {Code}",
				triggerType,
				widgetId,
				session.DeviceId,
				response.Error?.Code);

			return DeviceInteractionOutcome.Reject(DeviceSurfaceErrorCodes.TriggerFailed);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Error(exception,
				"Failed to run trigger {TriggerType} on widget {WidgetId} for device {DeviceId}",
				triggerType,
				widgetId,
				session.DeviceId);

			return DeviceInteractionOutcome.Reject(DeviceSurfaceErrorCodes.TriggerFailed);
		}
	}

	private async Task<DeviceInteractionOutcome> ExecuteWholePressAsync(
		DeviceSurfaceSession session,
		string widgetId,
		string triggerType,
		string[] phases)
	{
		var pending = ClaimAsync(session, widgetId);
		if (pending.IsCompleted)
		{
			return await FinishWholePressAsync(session, widgetId, triggerType, phases, pending);
		}

		// Never awaited on the report: the tile's tree may arrive on the same serial plugin connection.
		_ = FinishWholePressAsync(session, widgetId, triggerType, phases, pending);
		return DeviceInteractionOutcome.Accepted;
	}

	private async Task<DeviceInteractionOutcome> FinishWholePressAsync(
		DeviceSurfaceSession session,
		string widgetId,
		string triggerType,
		string[] phases,
		Task<DevicePressClaim> pending)
	{
		await using var claim = await pending;
		if (_lockState.IsLocked)
		{
			return DeviceInteractionOutcome.Reject(DeviceSurfaceErrorCodes.HostLocked);
		}

		if (!claim.TakesPress || session.OwningFolderIdOf(widgetId) is null)
		{
			return await ExecuteTriggerAsync(session, widgetId, triggerType, claim);
		}

		foreach (var phase in phases)
		{
			claim.Dispatch(phase);
		}

		return DeviceInteractionOutcome.Accepted;
	}

	private static DeviceSurfacePressTracker Presses(DeviceSurfaceSession session)
		=> session.Presses ?? throw new InvalidOperationException("The session has no press tracker.");
}
