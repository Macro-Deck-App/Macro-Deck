namespace MacroDeckHost.Application.Notifications;

public sealed record UserNotification
{
	public required string Id { get; init; }

	public required long Sequence { get; init; }

	public required DateTimeOffset Timestamp { get; init; }

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
}
