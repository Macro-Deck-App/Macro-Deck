using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Domain.Common;
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
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IHostLockState _lockState;
	private readonly ILogger _logger;

	public DeviceInteractionRouter(IServiceScopeFactory scopeFactory, IHostLockState lockState, ILogger logger)
	{
		_scopeFactory = scopeFactory;
		_lockState = lockState;
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
				return await ExecuteTriggerAsync(session, widgetId, WidgetTriggerTypes.ShortPress);
			case DeviceInteractionKind.LongPress:
				return await ExecuteTriggerAsync(session, widgetId, WidgetTriggerTypes.LongPress);
			default:
				return DeviceInteractionOutcome.NotSupported;
		}
	}

	/// <summary>
	/// Runs one widget trigger under the device's origin and reports what the host's pipeline made of it.
	/// A trigger that did not run comes back as a rejection rather than as an acceptance: a provider that
	/// is told "accepted" for a press that executed nothing has no way to learn otherwise.
	/// </summary>
	public async Task<DeviceInteractionOutcome> ExecuteTriggerAsync(
		DeviceSurfaceSession session,
		string widgetId,
		string triggerType)
	{
		// The folder the widget lives in, not the one on screen: a widget pinned into the current folder
		// is rendered here but is only addressable under its own folder.
		if (session.OwningFolderIdOf(widgetId) is not { } folderId)
		{
			return DeviceInteractionOutcome.Reject(DeviceSurfaceErrorCodes.WidgetNotOnSurface);
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

	private static DeviceSurfacePressTracker Presses(DeviceSurfaceSession session)
		=> session.Presses ?? throw new InvalidOperationException("The session has no press tracker.");
}
