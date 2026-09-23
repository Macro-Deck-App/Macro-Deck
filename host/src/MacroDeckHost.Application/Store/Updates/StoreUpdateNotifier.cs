using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Store.Updates;

public sealed class StoreUpdateNotifier : IDisposable
{
	public const string DedupeKey = "store-updates";

	public const string InstalledTarget = "installed";

	private readonly IUserNotificationStore _store;
	private readonly StoreAutoUpdater _autoUpdater;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly HashSet<string> _notified = new(StringComparer.Ordinal);
	private string? _entryId;
	private string? _lastText;

	public StoreUpdateNotifier(IUserNotificationStore store,
		StoreAutoUpdater autoUpdater,
		IServiceScopeFactory scopeFactory)
	{
		_store = store;
		_autoUpdater = autoUpdater;
		_scopeFactory = scopeFactory;
	}

	public async Task Notify(IReadOnlyList<StoreAvailableUpdate> updates, CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			ExtensionSettings settings;
			await using (var scope = _scopeFactory.CreateAsyncScope())
			{
				settings = await scope.ServiceProvider.GetRequiredService<IAppPreferenceService>().GetExtensions();
			}

			var relevant = settings is { CheckForUpdates: true, NotifyOnUpdates: true }
				? updates
					.Where(update => StoreUpdateScope.IsUpdatable(update.Kind))
					.Where(update => !_autoUpdater.IsHeld(update))
					.Where(update => !settings.AutoUpdate || !_autoUpdater.Covers(update))
					.ToList()
				: [];

			if (relevant.Count == 0)
			{
				Retire();
				return;
			}

			var keys = relevant.Select(StoreUpdateScope.Key).ToList();
			var hasNew = keys.Any(key => !_notified.Contains(key));
			var title = await ActiveLocalization.Resolve(_scopeFactory,
				AppStrings.Notifications.StoreUpdatesAvailable(count: relevant.Count));
			var message = await ActiveLocalization.Resolve(_scopeFactory,
				AppStrings.Notifications.StoreUpdatesAvailableMessage(
					names: string.Join(", ", relevant.Select(update => update.Name))));
			var text = title + "\n" + message;

			// A dismissed entry stays dismissed until a version the user has not been told about appears.
			var stillShown = _entryId is not null && _store.Snapshot().Any(entry => entry.Id == _entryId);
			if (!hasNew && !(stillShown && text != _lastText))
			{
				return;
			}

			_notified.UnionWith(keys);
			var raised = _store.Raise(new UserNotificationDraft
			{
				Severity = UserNotificationSeverity.Info,
				Kind = UserNotificationKind.Update,
				Title = title,
				Message = message,
				Action = new UserNotificationAction(UserNotificationActionKind.OpenExtensionStore, InstalledTarget),
				DedupeKey = DedupeKey
			});
			_entryId = raised?.Id ?? _entryId;
			_lastText = text;
		}
		finally
		{
			_gate.Release();
		}
	}

	public void Dispose() => _gate.Dispose();

	private void Retire()
	{
		_store.Retire(DedupeKey);
		_notified.Clear();
		_entryId = null;
		_lastText = null;
	}
}
