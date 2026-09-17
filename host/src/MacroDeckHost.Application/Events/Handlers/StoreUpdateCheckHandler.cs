using MacroDeckHost.Application.Store.Updates;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

/// <summary>Recomputes available updates whenever the registry snapshot changes, so the update list never
/// drifts from the catalog it was computed against.</summary>
public sealed class StoreUpdateCheckHandler : INotificationHandler<StoreRegistryRefreshedNotification>
{
	private readonly IStoreUpdateDetector _detector;
	private readonly StoreAutoUpdater _autoUpdater;

	public StoreUpdateCheckHandler(IStoreUpdateDetector detector, StoreAutoUpdater autoUpdater)
	{
		_detector = detector;
		_autoUpdater = autoUpdater;
	}

	public ValueTask Handle(StoreRegistryRefreshedNotification notification, CancellationToken cancellationToken)
	{
		if (!notification.Status.HasCatalog)
		{
			return ValueTask.CompletedTask;
		}

		// The cached registry loaded at startup has no attempt yet; only a fetch made in this session counts.
		if (notification.Status is { LastError: null, LastAttemptAt: not null })
		{
			_autoUpdater.MarkRegistryFresh();
		}

		_detector.Check();
		return ValueTask.CompletedTask;
	}
}
