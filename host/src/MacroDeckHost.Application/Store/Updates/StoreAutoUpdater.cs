using MacroDeck.Plugin.Packaging.Versioning;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Localization;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Store.Updates;

public sealed class StoreAutoUpdater : IDisposable
{
	public const string SummaryDedupeKey = "store-auto-updates";

	private readonly IStoreInstallationStore _installations;
	private readonly IPluginInstallationCatalog _plugins;
	private readonly IStoreUpdateBatchInstaller _installer;
	private readonly IStoreOperationTracker _tracker;
	private readonly IUserNotificationStore _notifications;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger _logger;
	private readonly Lock _sync = new();
	private readonly HashSet<string> _attempted = new(StringComparer.Ordinal);
	private readonly Dictionary<Guid, StoreOperation?> _batchOperations = [];
	private string? _batchId;
	private bool _registryFresh;

	public StoreAutoUpdater(IStoreInstallationStore installations,
		IPluginInstallationCatalog plugins,
		IStoreUpdateBatchInstaller installer,
		IStoreOperationTracker tracker,
		IUserNotificationStore notifications,
		IServiceScopeFactory scopeFactory,
		ILogger logger)
	{
		_installations = installations;
		_plugins = plugins;
		_installer = installer;
		_tracker = tracker;
		_notifications = notifications;
		_scopeFactory = scopeFactory;
		_logger = logger.ForContext<StoreAutoUpdater>();
		_tracker.Changed += OnOperationChanged;
	}

	public void Dispose() => _tracker.Changed -= OnOperationChanged;

	public void MarkRegistryFresh()
	{
		lock (_sync)
		{
			_registryFresh = true;
		}
	}

	public bool Covers(StoreAvailableUpdate update)
	{
		var key = StoreUpdateScope.Key(update);
		lock (_sync)
		{
			if (!_registryFresh)
			{
				return false;
			}

			if (_attempted.Contains(key))
			{
				return true;
			}
		}

		return IsInstalledFromStore(update);
	}

	public async Task Apply(IReadOnlyList<StoreAvailableUpdate> updates, CancellationToken cancellationToken = default)
	{
		lock (_sync)
		{
			if (!_registryFresh)
			{
				return;
			}
		}

		ExtensionSettings settings;
		await using (var scope = _scopeFactory.CreateAsyncScope())
		{
			settings = await scope.ServiceProvider.GetRequiredService<IAppPreferenceService>().GetExtensions();
		}

		if (!settings.AutoUpdate || !settings.CheckForUpdates)
		{
			return;
		}

		lock (_sync)
		{
			var eligible = updates
				.Where(update => StoreUpdateScope.IsUpdatable(update.Kind))
				.Where(update => !_attempted.Contains(StoreUpdateScope.Key(update)))
				.Where(update => _tracker.FindLive(update.Kind, update.PackageId) is null)
				.Where(IsInstalledFromStore)
				.ToList();
			if (eligible.Count == 0)
			{
				return;
			}

			_batchId ??= Guid.NewGuid().ToString("N");
			foreach (var update in eligible)
			{
				var key = StoreUpdateScope.Key(update);
				_attempted.Add(key);
				var started = _installer.Install([update], _batchId);
				if (started.Count > 0 && started[0] is { IsTerminal: false } operation)
				{
					_batchOperations[operation.Id] = operation;
				}
			}

			_logger.Information("Started {Count} automatic store updates", eligible.Count);
			if (_batchOperations.Count == 0)
			{
				_batchId = null;
			}
		}
	}

	private bool IsInstalledFromStore(StoreAvailableUpdate update)
	{
		if (!StoreUpdateScope.IsUpdatable(update.Kind))
		{
			return false;
		}

		var record = _installations.Find(update.Kind, update.PackageId);
		if (record is null)
		{
			return false;
		}

		if (update.Kind is not StoreExtensionKind.Plugin)
		{
			return true;
		}

		var active = _plugins.Discover()
			.FirstOrDefault(plugin => string.Equals(plugin.PluginId, update.PackageId, StringComparison.OrdinalIgnoreCase))
			?.ActiveVersion?.Version;

		return active is not null &&
			SemanticVersion.TryParse(active, out var activeVersion) &&
			SemanticVersion.TryParse(record.Version, out var recordedVersion) &&
			activeVersion.CompareTo(recordedVersion) == 0;
	}

	private void OnOperationChanged(StoreOperation operation)
	{
		List<StoreOperation> finished;
		lock (_sync)
		{
			if (!_batchOperations.ContainsKey(operation.Id))
			{
				return;
			}

			_batchOperations[operation.Id] = operation;
			if (_batchOperations.Values.Any(entry => entry is null || !entry.IsTerminal))
			{
				return;
			}

			finished = _batchOperations.Values.OfType<StoreOperation>().ToList();
			_batchOperations.Clear();
			_batchId = null;
		}

		_ = Task.Run(() => AnnounceBatch(finished));
	}

	private async Task AnnounceBatch(IReadOnlyList<StoreOperation> operations)
	{
		try
		{
			var updated = operations.Where(entry => entry.State is StoreOperationState.Completed).ToList();
			var failed = operations.Where(entry => entry.State is StoreOperationState.Failed).ToList();
			if (updated.Count == 0 && failed.Count == 0)
			{
				return;
			}

			var title = failed.Count > 0
				? await ActiveLocalization.Resolve(_scopeFactory, AppStrings.Notifications.StoreAutoUpdateFailed(count: failed.Count))
				: await ActiveLocalization.Resolve(_scopeFactory, AppStrings.Notifications.StoreAutoUpdated(count: updated.Count));
			var sentences = new List<string>();
			if (updated.Count > 0)
			{
				sentences.Add(await ActiveLocalization.Resolve(_scopeFactory,
					AppStrings.Notifications.StoreAutoUpdatedMessage(names: Names(updated))));
			}

			if (failed.Count > 0)
			{
				sentences.Add(await ActiveLocalization.Resolve(_scopeFactory,
					AppStrings.Notifications.StoreAutoUpdateFailedMessage(names: Names(failed))));
			}

			var message = string.Join(" ", sentences);

			_notifications.Raise(new UserNotificationDraft
			{
				Severity = failed.Count > 0 ? UserNotificationSeverity.Warning : UserNotificationSeverity.Info,
				Kind = UserNotificationKind.Update,
				Title = title,
				Message = message,
				Action = new UserNotificationAction(UserNotificationActionKind.OpenExtensionStore,
					StoreUpdateNotifier.InstalledTarget),
				DedupeKey = SummaryDedupeKey
			});
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Announcing automatic store updates failed");
		}
	}

	private static string Names(IEnumerable<StoreOperation> operations) =>
		string.Join(", ", operations.Select(entry => $"{entry.DisplayName} {entry.Version}"));
}
