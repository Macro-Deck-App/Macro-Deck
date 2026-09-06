namespace MacroDeckHost.Application.Notifications;

public interface IUserNotificationStore
{
	int Capacity { get; }

	UserNotification? Raise(UserNotificationDraft draft);

	UserNotification? RaiseIfAbsent(UserNotificationDraft draft);

	IReadOnlyList<UserNotification> Snapshot();

	bool UpdateProgress(string dedupeKey, UserNotificationProgress progress);

	bool Dismiss(string id);

	bool DismissByKey(string dedupeKey);

	bool Retire(string dedupeKey);

	bool DismissAll();

	event Action? Changed;
}
