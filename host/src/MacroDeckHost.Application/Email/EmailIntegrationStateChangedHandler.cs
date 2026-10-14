using MacroDeckHost.Application.Events;
using Mediator;

namespace MacroDeckHost.Application.Email;

public sealed class EmailIntegrationStateChangedHandler : INotificationHandler<IntegrationStateChangedNotification>
{
	private readonly IEmailInboxRegistry _registry;
	private readonly IEmailInboxCache _cache;
	private readonly IEmailSyncSignal _signal;

	public EmailIntegrationStateChangedHandler(
		IEmailInboxRegistry registry,
		IEmailInboxCache cache,
		IEmailSyncSignal signal)
	{
		_registry = registry;
		_cache = cache;
		_signal = signal;
	}

	public ValueTask Handle(IntegrationStateChangedNotification notification, CancellationToken cancellationToken)
	{
		var integrationId = notification.IntegrationId;
		_registry.ReconcileSubscriptions();

		if (_registry.IsEmailIntegration(integrationId)
			|| _cache.Snapshot.Inboxes.Any(state => state.Inbox.IntegrationId == integrationId))
		{
			_signal.Request(integrationId, null);
		}

		return ValueTask.CompletedTask;
	}
}
