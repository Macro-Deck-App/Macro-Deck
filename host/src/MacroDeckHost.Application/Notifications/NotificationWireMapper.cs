using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Serialization;

namespace MacroDeckHost.Application.Notifications;

public static class NotificationWireMapper
{
	public static NotificationOptions ToOptions(NotificationsShowArguments arguments)
	{
		return new NotificationOptions
		{
			Title = arguments.Title,
			Message = arguments.Message,
			Severity = ParseSeverity(arguments.Severity) ?? NotificationSeverity.Info,
			Popup = arguments.Popup switch
			{
				null or NotificationsWire.Popup.Toast => NotificationPopup.Toast,
				NotificationsWire.Popup.Dialog => NotificationPopup.Dialog,
				NotificationsWire.Popup.None => NotificationPopup.None,
				_ => throw Invalid("The popup kind is not known.")
			},
			Inbox = arguments.Inbox,
			Duration = arguments.DurationMs is { } ms
				? TimeSpan.FromMilliseconds(Math.Clamp(ms, 0, (long)TimeSpan.FromDays(1).TotalMilliseconds))
				: null,
			Dismissible = arguments.Dismissible,
			Target = arguments.TargetKind switch
			{
				null or NotificationsWire.TargetKind.All => NotificationTarget.All,
				NotificationsWire.TargetKind.AdminUi => NotificationTarget.AdminUi,
				NotificationsWire.TargetKind.Devices =>
					NotificationTarget.Devices([.. arguments.TargetDeviceIds ?? []]),
				_ => throw Invalid("The target kind is not known.")
			},
			Actions = ToActions(arguments.Actions) ?? [],
			Content = ToContent(arguments.Content),
			Progress = ToProgress(arguments.Progress)
		};
	}

	public static NotificationUpdate ToUpdate(NotificationsUpdateArguments arguments)
	{
		return new NotificationUpdate
		{
			Title = arguments.Title,
			Message = arguments.Message,
			Severity = ParseSeverity(arguments.Severity),
			Progress = ToProgress(arguments.Progress),
			Actions = ToActions(arguments.Actions),
			Content = ToContent(arguments.Content)
		};
	}

	public static string ReasonToWire(NotificationCloseReason reason)
	{
		return reason switch
		{
			NotificationCloseReason.Action => NotificationsWire.CloseReason.Action,
			NotificationCloseReason.Timeout => NotificationsWire.CloseReason.Timeout,
			NotificationCloseReason.Programmatic => NotificationsWire.CloseReason.Programmatic,
			NotificationCloseReason.OwnerGone => NotificationsWire.CloseReason.OwnerGone,
			NotificationCloseReason.HostUnavailable => NotificationsWire.CloseReason.HostUnavailable,
			_ => NotificationsWire.CloseReason.Dismissed
		};
	}

	private static NotificationSeverity? ParseSeverity(string? severity)
	{
		return severity switch
		{
			NotificationsWire.Severity.Success => NotificationSeverity.Success,
			NotificationsWire.Severity.Warning => NotificationSeverity.Warning,
			NotificationsWire.Severity.Error => NotificationSeverity.Error,
			NotificationsWire.Severity.Info => NotificationSeverity.Info,
			_ => null
		};
	}

	private static IReadOnlyList<NotificationAction>? ToActions(IReadOnlyList<NotificationsActionDto>? actions)
	{
		return actions is null
			? null
			:
			[
				.. actions.Select(action => new NotificationAction
				{
					Id = action.Id,
					Label = action.Label,
					Style = action.Style switch
					{
						NotificationsWire.ActionStyle.Primary => NotificationActionStyle.Primary,
						NotificationsWire.ActionStyle.Danger => NotificationActionStyle.Danger,
						_ => NotificationActionStyle.Default
					}
				})
			];
	}

	private static NotificationProgress? ToProgress(NotificationsProgressDto? progress)
	{
		return progress is null ? null : new NotificationProgress(progress.Processed, progress.Total);
	}

	private static UiNode? ToContent(JsonElement? content)
	{
		if (content is not { } element || element.ValueKind == JsonValueKind.Null)
		{
			return null;
		}

		if (element.ValueKind != JsonValueKind.Object)
		{
			throw Invalid("The content is not a UI tree.");
		}

		try
		{
			return element.Deserialize<UiNode>(UiCanonicalJson.Options)
				?? throw Invalid("The content is not a UI tree.");
		}
		catch (JsonException)
		{
			throw Invalid("The content is not a valid UI tree.");
		}
	}

	private static NotificationException Invalid(string message)
	{
		return new NotificationException(NotificationErrorCode.Invalid, message);
	}
}
