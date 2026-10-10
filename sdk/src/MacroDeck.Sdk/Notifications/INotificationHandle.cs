namespace MacroDeck.Sdk.Notifications;

/// <summary>An open notification. Only the integration that created it can update or dismiss it.</summary>
public interface INotificationHandle
{
	Guid Id { get; }

	/// <summary>Completes exactly once when the notification closes, whatever the reason.</summary>
	Task<NotificationResult> Completion { get; }

	Task UpdateAsync(NotificationUpdate update, CancellationToken cancellationToken = default);

	/// <summary>Closes the notification on every client. Closing an already closed notification does nothing.</summary>
	Task DismissAsync(CancellationToken cancellationToken = default);
}
