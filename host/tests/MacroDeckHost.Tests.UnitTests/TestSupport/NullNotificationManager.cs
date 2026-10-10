using System.Text.Json;
using MacroDeck.Sdk.Notifications;
using MacroDeckHost.Application.Notifications;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class NullNotificationManager : INotificationManager
{
	public event Action<NotificationEvent>? Published
	{
		add { }
		remove { }
	}

	public Task<NotificationShowOutcome> ShowAsync(NotificationOwner owner,
		NotificationOptions options,
		CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public Task UpdateAsync(NotificationOwner owner,
		Guid id,
		NotificationUpdate update,
		CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public void Close(NotificationOwner owner, Guid id)
	{
	}

	public NotificationRespondOutcome Respond(NotificationPrincipal principal,
		Guid id,
		string? actionId,
		IReadOnlyDictionary<string, JsonElement>? values)
		=> new(NotificationRespondStatus.NotFound);

	public IReadOnlyList<ActiveNotification> Snapshot(NotificationPrincipal principal) => [];

	public ActiveNotification? Find(Guid id) => null;

	public int CloseOwner(string ownerId, NotificationCloseReason reason = NotificationCloseReason.OwnerGone) => 0;
}
