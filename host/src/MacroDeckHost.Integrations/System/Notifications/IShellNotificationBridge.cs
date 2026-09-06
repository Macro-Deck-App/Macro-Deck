namespace MacroDeckHost.Integrations.System.Notifications;

/// <summary>
/// Hands an OS notification to the bootstrapper, which posts it from the signed app bundle so the
/// system attributes it to Macro Deck instead of to whatever helper the host would have started.
/// <para>
/// Delivery is confirmed rather than fire-and-forget: <see cref="TryDispatchAsync" /> only reports
/// success once the bootstrapper says it showed the notification, so a caller that gets
/// <c>false</c> - no bootstrapper attached, showing failed, or nothing was reported before the
/// lease expired - still has to fall back to the platform notification service.
/// </para>
/// </summary>
public interface IShellNotificationBridge
{
	/// <summary>Whether a bootstrapper polled recently enough to be considered attached.</summary>
	bool IsAttached { get; }

	/// <summary>
	/// Queues a notification for the bootstrapper and waits for its result. Returns <c>false</c>
	/// immediately when nothing is attached, and after the lease when the bootstrapper stays silent.
	/// </summary>
	Task<bool> TryDispatchAsync(string title, string message, CancellationToken cancellationToken = default);

	/// <summary>
	/// Marks the caller as the attached bootstrapper and returns the queued notifications, waiting up
	/// to <paramref name="hold" /> for the first one. Only one caller may wait at a time, because the
	/// read consumes the queue - a second concurrent caller is rejected instead of silently taking
	/// notifications away from the bootstrapper.
	/// </summary>
	/// <exception cref="InvalidOperationException">Another caller is already waiting.</exception>
	Task<IReadOnlyList<ShellNotification>> WaitAsync(TimeSpan hold, CancellationToken cancellationToken = default);

	/// <summary>Reports whether a dispatched notification was shown.</summary>
	void Report(long id, bool shown);
}
