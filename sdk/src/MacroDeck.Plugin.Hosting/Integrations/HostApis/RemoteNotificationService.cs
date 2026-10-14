using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.Ui;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Ui.Model.Serialization;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

internal sealed class RemoteNotificationService : INotificationService, IDisposable
{
	private readonly IHostInvoker _invoker;
	private readonly PluginConnectionState _state;
	private readonly NotificationResultStore _results;

	public RemoteNotificationService(
		IHostInvoker invoker,
		PluginConnectionState state,
		NotificationResultStore results)
	{
		_invoker = invoker;
		_state = state;
		_results = results;
		_state.Connected += OnConnected;
	}

	public void Dispose()
	{
		_state.Connected -= OnConnected;
	}

	public async Task<INotificationHandle> ShowAsync(
		NotificationOptions options,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(options);

		var data = await InvokeAsync(HostOperations.Notifications.Show, ToArguments(options), cancellationToken)
			.ConfigureAwait(false);
		var shown = data?.Deserialize<NotificationsShowResult>(PluginProtocolJson.Options)
			?? throw new NotificationException(NotificationErrorCode.Invalid,
				"The host did not return a notification id.");

		return new Handle(this, shown.Id, _results.Register(shown.Id));
	}

	public async Task<NotificationResult> ShowForResultAsync(
		NotificationOptions options,
		CancellationToken cancellationToken = default)
	{
		var handle = await ShowAsync(options, cancellationToken).ConfigureAwait(false);

		try
		{
			return await handle.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			await DismissQuietlyAsync(handle).ConfigureAwait(false);
			throw;
		}
	}

	private void OnConnected(object? sender, PluginConnectedEventArgs e)
	{
		// A new session means the host lost every notification of the old one, so nothing will answer them.
		if (!e.Resumed)
		{
			_results.CancelAll(NotificationCloseReason.HostUnavailable);
		}
	}

	private static async Task DismissQuietlyAsync(INotificationHandle handle)
	{
		try
		{
			await handle.DismissAsync(CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is NotificationException or HostInvocationException)
		{
		}
	}

	private async Task<JsonElement?> InvokeAsync(
		string operation,
		object arguments,
		CancellationToken cancellationToken)
	{
		try
		{
			return await _invoker.InvokeAsync(Protocol.Callbacks.HostApis.Notifications,
					operation,
					arguments,
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch (HostInvocationException exception)
		{
			throw new NotificationException(MapCode(exception.Code), exception.Message);
		}
	}

	private static NotificationErrorCode MapCode(string code)
	{
		return code switch
		{
			ProtocolErrorCodes.CapabilityUnsupported => NotificationErrorCode.Unsupported,
			ProtocolErrorCodes.InvalidPayload => NotificationErrorCode.Invalid,
			ProtocolErrorCodes.CorrelationUnknown => NotificationErrorCode.NotFound,
			ProtocolErrorCodes.RateLimited or ProtocolErrorCodes.QueueOverflow or ProtocolErrorCodes.PayloadTooLarge
				=> NotificationErrorCode.LimitExceeded,
			_ => NotificationErrorCode.Invalid
		};
	}

	private static NotificationsShowArguments ToArguments(NotificationOptions options)
	{
		return new NotificationsShowArguments
		{
			Title = options.Title,
			Message = options.Message,
			Severity = SeverityToWire(options.Severity),
			Popup = options.Popup switch
			{
				NotificationPopup.None => NotificationsWire.Popup.None,
				NotificationPopup.Dialog => NotificationsWire.Popup.Dialog,
				_ => NotificationsWire.Popup.Toast
			},
			Inbox = options.Inbox,
			DurationMs = options.Duration is { } duration ? (long)duration.TotalMilliseconds : null,
			Dismissible = options.Dismissible,
			TargetKind = options.Target.Kind switch
			{
				NotificationTargetKind.AdminUi => NotificationsWire.TargetKind.AdminUi,
				NotificationTargetKind.Devices => NotificationsWire.TargetKind.Devices,
				_ => NotificationsWire.TargetKind.All
			},
			TargetDeviceIds = options.Target.Kind == NotificationTargetKind.Devices ? options.Target.DeviceIds : null,
			Actions = ActionsToWire(options.Actions),
			Content = options.Content is { } content ? UiCanonicalJson.ToElement(content) : null,
			Progress = ProgressToWire(options.Progress)
		};
	}

	private static NotificationsUpdateArguments ToArguments(Guid id, NotificationUpdate update)
	{
		return new NotificationsUpdateArguments
		{
			Id = id,
			Title = update.Title,
			Message = update.Message,
			Severity = update.Severity is { } severity ? SeverityToWire(severity) : null,
			Progress = ProgressToWire(update.Progress),
			Actions = update.Actions is null ? null : ActionsToWire(update.Actions),
			Content = update.Content is { } content ? UiCanonicalJson.ToElement(content) : null
		};
	}

	private static string SeverityToWire(NotificationSeverity severity)
	{
		return severity switch
		{
			NotificationSeverity.Success => NotificationsWire.Severity.Success,
			NotificationSeverity.Warning => NotificationsWire.Severity.Warning,
			NotificationSeverity.Error => NotificationsWire.Severity.Error,
			_ => NotificationsWire.Severity.Info
		};
	}

	private static IReadOnlyList<NotificationsActionDto>? ActionsToWire(IReadOnlyList<NotificationAction> actions)
	{
		return actions.Count == 0
			? []
			:
			[
				.. actions.Select(action => new NotificationsActionDto
				{
					Id = action.Id,
					Label = action.Label,
					Style = action.Style switch
					{
						NotificationActionStyle.Primary => NotificationsWire.ActionStyle.Primary,
						NotificationActionStyle.Danger => NotificationsWire.ActionStyle.Danger,
						_ => NotificationsWire.ActionStyle.Default
					}
				})
			];
	}

	private static NotificationsProgressDto? ProgressToWire(NotificationProgress? progress)
	{
		return progress is null
			? null
			: new NotificationsProgressDto { Processed = progress.Processed, Total = progress.Total };
	}

	private sealed class Handle(RemoteNotificationService owner, Guid id, Task<NotificationResult> completion)
		: INotificationHandle
	{
		public Guid Id { get; } = id;

		public Task<NotificationResult> Completion { get; } = completion;

		public async Task UpdateAsync(NotificationUpdate update, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(update);

			await owner.InvokeAsync(HostOperations.Notifications.Update, ToArguments(Id, update), cancellationToken)
				.ConfigureAwait(false);
		}

		public async Task DismissAsync(CancellationToken cancellationToken = default)
		{
			try
			{
				await owner.InvokeAsync(HostOperations.Notifications.Close,
						new NotificationsCloseArguments { Id = Id },
						cancellationToken)
					.ConfigureAwait(false);
			}
			catch (NotificationException exception) when (exception.ErrorCode == NotificationErrorCode.NotFound)
			{
			}
		}
	}
}
