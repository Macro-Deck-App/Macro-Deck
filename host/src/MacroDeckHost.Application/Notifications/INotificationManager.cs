using System.Text.Json;
using MacroDeck.Sdk.Notifications;

namespace MacroDeckHost.Application.Notifications;

public interface INotificationManager
{
	// Subscribers run while the manager is locked and must only queue work.
	event Action<NotificationEvent>? Published;

	Task<NotificationShowOutcome> ShowAsync(
		NotificationOwner owner,
		NotificationOptions options,
		CancellationToken cancellationToken = default);

	Task UpdateAsync(
		NotificationOwner owner,
		Guid id,
		NotificationUpdate update,
		CancellationToken cancellationToken = default);

	void Close(NotificationOwner owner, Guid id);

	NotificationRespondOutcome Respond(
		NotificationPrincipal principal,
		Guid id,
		string? actionId,
		IReadOnlyDictionary<string, JsonElement>? values);

	IReadOnlyList<ActiveNotification> Snapshot(NotificationPrincipal principal);

	ActiveNotification? Find(Guid id);

	int CloseOwner(string ownerId, NotificationCloseReason reason = NotificationCloseReason.OwnerGone);
}

public sealed record NotificationShowOutcome(Guid Id, Task<NotificationResult> Completion);
