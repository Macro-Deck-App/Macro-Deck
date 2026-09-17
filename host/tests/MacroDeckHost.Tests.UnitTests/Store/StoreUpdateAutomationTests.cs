using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Application.Store.Updates;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Backups;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Infrastructure.Store;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Store;

[TestFixture]
internal sealed class StoreUpdateAutomationTests
{
	private const string PluginId = "com.acme.hue";
	private const string IconPackId = "com.acme.material";
	private const string TemplateId = "com.acme.streaming";

	private TestPaths _paths = null!;
	private JsonStoreInstallationStore _installations = null!;
	private StubPluginCatalog _plugins = null!;
	private StoreOperationTracker _tracker = null!;
	private RecordingBatchInstaller _installer = null!;
	private UserNotificationStore _notifications = null!;
	private AppPreferenceService _preferences = null!;
	private ServiceProvider _provider = null!;
	private StoreAutoUpdater _autoUpdater = null!;
	private StoreUpdateNotifier _notifier = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.StoreDirectory);
		_installations = new JsonStoreInstallationStore(_paths, Serilog.Core.Logger.None);
		_plugins = new StubPluginCatalog();
		_tracker = new StoreOperationTracker(new InMemoryStoreOperationStore(), TimeProvider.System);
		_installer = new RecordingBatchInstaller(_tracker);
		_notifications = new UserNotificationStore();
		_preferences = new AppPreferenceService(new InMemoryPreferenceRepository(),
			new StubBuildEnvironment(),
			new FakeHostListenerState());

		var services = new ServiceCollection();
		services.AddSingleton<IAppPreferenceService>(_preferences);
		services.AddSingleton(TestLocalization.Resolver);
		_provider = services.BuildServiceProvider();
		var scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();

		_autoUpdater = new StoreAutoUpdater(_installations,
			_plugins,
			_installer,
			_tracker,
			_notifications,
			scopeFactory,
			Serilog.Core.Logger.None);
		_notifier = new StoreUpdateNotifier(_notifications, _autoUpdater, scopeFactory);
	}

	[TearDown]
	public void TearDown()
	{
		_notifier.Dispose();
		_autoUpdater.Dispose();
		_provider.Dispose();
		_paths.Cleanup();
	}

	[Test]
	public async Task The_user_is_told_about_an_update_even_after_an_earlier_check_found_none()
	{
		await _notifier.Notify([]);

		await _notifier.Notify([PluginUpdate("1.1.0")]);

		var entry = _notifications.Snapshot().Single();
		Assert.Multiple(() =>
		{
			Assert.That(entry.Title, Is.EqualTo("1 extension update available"));
			Assert.That(entry.Message, Does.Contain("Hue Bridge"));
			Assert.That(entry.Action!.Kind, Is.EqualTo(UserNotificationActionKind.OpenExtensionStore));
			Assert.That(entry.Action.Target, Is.EqualTo(StoreUpdateNotifier.InstalledTarget));
		});
	}

	[Test]
	public async Task A_dismissed_update_notification_does_not_come_back_until_a_newer_version_appears()
	{
		await _notifier.Notify([PluginUpdate("1.1.0")]);
		_notifications.DismissByKey(StoreUpdateNotifier.DedupeKey);

		await _notifier.Notify([PluginUpdate("1.1.0")]);
		var afterRepeat = _notifications.Snapshot().Count;
		await _notifier.Notify([PluginUpdate("1.2.0")]);

		Assert.Multiple(() =>
		{
			Assert.That(afterRepeat, Is.Zero);
			Assert.That(_notifications.Snapshot(), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task The_notification_disappears_once_nothing_is_left_to_update()
	{
		await _notifier.Notify([PluginUpdate("1.1.0")]);

		await _notifier.Notify([]);

		Assert.That(_notifications.Snapshot(), Is.Empty);
	}

	[Test]
	public async Task No_update_notification_is_shown_when_the_user_turned_them_off()
	{
		await _preferences.SetExtensions(null, null, notifyOnUpdates: false, null);

		await _notifier.Notify([PluginUpdate("1.1.0")]);

		Assert.That(_notifications.Snapshot(), Is.Empty);
	}

	[Test]
	public async Task Profile_template_updates_are_neither_announced_nor_installed_automatically()
	{
		await EnableAutoUpdate();
		var template = new StoreAvailableUpdate
		{
			Kind = StoreExtensionKind.ProfileTemplate,
			PackageId = TemplateId,
			Name = "Streaming",
			InstalledVersion = "1.0.0",
			LatestVersion = "2.0.0"
		};
		SaveRecord(StoreExtensionKind.ProfileTemplate, TemplateId, "1.0.0");

		await _autoUpdater.Apply([template]);
		await _notifier.Notify([template]);

		Assert.Multiple(() =>
		{
			Assert.That(_installer.Started, Is.Empty);
			Assert.That(_notifications.Snapshot(), Is.Empty);
		});
	}

	[Test]
	public async Task Automatic_updates_wait_for_a_registry_fetched_in_this_session()
	{
		await EnableAutoUpdate(markFresh: false);
		InstallFromStore("1.0.0");

		await _autoUpdater.Apply([PluginUpdate("1.1.0")]);

		Assert.That(_installer.Started, Is.Empty);
	}

	[Test]
	public async Task A_plugin_installed_from_a_file_is_announced_instead_of_being_replaced_automatically()
	{
		await EnableAutoUpdate();
		_plugins.Active[PluginId] = "1.0.0";

		await _autoUpdater.Apply([PluginUpdate("1.1.0")]);
		await _notifier.Notify([PluginUpdate("1.1.0")]);

		Assert.Multiple(() =>
		{
			Assert.That(_installer.Started, Is.Empty);
			Assert.That(_notifications.Snapshot(), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_plugin_rolled_back_to_another_version_is_not_updated_automatically()
	{
		await EnableAutoUpdate();
		SaveRecord(StoreExtensionKind.Plugin, PluginId, "1.0.5");
		_plugins.Active[PluginId] = "1.0.0";

		await _autoUpdater.Apply([PluginUpdate("1.1.0")]);

		Assert.That(_installer.Started, Is.Empty);
	}

	[Test]
	public async Task Store_updates_install_automatically_in_one_backup_batch_and_end_in_one_summary()
	{
		await EnableAutoUpdate();
		InstallFromStore("1.0.0");
		SaveRecord(StoreExtensionKind.IconPack, IconPackId, "1.0.0");
		var iconUpdate = new StoreAvailableUpdate
		{
			Kind = StoreExtensionKind.IconPack,
			PackageId = IconPackId,
			Name = "Material",
			InstalledVersion = "1.0.0",
			LatestVersion = "2.0.0"
		};

		await _autoUpdater.Apply([PluginUpdate("1.1.0"), iconUpdate]);
		await _notifier.Notify([PluginUpdate("1.1.0"), iconUpdate]);
		var announcedWhileRunning = _notifications.Snapshot().Count;
		foreach (var operation in _installer.Started)
		{
			_tracker.Transition(operation.Id, StoreOperationState.Completed);
		}

		var summary = await WaitForNotification();
		Assert.Multiple(() =>
		{
			Assert.That(_installer.Started, Has.Count.EqualTo(2));
			Assert.That(_installer.BatchIds.Distinct().Count(), Is.EqualTo(1));
			Assert.That(announcedWhileRunning, Is.Zero);
			Assert.That(summary.Title, Is.EqualTo("2 extensions updated automatically"));
			Assert.That(summary.Message, Does.Contain("Hue Bridge 1.1.0").And.Contain("Material 2.0.0"));
		});
	}

	[Test]
	public async Task An_update_that_stays_listed_after_installing_is_not_installed_again_and_again()
	{
		await EnableAutoUpdate();
		InstallFromStore("1.0.0");

		await _autoUpdater.Apply([PluginUpdate("1.1.0")]);
		_tracker.Transition(_installer.Started[0].Id, StoreOperationState.Completed);
		await _autoUpdater.Apply([PluginUpdate("1.1.0")]);
		await _autoUpdater.Apply([PluginUpdate("1.1.0")]);

		Assert.That(_installer.Started, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task A_failed_automatic_update_is_reported_once_in_the_summary_together_with_what_succeeded()
	{
		await EnableAutoUpdate();
		InstallFromStore("1.0.0");
		SaveRecord(StoreExtensionKind.IconPack, IconPackId, "1.0.0");
		var iconUpdate = new StoreAvailableUpdate
		{
			Kind = StoreExtensionKind.IconPack,
			PackageId = IconPackId,
			Name = "Material",
			InstalledVersion = "1.0.0",
			LatestVersion = "2.0.0"
		};

		await _autoUpdater.Apply([PluginUpdate("1.1.0"), iconUpdate]);
		_tracker.Transition(_installer.Started[0].Id,
			StoreOperationState.Failed,
			StoreOperationError.InstallFailed,
			"broken");
		_tracker.Transition(_installer.Started[1].Id, StoreOperationState.Completed);
		var summary = await WaitForNotification();
		await _notifier.Notify([PluginUpdate("1.1.0")]);

		Assert.Multiple(() =>
		{
			Assert.That(summary.Severity, Is.EqualTo(UserNotificationSeverity.Warning));
			Assert.That(summary.Title, Is.EqualTo("1 automatic extension update failed"));
			Assert.That(summary.Message, Does.Contain("Material 2.0.0").And.Contain("Hue Bridge 1.1.0"));
			Assert.That(_notifications.Snapshot(), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Updates_are_announced_while_no_registry_fetch_has_succeeded_yet()
	{
		await EnableAutoUpdate(markFresh: false);
		InstallFromStore("1.0.0");

		await _notifier.Notify([PluginUpdate("1.1.0")]);

		Assert.That(_notifications.Snapshot().Select(entry => entry.Title), Does.Contain("1 extension update available"));
	}

	[Test]
	public async Task The_summary_still_names_an_update_whose_operation_was_dismissed()
	{
		await EnableAutoUpdate();
		InstallFromStore("1.0.0");

		await _autoUpdater.Apply([PluginUpdate("1.1.0")]);
		_tracker.Transition(_installer.Started[0].Id, StoreOperationState.Completed);
		_tracker.Dismiss(_installer.Started[0].Id);
		var summary = await WaitForNotification();

		Assert.That(summary.Message, Does.Contain("Hue Bridge 1.1.0"));
	}

	[Test]
	public async Task One_backup_covers_a_whole_update_batch_but_a_failed_backup_does_not()
	{
		var backups = new CountingBackupService();
		var services = new ServiceCollection();
		services.AddSingleton<IAppPreferenceService>(_preferences);
		services.AddSingleton<IBackupService>(backups);
		await using var provider = services.BuildServiceProvider();
		var coordinator = new PreUpdateBackupCoordinator(provider.GetRequiredService<IServiceScopeFactory>());

		backups.Succeed = false;
		var firstFailed = await coordinator.EnsureBeforePluginUpdate("com.acme.a", "batch");
		var secondAfterFailure = await coordinator.EnsureBeforePluginUpdate("com.acme.b", "batch");
		backups.Succeed = true;
		await coordinator.EnsureBeforePluginUpdate("com.acme.c", "batch");
		var fourth = await coordinator.EnsureBeforePluginUpdate("com.acme.d", "batch");

		Assert.Multiple(() =>
		{
			Assert.That(firstFailed.Success, Is.False);
			Assert.That(secondAfterFailure.Success, Is.False);
			Assert.That(fourth.Skipped, Is.True);
			Assert.That(backups.CreateCount, Is.EqualTo(3));
		});
	}

	private async Task EnableAutoUpdate(bool markFresh = true)
	{
		await _preferences.SetExtensions(null, null, null, null, autoUpdate: true);
		if (markFresh)
		{
			_autoUpdater.MarkRegistryFresh();
		}
	}

	private void InstallFromStore(string version)
	{
		SaveRecord(StoreExtensionKind.Plugin, PluginId, version);
		_plugins.Active[PluginId] = version;
	}

	private void SaveRecord(StoreExtensionKind kind, string packageId, string version) =>
		_installations.Save(new StoreInstallationRecord
		{
			Origin = "https://registry.example/",
			Kind = kind,
			PackageId = packageId,
			Version = version
		});

	private async Task<UserNotification> WaitForNotification()
	{
		for (var attempt = 0; attempt < 200; attempt++)
		{
			var summary = _notifications.Snapshot()
				.FirstOrDefault(entry => entry.Title.Contains("automatic", StringComparison.Ordinal));
			if (summary is not null)
			{
				return summary;
			}

			await Task.Delay(25);
		}

		throw new AssertionException("No automatic update summary was raised.");
	}

	private static StoreAvailableUpdate PluginUpdate(string latest) => new()
	{
		Kind = StoreExtensionKind.Plugin,
		PackageId = PluginId,
		Name = "Hue Bridge",
		InstalledVersion = "1.0.0",
		LatestVersion = latest
	};

	private sealed class RecordingBatchInstaller(StoreOperationTracker tracker) : IStoreUpdateBatchInstaller
	{
		public List<StoreOperation> Started { get; } = [];

		public List<string?> BatchIds { get; } = [];

		public IReadOnlyList<StoreOperation> Install(IEnumerable<StoreAvailableUpdate> updates,
			string? backupBatchId = null)
		{
			var operations = updates.Select(update => tracker.Create(StoreOperationKind.Update,
					update.Kind,
					update.PackageId,
					update.LatestVersion,
					update.Name,
					update.InstalledVersion))
				.ToList();
			Started.AddRange(operations);
			BatchIds.AddRange(operations.Select(_ => backupBatchId));
			return operations;
		}
	}

	private sealed class StubPluginCatalog : IPluginInstallationCatalog
	{
		public Dictionary<string, string> Active { get; } = new(StringComparer.Ordinal);

		public IReadOnlyList<InstalledPlugin> Discover() =>
			Active.Select(pair => new InstalledPlugin
				{
					PluginId = pair.Key,
					PluginDirectory = "/plugins/" + pair.Key,
					Versions = [],
					ActiveVersion = new InstalledPluginVersion
					{
						Version = pair.Value,
						VersionDirectory = "/plugins/" + pair.Key + "/" + pair.Value,
						ManifestPath = "/plugins/" + pair.Key + "/" + pair.Value + "/manifest.json"
					}
				})
				.ToList();

		public bool TryResolveActive(string pluginId, out InstalledPluginVersion? version)
		{
			version = null;
			return false;
		}

		public void Invalidate()
		{
		}
	}

	private sealed class InMemoryPreferenceRepository : IAppPreferenceRepository
	{
		private readonly Dictionary<string, AppPreferenceEntity> _store = new();

		public Task<AppPreferenceEntity?> GetByKey(string key) => Task.FromResult(_store.GetValueOrDefault(key));

		public Task SetValue(string key, string value)
		{
			_store[key] = new AppPreferenceEntity { Key = key, Value = value };
			return Task.CompletedTask;
		}
	}

	private sealed class StubBuildEnvironment : IBuildEnvironment
	{
		public string Version => "0.0.0-test";

		public bool IsBeta => false;

		public BuildChannel Channel => BuildChannel.Production;
	}

	private sealed class CountingBackupService : IBackupService
	{
		public bool Succeed { get; set; } = true;

		public int CreateCount { get; private set; }

		public Task<Result<BackupDescriptor, BackupError>> Create(CreateBackupRequest request,
			CancellationToken cancellationToken = default)
		{
			CreateCount++;
			return Task.FromResult(Succeed
				? Result.Ok<BackupDescriptor, BackupError>(new BackupDescriptor(Guid.NewGuid(),
					"local",
					"storage",
					"backup",
					DateTimeOffset.UtcNow,
					BackupTrigger.BeforePluginUpdate,
					"0.0.0",
					1,
					1,
					0,
					false,
					true,
					false,
					null,
					[]))
				: Result.Fail<BackupDescriptor, BackupError>(BackupError.ProviderUnavailable, "disk full"));
		}

		public Task<Result<IReadOnlyList<BackupDescriptor>, BackupError>> List(
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<Result<BackupInspection, BackupError>> Inspect(BackupSourceRef source,
			string? recoveryKey,
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<Result<BackupDescriptor, BackupError>> Import(BackupSourceRef source,
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<Result<BackupExportHandle, BackupError>> OpenExport(Guid backupId,
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<Result<BackupError>> Delete(Guid backupId, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();
	}
}
