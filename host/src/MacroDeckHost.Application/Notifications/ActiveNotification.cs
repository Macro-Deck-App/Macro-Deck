using MacroDeck.Localization;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Ui.Model.Nodes;

namespace MacroDeckHost.Application.Notifications;

public sealed record ActiveNotification
{
	public required Guid Id { get; init; }

	public required long Revision { get; init; }

	public required NotificationOwner Owner { get; init; }

	public required LocalizedText Title { get; init; }

	public LocalizedText? Message { get; init; }

	public required NotificationSeverity Severity { get; init; }

	public required NotificationPopup Popup { get; init; }

	public required bool Inbox { get; init; }

	public required bool Dismissible { get; init; }

	public required NotificationTarget Target { get; init; }

	public required DateTimeOffset CreatedAt { get; init; }

	public DateTimeOffset? ExpiresAt { get; init; }

	public IReadOnlyList<NotificationAction> Actions { get; init; } = [];

	public UiNode? Content { get; init; }

	public NotificationProgress? Progress { get; init; }
}
