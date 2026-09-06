using MacroDeckHost.Application.Store.Updates;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

/// <summary>Recomputes available updates whenever the registry snapshot changes, so the update list never
/// drifts from the catalog it was computed against.</summary>
public sealed class StoreUpdateCheckHandler : INotificationHandler<StoreRegistryRefreshedNotification>
{
	private readonly IStoreUpdateDetector _detector;
	private readonly IMediator _mediator;

	public StoreUpdateCheckHandler(IStoreUpdateDetector detector, IMediator mediator)
	{
		_detector = detector;
		_mediator = mediator;
	}

	public async ValueTask Handle(StoreRegistryRefreshedNotification notification, CancellationToken cancellationToken)
	{
		if (!notification.Status.HasCatalog)
		{
			return;
		}

		var updates = _detector.Check();
		await _mediator.Publish(new StoreUpdatesChangedNotification(updates), cancellationToken);
	}
}
