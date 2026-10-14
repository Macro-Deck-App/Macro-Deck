using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Ui.Model.Serialization;
using MacroDeckHost.Application.Notifications;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Notifications;

public sealed class NotificationActionDto
{
	public string Id { get; set; } = string.Empty;

	public LocalizedText Label { get; set; }

	public string Style { get; set; } = NotificationsWire.ActionStyle.Default;
}

public sealed class NotificationProgressDto
{
	public int Processed { get; set; }

	public int? Total { get; set; }
}

public sealed class ActiveNotificationDto
{
	public Guid Id { get; set; }

	public long Revision { get; set; }

	public string SourceCategory { get; set; } = string.Empty;

	public string SourceId { get; set; } = string.Empty;

	public string SourceName { get; set; } = string.Empty;

	public LocalizedText Title { get; set; }

	public LocalizedText? Message { get; set; }

	public string Severity { get; set; } = NotificationsWire.Severity.Info;

	public string Popup { get; set; } = NotificationsWire.Popup.Toast;

	public bool Inbox { get; set; }

	public bool Dismissible { get; set; } = true;

	public DateTimeOffset CreatedAt { get; set; }

	public DateTimeOffset? ExpiresAt { get; set; }

	public long? ExpiresInMs { get; set; }

	public List<NotificationActionDto> Actions { get; set; } = [];

	public JsonElement? Content { get; set; }

	public NotificationProgressDto? Progress { get; set; }

	public static ActiveNotificationDto From(ActiveNotification notification, DateTimeOffset now)
	{
		return new ActiveNotificationDto
		{
			Id = notification.Id,
			Revision = notification.Revision,
			SourceCategory = notification.Owner.Category switch
			{
				NotificationSourceCategory.BuiltInIntegration => "built-in-integration",
				NotificationSourceCategory.Plugin => "plugin",
				_ => "system"
			},
			SourceId = notification.Owner.Id,
			SourceName = notification.Owner.Name,
			Title = notification.Title,
			Message = notification.Message,
			Severity = notification.Severity switch
			{
				NotificationSeverity.Success => NotificationsWire.Severity.Success,
				NotificationSeverity.Warning => NotificationsWire.Severity.Warning,
				NotificationSeverity.Error => NotificationsWire.Severity.Error,
				_ => NotificationsWire.Severity.Info
			},
			Popup = notification.Popup switch
			{
				NotificationPopup.Dialog => NotificationsWire.Popup.Dialog,
				NotificationPopup.None => NotificationsWire.Popup.None,
				_ => NotificationsWire.Popup.Toast
			},
			Inbox = notification.Inbox,
			Dismissible = notification.Dismissible,
			CreatedAt = notification.CreatedAt,
			ExpiresAt = notification.ExpiresAt,
			ExpiresInMs = notification.ExpiresAt is { } expires
				? Math.Max(0, (long)(expires - now).TotalMilliseconds)
				: null,
			Actions =
			[
				.. notification.Actions.Select(action => new NotificationActionDto
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
			],
			Content = notification.Content is { } content ? UiCanonicalJson.ToElement(content) : null,
			Progress = notification.Progress is { } progress
				? new NotificationProgressDto { Processed = progress.Processed, Total = progress.Total }
				: null
		};
	}
}

public sealed class NotificationShownEvent
{
	public ActiveNotificationDto Notification { get; set; } = new();
}

public sealed class NotificationUpdatedEvent
{
	public ActiveNotificationDto Notification { get; set; } = new();
}

public sealed class NotificationClosedEvent
{
	public Guid Id { get; set; }

	public long Revision { get; set; }

	public string Reason { get; set; } = NotificationsWire.CloseReason.Dismissed;

	public string? ActionId { get; set; }
}

public sealed class ActiveNotificationsEvent
{
	public List<ActiveNotificationDto> Notifications { get; set; } = [];
}

public sealed class RespondToNotificationRequest
{
	public Guid Id { get; set; }

	public string? ActionId { get; set; }

	public Dictionary<string, JsonElement>? Values { get; set; }
}

public sealed class RespondToNotificationResponse
{
	public string Status { get; set; } = string.Empty;

	public string? Message { get; set; }
}

public sealed class MarkUserNotificationReadRequest
{
	public string Id { get; set; } = string.Empty;
}

public sealed class MarkUserNotificationReadResponse
{
	public bool Success { get; set; }
}

public sealed class MarkAllUserNotificationsReadRequest;

public sealed class MarkAllUserNotificationsReadResponse
{
	public bool Success { get; set; }
}
