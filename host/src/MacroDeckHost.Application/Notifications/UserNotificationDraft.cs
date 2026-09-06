namespace MacroDeckHost.Application.Notifications;

public sealed record UserNotificationDraft
{
	public required UserNotificationSeverity Severity { get; init; }

	public required UserNotificationKind Kind { get; init; }

	public required string Title { get; init; }

	public string? Message { get; init; }

	public string? SourceId { get; init; }

	public string? SourceName { get; init; }

	public UserNotificationAction? Action { get; init; }

	public IReadOnlyList<UserNotificationAction> Actions { get; init; } = [];

	public UserNotificationProgress? Progress { get; init; }

	public string? CancelKey { get; init; }

	public string? DedupeKey { get; init; }
}
