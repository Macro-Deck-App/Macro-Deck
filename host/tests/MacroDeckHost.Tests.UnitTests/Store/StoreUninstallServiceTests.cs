using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Application.Ui.Transport.Messages.Store;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Infrastructure.Store;
using MacroDeckHost.Tests.UnitTests.Api;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.Icons;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Store;

/// <summary>Uninstall is deliberately synchronous, not routed through the download pipeline, and each
/// extension kind resolves to a different owning source (issue #517 follow-up): a plugin's data is kept
/// for a later reinstall, an icon pack goes through its owner so the deletion isn't duplicated, and a
/// profile template's record is pruned only once the profile it created is confirmed gone.</summary>
[TestFixture]
internal sealed class StoreUninstallServiceTests
{
	private const string IconPackPackageId = "com.acme.icons";
	private const string ProfileTemplatePackageId = "com.acme.profile";
	private const string DependentPluginId = "com.acme.plugin";

	private TestPaths _paths = null!;
	private IconTestHarness _iconHarness = null!;
	private JsonStoreInstallationStore _installations = null!;
	private FakePluginInstaller _pluginInstaller = null!;
	private IconPackService _iconPackService = null!;
	private InMemoryProfileStore _profileStore = null!;
	private ProfileCache _profileCache = null!;
	private ProfileService _profileService = null!;
	private FakeStoreUpdateDetector _updateDetector = null!;
	private FakeStoreOperationTracker _operationTracker = null!;
	private RecordingUiTransport _transport = null!;
	private StoreUninstallService _service = null!;

	[SetUp]
	public async Task SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.StoreDirectory);
		_iconHarness = new IconTestHarness();
		_installations = new JsonStoreInstallationStore(_paths, Logger.None);
		_pluginInstaller = new FakePluginInstaller();

		var ownerRegistry = new IconPackOwnerRegistry([
			new StoreIconPackOwner(_installations, new FakeStoreUpdateDetector(), Logger.None)
		]);
		_iconPackService = new IconPackService(_iconHarness.Cache,
			_iconHarness.BatchTracker,
			_iconHarness.Storage,
			_iconHarness.Mediator,
			ownerRegistry,
			Logger.None);

		_profileStore = new InMemoryProfileStore();
		_profileCache = new ProfileCache(_profileStore, Logger.None);
		await _profileCache.InitializeCache();
		_profileService = new ProfileService(_profileCache,
			new FolderCache(_profileCache),
			new InMemoryDeviceRepository(),
			_iconHarness.Mediator);

		_updateDetector = new FakeStoreUpdateDetector();
		_operationTracker = new FakeStoreOperationTracker();
		_transport = new RecordingUiTransport();

		_service = new StoreUninstallService(_installations,
			_pluginInstaller,
			_iconPackService,
			_profileService,
			_updateDetector,
			_operationTracker,
			_transport,
			Logger.None);
	}

	[TearDown]
	public void TearDown()
	{
		_iconHarness.Dispose();
		_profileCache.Dispose();
		_paths.Cleanup();
	}

	[Test]
	public async Task
		Uninstalling_an_installed_icon_pack_removes_the_pack_and_leaves_the_store_reporting_not_installed()
	{
		var packId = await SeedInstalledIconPack();

		var result = await _service.Uninstall(StoreExtensionKind.IconPack, IconPackPackageId);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_iconHarness.Cache.GetPackById(packId), Is.Null);
			// Install state for a non-plugin kind is entirely driven by this lookup - see
			// StoreCatalogQueryService.InstalledVersion - so a null record here is what "reports not
			// installed" means for the catalog.
			Assert.That(_installations.Find(StoreExtensionKind.IconPack, IconPackPackageId), Is.Null);
			// A successful uninstall must be visible to every other connected client and must not leave
			// a pending update offer behind for something that no longer exists.
			Assert.That(_transport.GroupMessages.Select(m => m.Message),
				Has.Some.InstanceOf<StoreCatalogChangedEvent>());
			Assert.That(_updateDetector.CheckCallCount, Is.GreaterThanOrEqualTo(1));
		});
	}

	[Test]
	public async Task A_failed_uninstall_publishes_no_catalog_change_and_does_not_refresh_updates()
	{
		var result = await _service.Uninstall(StoreExtensionKind.IconPack, "com.acme.never-installed");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(_transport.GroupMessages.Select(m => m.Message),
				Has.None.InstanceOf<StoreCatalogChangedEvent>());
			Assert.That(_updateDetector.CheckCallCount, Is.Zero);
		});
	}

	[Test]
	public async Task Uninstalling_a_profile_template_deletes_the_imported_profile_and_its_installation_record()
	{
		var keep = (await _profileService.Create("Keep")).Data!;
		var imported = (await _profileService.Create("Imported")).Data!;
		_installations.Save(TemplateRecord(imported.Id));

		var result = await _service.Uninstall(StoreExtensionKind.ProfileTemplate, ProfileTemplatePackageId);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_profileCache.GetById(imported.Id), Is.Null);
			Assert.That(_profileCache.GetById(keep.Id), Is.Not.Null);
			Assert.That(_installations.Find(StoreExtensionKind.ProfileTemplate, ProfileTemplatePackageId), Is.Null);
		});
	}

	[Test]
	public async Task Uninstalling_a_plugin_another_installed_plugin_hard_depends_on_is_refused_and_it_stays_installed()
	{
		_pluginInstaller.Installed.Add(DependentPluginId);
		_pluginInstaller.HardDependents.Add(DependentPluginId);

		var result = await _service.Uninstall(StoreExtensionKind.Plugin, DependentPluginId);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(StoreUninstallError.DependencyInUse));
		});
	}

	[Test]
	public async Task Uninstalling_a_plugin_keeps_its_data_and_does_not_force_the_removal()
	{
		const string pluginId = "com.acme.standalone-plugin";
		_pluginInstaller.Installed.Add(pluginId);

		var result = await _service.Uninstall(StoreExtensionKind.Plugin, pluginId);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			// A plugin uninstalled from the Store must leave its configuration behind for a later
			// reinstall, and must not silently force past the hard-dependency guard.
			Assert.That(_pluginInstaller.LastRequest?.KeepData, Is.True);
			Assert.That(_pluginInstaller.LastRequest?.Force, Is.False);
		});
	}

	[Test]
	public async Task Uninstalling_something_that_is_not_installed_reports_not_installed()
	{
		var result = await _service.Uninstall(StoreExtensionKind.IconPack, "com.acme.never-installed");

		Assert.That(result.Error, Is.EqualTo(StoreUninstallError.NotInstalled));
	}

	[Test]
	public async Task A_profile_template_record_with_no_target_self_heals_instead_of_being_stuck_installed()
	{
		_installations.Save(new StoreInstallationRecord
		{
			Origin = "official",
			Kind = StoreExtensionKind.ProfileTemplate,
			PackageId = ProfileTemplatePackageId,
			Version = "1.0.0",
			InstalledAt = DateTimeOffset.UtcNow,
			TargetIds = []
		});

		var result = await _service.Uninstall(StoreExtensionKind.ProfileTemplate, ProfileTemplatePackageId);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_installations.Find(StoreExtensionKind.ProfileTemplate, ProfileTemplatePackageId), Is.Null);
		});
	}

	[Test]
	public async Task A_stranded_icon_pack_record_is_dropped_even_when_the_owner_did_not_delete_it()
	{
		// Owns() resolves the owner by looking the record up under the pack's OWN SourceId, not the
		// package id the caller asks to uninstall - a mismatched SourceId (drift) makes Resolve() find
		// no owner, so IconPackService.Delete removes the pack directly without going through
		// StoreIconPackOwner.Release, and the installation record would otherwise survive.
		var packId = Guid.CreateVersion7();
		await _iconHarness.Cache.AddOrUpdatePack(new IconPackEntity
		{
			Id = packId,
			Name = "Store Icons",
			SourceType = IconPackSourceType.ExtensionStore,
			SourceId = "drifted-source-id",
			CreatedAt = DateTime.UtcNow
		});
		_installations.Save(new StoreInstallationRecord
		{
			Origin = "official",
			Kind = StoreExtensionKind.IconPack,
			PackageId = IconPackPackageId,
			Version = "1.0.0",
			InstalledAt = DateTimeOffset.UtcNow,
			TargetIds = [packId]
		});

		var result = await _service.Uninstall(StoreExtensionKind.IconPack, IconPackPackageId);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_iconHarness.Cache.GetPackById(packId), Is.Null);
			Assert.That(_installations.Find(StoreExtensionKind.IconPack, IconPackPackageId), Is.Null);
		});
	}

	[Test]
	public async Task An_uninstall_is_refused_while_a_live_operation_exists_for_the_same_package()
	{
		var packId = await SeedInstalledIconPack();
		_operationTracker.Live = new StoreOperation
		{
			Id = Guid.CreateVersion7(),
			Kind = StoreOperationKind.Install,
			ExtensionKind = StoreExtensionKind.IconPack,
			PackageId = IconPackPackageId,
			Version = "1.1.0",
			DisplayName = "Store Icons",
			State = StoreOperationState.Downloading,
			StartedAt = DateTimeOffset.UtcNow,
			UpdatedAt = DateTimeOffset.UtcNow
		};

		var result = await _service.Uninstall(StoreExtensionKind.IconPack, IconPackPackageId);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(StoreUninstallError.OperationInProgress));
			Assert.That(_iconHarness.Cache.GetPackById(packId), Is.Not.Null);
			Assert.That(_installations.Find(StoreExtensionKind.IconPack, IconPackPackageId), Is.Not.Null);
		});
	}

	[Test]
	public async Task
		Uninstalling_the_profile_template_whose_profile_is_the_last_remaining_one_is_refused_and_deletes_nothing()
	{
		var only = (await _profileService.Create("Only")).Data!;
		_installations.Save(TemplateRecord(only.Id));

		var result = await _service.Uninstall(StoreExtensionKind.ProfileTemplate, ProfileTemplatePackageId);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(StoreUninstallError.LastProfileProtected));
			Assert.That(_profileCache.GetById(only.Id), Is.Not.Null);
			Assert.That(_installations.Find(StoreExtensionKind.ProfileTemplate, ProfileTemplatePackageId), Is.Not.Null);
		});
	}

	private async Task<Guid> SeedInstalledIconPack()
	{
		var packId = Guid.CreateVersion7();
		await _iconHarness.Cache.AddOrUpdatePack(new IconPackEntity
		{
			Id = packId,
			Name = "Store Icons",
			SourceType = IconPackSourceType.ExtensionStore,
			SourceId = IconPackPackageId,
			CreatedAt = DateTime.UtcNow
		});
		_installations.Save(new StoreInstallationRecord
		{
			Origin = "official",
			Kind = StoreExtensionKind.IconPack,
			PackageId = IconPackPackageId,
			Version = "1.0.0",
			InstalledAt = DateTimeOffset.UtcNow,
			TargetIds = [packId]
		});
		return packId;
	}

	private static StoreInstallationRecord TemplateRecord(Guid profileId) => new()
	{
		Origin = "official",
		Kind = StoreExtensionKind.ProfileTemplate,
		PackageId = ProfileTemplatePackageId,
		Version = "1.0.0",
		InstalledAt = DateTimeOffset.UtcNow,
		TargetIds = [profileId]
	};
}

/// <summary>Mutates its <see cref="Installed" /> set only on a successful uninstall, matching the real
/// installer's contract of leaving the plugin untouched when it refuses - which is what lets the test
/// below assert "still installed" after a refusal without re-proving the installer's own hard-dependency
/// guard (covered by PluginInstallerTests).</summary>
internal sealed class FakePluginInstaller : IPluginInstaller
{
	public HashSet<string> Installed { get; } = [];

	public HashSet<string> HardDependents { get; } = [];

	public PluginUninstallRequest? LastRequest { get; private set; }

	public Task<PluginInstallResult>
		Inspect(PluginArtifactSource source, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public Task<PluginInstallResult> Install(PluginArtifactSource source,
		PluginInstallRequest request,
		CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public Task<PluginInstallResult> Activate(string pluginId,
		string version,
		CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public Task<PluginInstallResult> Uninstall(string pluginId,
		PluginUninstallRequest request,
		CancellationToken cancellationToken = default)
	{
		LastRequest = request;

		if (!Installed.Contains(pluginId))
		{
			return Task.FromResult(PluginInstallResult.Fail(PluginInstallError.NotInstalled,
				$"'{pluginId}' is not installed.",
				pluginId));
		}

		if (!request.Force && HardDependents.Contains(pluginId))
		{
			return Task.FromResult(PluginInstallResult.Fail(PluginInstallError.DependencyInUse,
				$"'{pluginId}' is required by another plugin.",
				pluginId));
		}

		Installed.Remove(pluginId);
		return Task.FromResult(PluginInstallResult.Ok(pluginId, "1.0.0", previousVersion: null, activated: false));
	}
}

/// <summary>Only <see cref="FindLive" /> is exercised by <c>StoreUninstallService</c> - every other
/// member throws, so a test that accidentally depends on tracked operation history fails loudly
/// instead of silently passing against unimplemented behaviour.</summary>
internal sealed class FakeStoreOperationTracker : IStoreOperationTracker
{
	public StoreOperation? Live { get; set; }

	public StoreOperation? FindLive(StoreExtensionKind kind, string packageId) =>
		Live is not null && Live.ExtensionKind == kind && Live.PackageId == packageId ? Live : null;

	public IReadOnlyList<StoreOperation> Snapshot() => throw new NotSupportedException();

	public StoreOperation? Find(Guid operationId) => throw new NotSupportedException();

	public StoreOperation Create(StoreOperationKind kind,
		StoreExtensionKind extensionKind,
		string packageId,
		string version,
		string displayName,
		string? previousVersion,
		Guid? retryOf = null) =>
		throw new NotSupportedException();

	public StoreOperation? Transition(Guid operationId,
		StoreOperationState state,
		StoreOperationError? failure = null,
		string? errorMessage = null) =>
		throw new NotSupportedException();

	public void ReportProgress(Guid operationId, long bytesDownloaded, long? totalBytes) =>
		throw new NotSupportedException();

	public bool Dismiss(Guid operationId) => throw new NotSupportedException();

	public void Restore(IReadOnlyList<StoreOperation> operations) => throw new NotSupportedException();

	public event Action<StoreOperation>? Changed
	{
		add { }
		remove { }
	}
}
