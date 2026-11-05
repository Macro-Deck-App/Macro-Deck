using System.Text.Json;
using MacroDeck.Sdk.Profiles;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Application.Profiles;

public sealed class VirtualWidgetPress
{
	private readonly Func<IProfileRegistry> _profiles;
	private readonly IUiTransport _transport;
	private readonly ILogger _logger;

	// Lazy: the profile registry reads the widget type registry, which is built from the widget providers
	// that depend on this.
	public VirtualWidgetPress(Func<IProfileRegistry> profiles, IUiTransport transport, ILogger logger)
	{
		_profiles = profiles;
		_transport = transport;
		_logger = logger;
	}

	public string? VirtualWidgetIdOf(UiSurface surface)
	{
		ArgumentNullException.ThrowIfNull(surface);

		return surface.Kind == UiSurfaceKinds.Widget
			&& surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.WidgetId, out var id)
			&& id.ValueKind == JsonValueKind.String
			&& id.GetString() is { } widgetId
			&& !Guid.TryParse(widgetId, out _)
			&& _profiles().FindVirtualWidget(widgetId) is not null
				? widgetId
				: null;
	}

	public void ReportLocked(string widgetId, string triggerType, string? originClientId)
	{
		if (string.IsNullOrEmpty(originClientId))
		{
			return;
		}

		_ = _transport.SendToGroup(UiClientGroups.For(originClientId),
			new ActionExecutionStatusEvent
			{
				ExecutionId = Guid.NewGuid().ToString(),
				Status = ActionExecutionStatus.Failed,
				WidgetId = widgetId,
				TriggerType = triggerType,
				Error = new TransportError
					{ Code = ActionExecutionErrorCodes.HostLocked, Message = AppStrings.Errors.Common.HostLocked() }
			});
	}

	public async Task RouteAsync(
		string widgetId,
		string triggerType,
		string? originClientId,
		CancellationToken cancellationToken)
	{
		TransportError? error = null;

		try
		{
			if (!await _profiles()
				.RouteWidgetInteraction(string.Empty, widgetId, new WidgetInteraction(triggerType))
				.ConfigureAwait(false))
			{
				error = new TransportError
					{ Code = "NOT_FOUND", Message = AppStrings.Errors.Widgets.ProviderDidNotHandle() };
			}
		}
		catch (Exception exception) when (exception is not (OutOfMemoryException or OperationCanceledException))
		{
			_logger.Warning(exception, "Routing a press on virtual widget {WidgetId} failed", widgetId);
			var (code, message) = ActionErrorSanitizer.Sanitize(exception);
			error = new TransportError { Code = code, Message = message };
		}

		if (error is null || string.IsNullOrEmpty(originClientId))
		{
			return;
		}

		await _transport.SendToGroup(UiClientGroups.For(originClientId),
				new ActionExecutionStatusEvent
				{
					ExecutionId = Guid.NewGuid().ToString(),
					Status = ActionExecutionStatus.Failed,
					WidgetId = widgetId,
					TriggerType = triggerType,
					Error = error
				},
				cancellationToken)
			.ConfigureAwait(false);
	}
}
