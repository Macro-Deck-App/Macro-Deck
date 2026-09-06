using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Updates;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Store;

namespace MacroDeckHost.Tests.UnitTests.Icons;

/// <summary>Issue #707: an icon pack installed through the Store must not be deletable in a way that
/// leaves the Store's own installation record behind - that record is what StoreCatalogQueryService
/// derives "installed" from, so a divergence there is a permanently phantom "Installed" state. These
/// tests pin the full contract: routing removal through the owning provider, refusing removal for a
/// provider that forbids it, leaving unrelated ownership untouched, and self-healing records an older
/// build left orphaned.</summary>
[TestFixture]
internal sealed class IconPackOwnershipTests
{
	private const string IconPackId = "com.acme.material";

	private IconTestHarness _harness = null!;
	private JsonStoreInstallationStore _installations = null!;
	private StoreCatalog _catalog = null!;
	private PluginInstallationCatalog _plugins = null!;
	private StoreUpdateState _updateState = null!;
	private StoreUpdateDetector _updateDetector = null!;
	private StoreCatalogQueryService _catalogQuery = null!;
	private StoreIconPackOwner _storeOwner = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new IconTestHarness();
		Directory.CreateDirectory(_harness.Paths.StoreDirectory);
		Directory.CreateDirectory(_harness.Paths.PluginsDirectory);

		_installations = new JsonStoreInstallationStore(_harness.Paths, _harness.Logger);
		_catalog = new StoreCatalog();
		_plugins = new PluginInstallationCatalog(_harness.Paths, Serilog.Core.Logger.None);
		_updateState = new StoreUpdateState();
		_updateDetector = new StoreUpdateDetector(_catalog, _plugins, _installations, _updateState);
		_catalogQuery = new StoreCatalogQueryService(_catalog, _plugins, _installations);
		_storeOwner = new StoreIconPackOwner(_installations, _updateDetector, _harness.Logger);
	}

	[TearDown]
	public void TearDown() => _harness.Dispose();

	private IconPackService CreateService(params IIconPackOwner[] owners)
		=> new(_harness.Cache,
			_harness.BatchTracker,
			_harness.Storage,
			_harness.Mediator,
			new IconPackOwnerRegistry(owners),
			_harness.Logger);

	private static StoreReleaseManifest Release(string version) => new()
	{
		Version = version,
		ArtifactUrl = new Uri($"https://cdn.example/artifact-{version}.bin"),
		Sha256 = new string('a', 64),
		Size = 16
	};

	private void SwapCatalog(string latestVersion) => _catalog.Swap(new StoreCatalogSnapshot
	{
		Sequence = 1,
		Entries =
		[
			new StoreCatalogEntry
			{
				Kind = StoreExtensionKind.IconPack,
				Id = IconPackId,
				Name = "Material Icons",
				LatestVersion = latestVersion,
				LatestRelease = Release(latestVersion)
			}
		]
	});

	private async Task<IconPackEntity> InstallStorePack(string version = "1.0.0")
	{
		var pack = await _harness.CreatePack("Material Icons");
		pack.SourceType = IconPackSourceType.ExtensionStore;
		pack.SourceId = IconPackId;
		await _harness.Cache.AddOrUpdatePack(pack);

		_installations.Save(new StoreInstallationRecord
		{
			Origin = "https://registry.test/",
			Kind = StoreExtensionKind.IconPack,
			PackageId = IconPackId,
			Version = version,
			InstalledAt = DateTimeOffset.UtcNow,
			TargetIds = [pack.Id]
		});

		return pack;
	}

	// Scenario 1 (regression): must fail against today's code, which never touches the Store's
	// installation record on delete.
	[Test]
	public async Task Deleting_a_Store_installed_pack_clears_the_Store_installation_record_and_reports_not_installed()
	{
		SwapCatalog("1.0.0");
		var pack = await InstallStorePack("1.0.0");
		var service = CreateService(_storeOwner);

		var result = await service.Delete(pack.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_harness.Cache.GetPackById(pack.Id), Is.Null);
			Assert.That(_installations.Find(StoreExtensionKind.IconPack, IconPackId), Is.Null);

			var found = _catalogQuery.Find(StoreExtensionKind.IconPack, IconPackId);
			Assert.That(found.Success, Is.True);
			Assert.That(found.Data!.InstallState, Is.EqualTo(StoreInstallState.NotInstalled));
			Assert.That(found.Data!.InstalledVersion, Is.Null);
		});
	}

	[TestCase(IconPackSourceType.User)]
	[TestCase(IconPackSourceType.StreamDeckImport)]
	[TestCase(IconPackSourceType.TouchPortalImport)]
	[TestCase(IconPackSourceType.MacroDeckImport)]
	public async Task User_created_and_imported_packs_stay_directly_deletable(IconPackSourceType sourceType)
	{
		var pack = await _harness.CreatePack("Local Pack");
		pack.SourceType = sourceType;
		await _harness.Cache.AddOrUpdatePack(pack);
		var service = CreateService(_storeOwner);

		var result = await service.Delete(pack.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_harness.Cache.GetPackById(pack.Id), Is.Null);
		});
	}

	[Test]
	public async Task Default_and_read_only_protections_keep_their_own_error_and_are_not_shadowed_by_ownership()
	{
		var defaultPack = await _harness.CreatePack("Imported Icons", isDefault: true);
		var readOnlyPack = await _harness.CreatePack("Bundled Pack", isReadOnly: true);
		var service = CreateService(_storeOwner);

		var defaultResult = await service.Delete(defaultPack.Id);
		var readOnlyResult = await service.Delete(readOnlyPack.Id);

		Assert.Multiple(() =>
		{
			Assert.That(defaultResult.Success, Is.False);
			Assert.That(defaultResult.Error, Is.EqualTo(IconPackError.DefaultPackProtected));
			Assert.That(readOnlyResult.Success, Is.False);
			Assert.That(readOnlyResult.Error, Is.EqualTo(IconPackError.ReadOnly));
			Assert.That(_harness.Cache.GetPackById(defaultPack.Id), Is.Not.Null);
			Assert.That(_harness.Cache.GetPackById(readOnlyPack.Id), Is.Not.Null);
		});
	}

	[Test]
	public async Task A_provider_that_forbids_removal_refuses_observably()
	{
		var pack = await _harness.CreatePack("Plugin Pack");
		var forbidding = new TestIconPackOwner(canRemove: false);
		var service = CreateService(forbidding);

		var result = await service.Delete(pack.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(IconPackError.OwnedBySource));
			Assert.That(_harness.Cache.GetPackById(pack.Id), Is.Not.Null);
			Assert.That(_harness.Mediator.Published.OfType<IconPackDeletedNotification>(), Is.Empty);
		});
	}

	[Test]
	public async Task Record_removal_is_keyed_by_kind_not_just_package_id()
	{
		SwapCatalog("1.0.0");
		var pack = await InstallStorePack("1.0.0");
		_installations.Save(new StoreInstallationRecord
		{
			Origin = "https://registry.test/",
			Kind = StoreExtensionKind.ProfileTemplate,
			PackageId = IconPackId,
			Version = "3.0.0",
			InstalledAt = DateTimeOffset.UtcNow,
			TargetIds = [Guid.CreateVersion7()]
		});
		var service = CreateService(_storeOwner);

		var result = await service.Delete(pack.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			var profileRecord = _installations.Find(StoreExtensionKind.ProfileTemplate, IconPackId);
			Assert.That(profileRecord, Is.Not.Null);
			Assert.That(profileRecord!.Version, Is.EqualTo("3.0.0"));
		});
	}

	[Test]
	public async Task An_orphaned_record_self_heals_at_startup_and_a_live_one_is_left_untouched()
	{
		SwapCatalog("2.0.0");
		_installations.Save(new StoreInstallationRecord
		{
			Origin = "https://registry.test/",
			Kind = StoreExtensionKind.IconPack,
			PackageId = IconPackId,
			Version = "1.0.0",
			InstalledAt = DateTimeOffset.UtcNow,
			TargetIds = [Guid.CreateVersion7()]
		});
		var livePack = await _harness.CreatePack("Still Here");
		livePack.SourceType = IconPackSourceType.ExtensionStore;
		livePack.SourceId = "com.acme.still-here";
		await _harness.Cache.AddOrUpdatePack(livePack);
		_installations.Save(new StoreInstallationRecord
		{
			Origin = "https://registry.test/",
			Kind = StoreExtensionKind.IconPack,
			PackageId = "com.acme.still-here",
			Version = "1.0.0",
			InstalledAt = DateTimeOffset.UtcNow,
			TargetIds = [livePack.Id]
		});

		var reconciler = new StoreInstallationReconciler(_installations, _harness.Cache);
		reconciler.PruneOrphanedIconPackRecords();

		Assert.Multiple(() =>
		{
			var found = _catalogQuery.Find(StoreExtensionKind.IconPack, IconPackId);
			Assert.That(found.Success, Is.True);
			Assert.That(found.Data!.InstallState, Is.EqualTo(StoreInstallState.NotInstalled));
			Assert.That(found.Data!.InstalledVersion, Is.Null);
			Assert.That(_installations.Find(StoreExtensionKind.IconPack, "com.acme.still-here"), Is.Not.Null);
		});
	}

	[Test]
	public async Task No_update_is_offered_for_an_orphan_but_a_real_pending_update_still_is()
	{
		var orphanCatalog = new StoreCatalogSnapshot
		{
			Sequence = 1,
			Entries =
			[
				new StoreCatalogEntry
				{
					Kind = StoreExtensionKind.IconPack,
					Id = IconPackId,
					Name = "Material Icons",
					LatestVersion = "2.0.0",
					LatestRelease = Release("2.0.0")
				},
				new StoreCatalogEntry
				{
					Kind = StoreExtensionKind.IconPack,
					Id = "com.acme.real",
					Name = "Real Pack",
					LatestVersion = "2.0.0",
					LatestRelease = Release("2.0.0")
				}
			]
		};
		_catalog.Swap(orphanCatalog);

		_installations.Save(new StoreInstallationRecord
		{
			Origin = "https://registry.test/",
			Kind = StoreExtensionKind.IconPack,
			PackageId = IconPackId,
			Version = "1.0.0",
			InstalledAt = DateTimeOffset.UtcNow,
			TargetIds = [Guid.CreateVersion7()]
		});

		var realPack = await _harness.CreatePack("Real Pack");
		realPack.SourceType = IconPackSourceType.ExtensionStore;
		realPack.SourceId = "com.acme.real";
		await _harness.Cache.AddOrUpdatePack(realPack);
		_installations.Save(new StoreInstallationRecord
		{
			Origin = "https://registry.test/",
			Kind = StoreExtensionKind.IconPack,
			PackageId = "com.acme.real",
			Version = "1.0.0",
			InstalledAt = DateTimeOffset.UtcNow,
			TargetIds = [realPack.Id]
		});

		var reconciler = new StoreInstallationReconciler(_installations, _harness.Cache);
		reconciler.PruneOrphanedIconPackRecords();

		var updates = _updateDetector.Check();

		Assert.Multiple(() =>
		{
			Assert.That(updates.Any(u => u.PackageId == IconPackId), Is.False);
			Assert.That(_updateState.Current.Any(u => u.PackageId == IconPackId), Is.False);

			var realUpdate = updates.Single(u => u.PackageId == "com.acme.real");
			Assert.That(realUpdate.InstalledVersion, Is.EqualTo("1.0.0"));
			Assert.That(realUpdate.LatestVersion, Is.EqualTo("2.0.0"));
		});
	}

	// Scenario 8: goes through the actual wire handler so the DTO values pinned here - not just the
	// descriptor the registry hands back - are what a client receives. Inverting CanDelete in
	// IconMapper.ToDto, or mis-casing OwnerKind, must fail this test.
	[Test]
	public async Task GetIconPacks_reports_owner_kind_and_delete_policy_per_pack()
	{
		SwapCatalog("1.0.0");
		var storePack = await InstallStorePack("1.0.0");
		var userPack = await _harness.CreatePack("User Pack");
		var defaultPack = await _harness.CreatePack("Imported Icons", isDefault: true);
		var forbiddenPack = await _harness.CreatePack("Plugin Pack");
		var forbidding = new TestIconPackOwner(canRemove: false, ownedPackId: forbiddenPack.Id);
		var registry = new IconPackOwnerRegistry([_storeOwner, forbidding]);
		var handler = new GetIconPacksRequestMessageHandler(_harness.Cache, registry);

		var response = await handler.Handle(new GetIconPacksRequest(), CancellationToken.None);

		var storeDto = response.Packs.Single(p => p.Id == storePack.Id.ToString());
		var userDto = response.Packs.Single(p => p.Id == userPack.Id.ToString());
		var defaultDto = response.Packs.Single(p => p.Id == defaultPack.Id.ToString());
		var forbiddenDto = response.Packs.Single(p => p.Id == forbiddenPack.Id.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(storeDto.OwnerKind, Is.EqualTo("Store"));
			Assert.That(storeDto.CanDelete, Is.True);
			Assert.That(userDto.OwnerKind, Is.EqualTo("User"));
			Assert.That(userDto.CanDelete, Is.True);
			Assert.That(defaultDto.OwnerKind, Is.EqualTo("User"));
			Assert.That(defaultDto.CanDelete, Is.True);
			Assert.That(forbiddenDto.OwnerKind, Is.EqualTo("Plugin"));
			Assert.That(forbiddenDto.CanDelete, Is.False);
		});
	}

	[Test]
	public async Task A_pack_stamped_ExtensionStore_with_no_matching_record_is_reported_and_deletable_as_user_created()
	{
		var pack = await _harness.CreatePack("Orphan Stamp");
		pack.SourceType = IconPackSourceType.ExtensionStore;
		pack.SourceId = "com.acme.orphan-stamp";
		await _harness.Cache.AddOrUpdatePack(pack);
		var registry = new IconPackOwnerRegistry([_storeOwner]);
		var service = CreateService(_storeOwner);

		var descriptor = registry.Describe(pack);
		var result = await service.Delete(pack.Id);

		Assert.Multiple(() =>
		{
			Assert.That(descriptor.Kind, Is.EqualTo(IconPackOwnerKind.User));
			Assert.That(descriptor.CanRemove, Is.True);
			Assert.That(result.Success, Is.True);
			Assert.That(_harness.Cache.GetPackById(pack.Id), Is.Null);
		});
	}

	private sealed class TestIconPackOwner : IIconPackOwner
	{
		private readonly bool _canRemove;
		private readonly Guid? _ownedPackId;

		public TestIconPackOwner(bool canRemove, Guid? ownedPackId = null)
		{
			_canRemove = canRemove;
			_ownedPackId = ownedPackId;
		}

		public bool Owns(IconPackEntity pack) => _ownedPackId is null || _ownedPackId == pack.Id;

		public IconPackOwnerDescriptor Describe(IconPackEntity pack) => new(IconPackOwnerKind.Plugin, _canRemove);

		public Task Release(IconPackEntity pack) => Task.CompletedTask;
	}
}
