using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.Keys;
using MacroDeck.Signing.Packages;
using MacroDeck.Signing.TestSupport;
using MacroDeckHost.Application.Packaging;
using MacroDeckHost.Application.Persistence.Icons;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Application.Store.Updates;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Plugins.Installation;
using MacroDeckHost.Infrastructure.Store;
using MacroDeckHost.Tests.UnitTests.Icons;
using MacroDeckHost.Tests.UnitTests.Plugins;
using MacroDeckHost.Tests.UnitTests.Plugins.Installation;
using MacroDeckHost.Tests.UnitTests.Plugins.Trust;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Infrastructure.Plugins.Trust;

namespace MacroDeckHost.Tests.UnitTests.Store;

/// <summary>The registry stores a bare hex digest, while the plugin installer's artifact acquirer expects
/// it prefixed with "sha256:" - a mismatch between those two formats would make either every install fail
/// or every checksum check pass regardless of content. These tests pin the correct behaviour: an artifact
/// whose bytes genuinely do not match the declared digest is refused, for both an installable kind
/// (plugin, which round-trips through the real acquirer) and a downloaded kind (icon pack).</summary>
[TestFixture]
internal sealed class StoreInstallExecutorTests
{
	private const string PluginId = "com.acme.store-plugin";
	private const string IconPackId = "com.acme.store-icons";

	private static readonly byte[] _servedBytes = "these-bytes-do-not-match-the-declared-digest"u8.ToArray();
	private const string WrongDeclaredSha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

	private TestPaths _paths = null!;
	private FakeUrlHttpClientFactory _httpClientFactory = null!;
	private StoreOperationTracker _tracker = null!;
	private StoreCatalog _catalog = null!;
	private JsonStoreInstallationStore _installations = null!;
	private PluginInstallationCatalog _pluginCatalog = null!;
	private PluginInstaller _pluginInstaller = null!;
	private IconTestHarness _iconHarness = null!;
	private StoreInstallExecutor _executor = null!;
	private IServiceScopeFactory _scopeFactory = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.PluginsDirectory);
		Directory.CreateDirectory(_paths.PluginStagingDirectory);
		Directory.CreateDirectory(_paths.PluginCacheDirectory);
		Directory.CreateDirectory(_paths.StoreDirectory);
		Directory.CreateDirectory(_paths.StoreStagingDirectory);

		_httpClientFactory = new FakeUrlHttpClientFactory { Body = _servedBytes };
		_tracker = new StoreOperationTracker(new InMemoryStoreOperationStore(), TimeProvider.System);
		_catalog = new StoreCatalog();
		_installations = new JsonStoreInstallationStore(_paths, Serilog.Core.Logger.None);
		_iconHarness = new IconTestHarness();

		var pluginManifestReader = new PluginManifestReader();
		var pluginOptions = PluginInstallerOptions.Default;
		_pluginCatalog = new PluginInstallationCatalog(_paths, Serilog.Core.Logger.None);
		var sessionRegistry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);

		var services = new ServiceCollection();
		services.AddSingleton<IPluginRegistrationRepository, InMemoryPluginRegistrationRepository>();
		services.AddSingleton<IPluginAccessTokenRepository, InMemoryPluginAccessTokenRepository>();
		services.AddSingleton<IPluginTrustRecordRepository,
			InMemoryPluginTrustRecordRepository>();
		services.AddSingleton<MacroDeckHost.Application.Plugins.Trust.IPluginTrustBaseline, FakePluginTrustBaseline>();
		services.AddScoped<IIconPackRestoreService>(_ => _iconHarness.RestoreService);
		services.AddSingleton<Mediator.IMediator>(_iconHarness.Mediator);
		services.AddScoped<IProfilePortabilityService>(_ => throw new NotSupportedException());
		var provider = services.BuildServiceProvider();
		_scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

		_pluginInstaller = new PluginInstaller(_paths,
			new PluginArtifactReader(pluginManifestReader, Serilog.Core.Logger.None),
			new PluginArtifactAcquirer(_httpClientFactory, pluginOptions, Serilog.Core.Logger.None),
			new PluginArtifactCache(_paths, pluginOptions, Serilog.Core.Logger.None),
			new FakePluginTrustEvaluator(),
			new PluginDependencyResolver(_pluginCatalog, pluginManifestReader),
			pluginManifestReader,
			_pluginCatalog,
			new FakeInstallSupervisor(_pluginCatalog, sessionRegistry),
			new FakeIntegrationRegistrar(),
			sessionRegistry,
			new PluginTakeoverRegistry(),
			provider.GetRequiredService<IServiceScopeFactory>(),
			pluginOptions,
			TimeProvider.System,
			Serilog.Core.Logger.None);

		var downloader = new StoreArtifactDownloader(_httpClientFactory,
			StoreRegistryOptions.Default,
			_paths,
			TimeProvider.System);

		_executor = new StoreInstallExecutor(_catalog,
			new StoreCatalogQueryService(_catalog, _pluginCatalog, _installations, new JsonStoreTestInstallationStore(_paths, Serilog.Core.Logger.None), new InstalledPluginSigners()),
			_tracker,
			downloader,
			_pluginInstaller,
			_iconHarness.Cache,
			_installations,
			new StoreInstallConsent(),
			new StoreInstallBackupBatches(),
			provider.GetRequiredService<IServiceScopeFactory>(),
			_paths,
			StoreRegistryOptions.Default,
			TimeProvider.System,
			Serilog.Core.Logger.None);
	}

	[TearDown]
	public void TearDown()
	{
		_paths.Cleanup();
		_iconHarness.Dispose();
	}

	private static StoreCatalogEntry PluginEntry() => new()
	{
		Kind = StoreExtensionKind.Plugin,
		Id = PluginId,
		Name = "Store Plugin",
		LatestVersion = "1.0.0",
		LatestRelease = new StoreReleaseManifest
		{
			Version = "1.0.0",
			ArtifactUrl = new Uri("https://cdn.example/store-plugin.macroDeckPlugin"),
			Sha256 = WrongDeclaredSha256,
			Size = _servedBytes.LongLength
		}
	};

	private static StoreCatalogEntry IconPackEntry() => new()
	{
		Kind = StoreExtensionKind.IconPack,
		Id = IconPackId,
		Name = "Store Icons",
		LatestVersion = "1.0.0",
		LatestRelease = new StoreReleaseManifest
		{
			Version = "1.0.0",
			ArtifactUrl = new Uri("https://cdn.example/store-icons.macroDeckIconPack"),
			Sha256 = WrongDeclaredSha256,
			Size = _servedBytes.LongLength
		}
	};

	[Test]
	public async Task
		A_plugin_artifact_that_does_not_hash_to_the_declared_digest_fails_with_checksum_mismatch_and_installs_nothing()
	{
		_catalog.Swap(new StoreCatalogSnapshot { Sequence = 1, Entries = [PluginEntry()] });
		var operation = _tracker.Create(StoreOperationKind.Install,
			StoreExtensionKind.Plugin,
			PluginId,
			"1.0.0",
			"Store Plugin",
			previousVersion: null);

		await _executor.Execute(operation.Id);

		var result = _tracker.Find(operation.Id)!;
		Assert.Multiple(() =>
		{
			Assert.That(result.State, Is.EqualTo(StoreOperationState.Failed));
			Assert.That(result.Error, Is.EqualTo(StoreOperationError.ChecksumMismatch));
			Assert.That(_pluginCatalog.Discover(), Is.Empty, "no plugin tree must be left behind");
		});
	}

	[Test]
	public async Task A_plugin_install_refused_during_a_takeover_fails_with_its_own_error_and_the_installers_message()
	{
		var installer = new Plugins.Installation.FakePluginInstaller
		{
			ResultToReturn = PluginInstallResult.Fail(PluginInstallError.Failed, "installer message", PluginId) with
			{
				BlockedByDevelopmentTakeover = true
			}
		};
		var executor = new StoreInstallExecutor(_catalog,
			new StoreCatalogQueryService(_catalog, _pluginCatalog, _installations, new JsonStoreTestInstallationStore(_paths, Serilog.Core.Logger.None), new InstalledPluginSigners()),
			_tracker,
			new StoreArtifactDownloader(_httpClientFactory, StoreRegistryOptions.Default, _paths, TimeProvider.System),
			installer,
			_iconHarness.Cache,
			_installations,
			new StoreInstallConsent(),
			new StoreInstallBackupBatches(),
			_scopeFactory,
			_paths,
			StoreRegistryOptions.Default,
			TimeProvider.System,
			Serilog.Core.Logger.None);
		_catalog.Swap(new StoreCatalogSnapshot { Sequence = 1, Entries = [PluginEntry()] });
		var operation = _tracker.Create(StoreOperationKind.Install,
			StoreExtensionKind.Plugin,
			PluginId,
			"1.0.0",
			"Store Plugin",
			previousVersion: null);

		await executor.Execute(operation.Id);

		var result = _tracker.Find(operation.Id)!;
		Assert.Multiple(() =>
		{
			Assert.That(result.State, Is.EqualTo(StoreOperationState.Failed));
			Assert.That(result.Error, Is.EqualTo(StoreOperationError.InstallBlockedByTakeover));
			Assert.That(result.ErrorMessage, Is.EqualTo("installer message"));
		});
	}

	[Test]
	public async Task A_plugin_update_reports_installing_once_the_download_is_done_and_remembers_it_came_from_the_store()
	{
		var installer = new Plugins.Installation.FakePluginInstaller
		{
			ResultToReturn = PluginInstallResult.Ok(PluginId, "1.1.0", "1.0.0", activated: true)
		};
		var batches = new StoreInstallBackupBatches();
		var executor = new StoreInstallExecutor(_catalog,
			new StoreCatalogQueryService(_catalog, _pluginCatalog, _installations, new JsonStoreTestInstallationStore(_paths, Serilog.Core.Logger.None), new InstalledPluginSigners()),
			_tracker,
			new StoreArtifactDownloader(_httpClientFactory, StoreRegistryOptions.Default, _paths, TimeProvider.System),
			installer,
			_iconHarness.Cache,
			_installations,
			new StoreInstallConsent(),
			batches,
			_scopeFactory,
			_paths,
			StoreRegistryOptions.Default,
			TimeProvider.System,
			Serilog.Core.Logger.None);
		_catalog.Swap(new StoreCatalogSnapshot { Sequence = 1, Entries = [PluginEntry()] });
		var operation = _tracker.Create(StoreOperationKind.Update,
			StoreExtensionKind.Plugin,
			PluginId,
			"1.1.0",
			"Store Plugin",
			previousVersion: "1.0.0");
		batches.Record(operation.Id, "batch-1");
		var states = new List<StoreOperationState>();
		_tracker.Changed += changed =>
		{
			if (changed.Id == operation.Id)
			{
				states.Add(changed.State);
			}
		};

		await executor.Execute(operation.Id);

		Assert.Multiple(() =>
		{
			Assert.That(states, Is.EqualTo(new[]
			{
				StoreOperationState.Downloading, StoreOperationState.Installing, StoreOperationState.Completed
			}));
			Assert.That(installer.LastInstallRequest!.BackupBatchId, Is.EqualTo("batch-1"));
			Assert.That(_installations.Find(StoreExtensionKind.Plugin, PluginId)?.Version, Is.EqualTo("1.1.0"));
		});
	}

	[Test]
	public async Task
		An_icon_pack_artifact_that_does_not_hash_to_the_declared_digest_fails_with_checksum_mismatch_and_installs_nothing()
	{
		_catalog.Swap(new StoreCatalogSnapshot { Sequence = 1, Entries = [IconPackEntry()] });
		var operation = _tracker.Create(StoreOperationKind.Install,
			StoreExtensionKind.IconPack,
			IconPackId,
			"1.0.0",
			"Store Icons",
			previousVersion: null);

		await _executor.Execute(operation.Id);

		var result = _tracker.Find(operation.Id)!;
		Assert.Multiple(() =>
		{
			Assert.That(result.State, Is.EqualTo(StoreOperationState.Failed));
			Assert.That(result.Error, Is.EqualTo(StoreOperationError.ChecksumMismatch));
			Assert.That(_iconHarness.Cache.GetAllPacks(), Is.Empty, "no icon pack must be left behind");
			Assert.That(_installations.Find(StoreExtensionKind.IconPack, IconPackId), Is.Null);
		});
	}

	[Test]
	public async Task An_installed_Store_icon_pack_is_announced_as_Store_owned_and_read_only()
	{
		var artifact = await BuildIconPackArtifact("1.0.0");
		ServeIconPack(artifact, "1.0.0");
		var operation = _tracker.Create(StoreOperationKind.Install,
			StoreExtensionKind.IconPack,
			IconPackId,
			"1.0.0",
			"Store Icons",
			previousVersion: null);
		_iconHarness.Mediator.Published.Clear();

		await _executor.Execute(operation.Id);

		var pack = _iconHarness.Cache.GetAllPacks().Single();
		var announced = _iconHarness.Mediator.Published.OfType<IconPackUpdatedNotification>().LastOrDefault();
		Assert.Multiple(() =>
		{
			Assert.That(_tracker.Find(operation.Id)!.State, Is.EqualTo(StoreOperationState.Completed));
			Assert.That(announced?.Pack.Id, Is.EqualTo(pack.Id));
			Assert.That(OwnerRegistry().Describe(announced!.Pack).Kind, Is.EqualTo(IconPackOwnerKind.Store));
			Assert.That(OwnerRegistry().IsReadOnly(pack), Is.True);
		});
	}

	[Test]
	public async Task A_Store_update_of_an_installed_read_only_icon_pack_still_installs()
	{
		ServeIconPack(await BuildIconPackArtifact("1.0.0"), "1.0.0");
		var install = _tracker.Create(StoreOperationKind.Install,
			StoreExtensionKind.IconPack,
			IconPackId,
			"1.0.0",
			"Store Icons",
			previousVersion: null);
		await _executor.Execute(install.Id);
		var installed = _iconHarness.Cache.GetAllPacks().Single();

		ServeIconPack(await BuildIconPackArtifact("1.1.0"), "1.1.0");
		var update = _tracker.Create(StoreOperationKind.Update,
			StoreExtensionKind.IconPack,
			IconPackId,
			"1.1.0",
			"Store Icons",
			previousVersion: "1.0.0");
		await _executor.Execute(update.Id);

		var packs = _iconHarness.Cache.GetAllPacks();
		Assert.Multiple(() =>
		{
			Assert.That(_tracker.Find(update.Id)!.State, Is.EqualTo(StoreOperationState.Completed));
			Assert.That(packs, Has.Count.EqualTo(1));
			Assert.That(packs.Single().Id, Is.EqualTo(installed.Id));
			Assert.That(packs.Single().Version, Is.EqualTo("1.1.0"));
			Assert.That(_installations.Find(StoreExtensionKind.IconPack, IconPackId)!.Version, Is.EqualTo("1.1.0"));
		});
	}

	[Test]
	public async Task An_older_version_the_user_chose_is_installed_from_that_releases_own_artifact_and_held()
	{
		var older = PluginArtifact("1.0.0");
		ServePlugins(older, PluginArtifact("2.0.0"));
		_httpClientFactory.Body = older.Bytes;

		var operation = await Run(StoreOperationKind.Install, "1.0.0", previousVersion: null, pinned: true);

		var record = _installations.Find(StoreExtensionKind.Plugin, PluginId);
		Assert.Multiple(() =>
		{
			Assert.That(operation.State, Is.EqualTo(StoreOperationState.Completed), operation.ErrorMessage);
			Assert.That(ActivePluginVersion(), Is.EqualTo("1.0.0"));
			Assert.That(record?.Version, Is.EqualTo("1.0.0"));
			Assert.That(record?.ArtifactSha256, Is.EqualTo(older.Sha256));
			Assert.That(record?.Held, Is.True);
		});
	}

	[Test]
	public async Task A_downgrade_onto_a_retained_version_and_the_update_back_both_replace_the_kept_folder()
	{
		var older = PluginArtifact("1.0.0");
		var latest = PluginArtifact("2.0.0");
		ServePlugins(older, latest);
		_httpClientFactory.Body = older.Bytes;
		await Run(StoreOperationKind.Install, "1.0.0", previousVersion: null, pinned: true);
		_httpClientFactory.Body = latest.Bytes;
		var update = await Run(StoreOperationKind.Update, "2.0.0", previousVersion: "1.0.0", pinned: false);
		Assert.That(update.State, Is.EqualTo(StoreOperationState.Completed), update.ErrorMessage);
		Assert.That(Directory.Exists(PluginVersionDirectory("1.0.0")), Is.True, "the update keeps 1.0.0 for rollback");

		_httpClientFactory.Body = older.Bytes;
		var downgrade = await Run(StoreOperationKind.Update, "1.0.0", previousVersion: "2.0.0", pinned: true);
		var heldAfterDowngrade = _installations.Find(StoreExtensionKind.Plugin, PluginId)?.Held;
		var activeAfterDowngrade = ActivePluginVersion();

		_httpClientFactory.Body = latest.Bytes;
		var backUp = await Run(StoreOperationKind.Update, "2.0.0", previousVersion: "1.0.0", pinned: false);

		var record = _installations.Find(StoreExtensionKind.Plugin, PluginId);
		Assert.Multiple(() =>
		{
			Assert.That(downgrade.State, Is.EqualTo(StoreOperationState.Completed), downgrade.ErrorMessage);
			Assert.That(activeAfterDowngrade, Is.EqualTo("1.0.0"));
			Assert.That(heldAfterDowngrade, Is.True);
			Assert.That(backUp.State, Is.EqualTo(StoreOperationState.Completed), backUp.ErrorMessage);
			Assert.That(ActivePluginVersion(), Is.EqualTo("2.0.0"));
			Assert.That(record?.Version, Is.EqualTo("2.0.0"));
			Assert.That(record?.Held, Is.False);
		});
	}

	[Test]
	public async Task An_install_that_names_no_version_installs_the_latest_release()
	{
		var latest = PluginArtifact("2.0.0");
		ServePlugins(PluginArtifact("1.0.0"), latest);
		_httpClientFactory.Body = latest.Bytes;

		var operation = await Run(StoreOperationKind.Install, "2.0.0", previousVersion: null, pinned: false);

		Assert.Multiple(() =>
		{
			Assert.That(operation.State, Is.EqualTo(StoreOperationState.Completed), operation.ErrorMessage);
			Assert.That(ActivePluginVersion(), Is.EqualTo("2.0.0"));
			Assert.That(_installations.Find(StoreExtensionKind.Plugin, PluginId)?.Held, Is.False);
		});
	}

	[Test]
	public async Task Retrying_an_unpinned_update_after_the_registry_moved_on_installs_the_new_latest()
	{
		var installer = new Plugins.Installation.FakePluginInstaller
		{
			ResultToReturn = PluginInstallResult.Ok(PluginId, "3.0.0", "1.0.0", activated: true)
		};
		var executor = ExecutorWith(installer);
		ServePlugins(PluginArtifact("1.0.0"), PluginArtifact("3.0.0"));
		var operation = _tracker.Create(StoreOperationKind.Update,
			StoreExtensionKind.Plugin,
			PluginId,
			"2.0.0",
			"Store Plugin",
			previousVersion: "1.0.0");

		await executor.Execute(operation.Id);

		var record = _installations.Find(StoreExtensionKind.Plugin, PluginId);
		Assert.Multiple(() =>
		{
			Assert.That(_tracker.Find(operation.Id)!.State, Is.EqualTo(StoreOperationState.Completed));
			Assert.That(installer.LastInstallSource?.Url,
				Is.EqualTo(new Uri("https://cdn.example/store-plugin-3.0.0.macroDeckPlugin")));
			Assert.That(record?.Version, Is.EqualTo("3.0.0"));
			Assert.That(record?.Held, Is.False);
		});
	}

	[Test]
	public async Task The_latest_release_installs_although_an_older_version_is_withdrawn()
	{
		var latest = PluginArtifact("2.0.0");
		ServePlugins(PluginArtifact("1.0.0"), latest);
		Withdraw("1.0.0");
		_httpClientFactory.Body = latest.Bytes;

		var operation = await Run(StoreOperationKind.Install, "2.0.0", previousVersion: null, pinned: false);

		Assert.Multiple(() =>
		{
			Assert.That(operation.State, Is.EqualTo(StoreOperationState.Completed), operation.ErrorMessage);
			Assert.That(ActivePluginVersion(), Is.EqualTo("2.0.0"));
		});
	}

	[Test]
	public async Task A_chosen_version_the_registry_withdrew_is_refused_as_removed()
	{
		var older = PluginArtifact("1.0.0");
		ServePlugins(older, PluginArtifact("2.0.0"));
		Withdraw("1.0.0");
		_httpClientFactory.Body = older.Bytes;

		var operation = await Run(StoreOperationKind.Install, "1.0.0", previousVersion: null, pinned: true);

		Assert.Multiple(() =>
		{
			Assert.That(operation.State, Is.EqualTo(StoreOperationState.Failed));
			Assert.That(operation.Error, Is.EqualTo(StoreOperationError.PackageRemoved));
			Assert.That(ActivePluginVersion(), Is.Null);
		});
	}

	[Test]
	public async Task A_package_whose_latest_version_is_withdrawn_is_refused_as_removed()
	{
		var latest = PluginArtifact("2.0.0");
		ServePlugins(PluginArtifact("1.0.0"), latest);
		Withdraw("2.0.0");
		_httpClientFactory.Body = latest.Bytes;

		var operation = await Run(StoreOperationKind.Install, "2.0.0", previousVersion: null, pinned: false);

		Assert.Multiple(() =>
		{
			Assert.That(operation.State, Is.EqualTo(StoreOperationState.Failed));
			Assert.That(operation.Error, Is.EqualTo(StoreOperationError.PackageRemoved));
		});
	}

	[Test]
	public async Task A_version_that_disappeared_from_the_registry_fails_as_not_found_and_is_not_retried()
	{
		var installer = new Plugins.Installation.FakePluginInstaller();
		var executor = ExecutorWith(installer);
		ServePlugins(PluginArtifact("2.0.0"));
		var operation = _tracker.Create(StoreOperationKind.Install,
			StoreExtensionKind.Plugin,
			PluginId,
			"1.0.0",
			"Store Plugin",
			previousVersion: null,
			versionPinned: true);

		await executor.Execute(operation.Id);

		var result = _tracker.Find(operation.Id)!;
		Assert.Multiple(() =>
		{
			Assert.That(result.State, Is.EqualTo(StoreOperationState.Failed));
			Assert.That(result.Error, Is.EqualTo(StoreOperationError.VersionNotFound));
			Assert.That(result.CanRetry, Is.False);
			Assert.That(installer.LastInstallRequest, Is.Null);
		});
	}

	[TestCase("1.0.0+build.7", false)]
	[TestCase("2.0.0", true)]
	public async Task Only_a_version_other_than_the_active_one_replaces_an_existing_version_folder(string target,
		bool forced)
	{
		var installer = new Plugins.Installation.FakePluginInstaller
		{
			ResultToReturn = PluginInstallResult.Ok(PluginId, target, "1.0.0", activated: true)
		};
		var executor = ExecutorWith(installer);
		await InstallActivePlugin(PluginArtifact("1.0.0"));
		ServePlugins(PluginArtifact("1.0.0"), PluginArtifact("2.0.0"));
		var operation = _tracker.Create(StoreOperationKind.Update,
			StoreExtensionKind.Plugin,
			PluginId,
			target,
			"Store Plugin",
			previousVersion: "1.0.0",
			versionPinned: true);

		await executor.Execute(operation.Id);

		Assert.That(installer.LastInstallRequest?.Force, Is.EqualTo(forced));
	}

	[TestCase("1.0.0", "2.0.0", StoreDownloadOperation.Update)]
	[TestCase("1.0.0", "1.0.0", StoreDownloadOperation.Repair)]
	public async Task The_download_is_described_against_the_version_actually_installed(string target,
		string installed,
		StoreDownloadOperation expected)
	{
		var installer = new Plugins.Installation.FakePluginInstaller
		{
			ResultToReturn = PluginInstallResult.Ok(PluginId, target, installed, activated: true)
		};
		var executor = ExecutorWith(installer);
		ServePlugins(PluginArtifact("1.0.0"), PluginArtifact("2.0.0"));
		var operation = _tracker.Create(StoreOperationKind.Update,
			StoreExtensionKind.Plugin,
			PluginId,
			target,
			"Store Plugin",
			previousVersion: installed,
			versionPinned: true);

		await executor.Execute(operation.Id);

		var metadata = installer.LastInstallSource?.DownloadMetadata;
		Assert.Multiple(() =>
		{
			Assert.That(installer.LastInstallSource?.Url,
				Is.EqualTo(new Uri($"https://cdn.example/store-plugin-{target}.macroDeckPlugin")));
			Assert.That(metadata?.Operation, Is.EqualTo(expected));
			Assert.That(metadata?.CurrentVersion, Is.EqualTo(installed));
		});
	}

	[TestCase(PluginIncompatibility.HostTooOld, StoreOperationError.RequiresNewerMacroDeck)]
	[TestCase(PluginIncompatibility.HostTooNew, StoreOperationError.Incompatible)]
	[TestCase(PluginIncompatibility.Unknown, StoreOperationError.Incompatible)]
	public async Task An_incompatible_plugin_says_whether_a_newer_Macro_Deck_would_accept_it(
		PluginIncompatibility direction,
		StoreOperationError expected)
	{
		var executor = ExecutorWith(new Plugins.Installation.FakePluginInstaller
		{
			ResultToReturn = PluginInstallResult.Fail(PluginInstallError.Incompatible, "needs another host") with
			{
				Incompatibility = direction
			}
		});
		ServePlugins(PluginArtifact("1.0.0"));
		var operation = _tracker.Create(StoreOperationKind.Install,
			StoreExtensionKind.Plugin,
			PluginId,
			"1.0.0",
			"Store Plugin",
			previousVersion: null);

		await executor.Execute(operation.Id);

		Assert.That(_tracker.Find(operation.Id)!.Error, Is.EqualTo(expected));
	}

	[Test]
	public async Task A_plugin_needing_a_newer_plugin_protocol_asks_for_a_newer_Macro_Deck()
	{
		var artifact = PluginArtifact("1.0.0",
			"""
			"compatibility": { "protocol": { "minimum": 99, "maximum": 100 } }
			""");
		ServePlugins(artifact);
		_httpClientFactory.Body = artifact.Bytes;

		var operation = await Run(StoreOperationKind.Install, "1.0.0", previousVersion: null, pinned: false);

		Assert.Multiple(() =>
		{
			Assert.That(operation.Error, Is.EqualTo(StoreOperationError.RequiresNewerMacroDeck));
			Assert.That(operation.CanRetry, Is.False);
		});
	}

	[TestCase(PluginInstallError.SignatureUnverifiable, StoreOperationError.SignatureUnverifiable)]
	[TestCase(PluginInstallError.ArtifactLimitExceeded, StoreOperationError.MalformedPackage)]
	[TestCase(PluginInstallError.IdMismatch, StoreOperationError.MalformedPackage)]
	[TestCase(PluginInstallError.SignatureRevoked, StoreOperationError.SignatureUntrusted)]
	public async Task A_plugin_refusal_is_reported_with_the_store_error_that_describes_it(PluginInstallError error,
		StoreOperationError expected)
	{
		var executor = ExecutorWith(new Plugins.Installation.FakePluginInstaller
		{
			ResultToReturn = PluginInstallResult.Fail(error, "refused")
		});
		ServePlugins(PluginArtifact("1.0.0"));
		var operation = _tracker.Create(StoreOperationKind.Install,
			StoreExtensionKind.Plugin,
			PluginId,
			"1.0.0",
			"Store Plugin",
			previousVersion: null);

		await executor.Execute(operation.Id);

		Assert.That(_tracker.Find(operation.Id)!.Error, Is.EqualTo(expected));
	}

	[Test]
	public async Task An_older_icon_pack_version_is_installed_and_recorded_as_held()
	{
		var older = await BuildIconPackArtifact("1.0.0");
		var latest = await BuildIconPackArtifact("2.0.0");
		_httpClientFactory.Body = older;
		_catalog.Swap(new StoreCatalogSnapshot
		{
			Sequence = 1,
			Entries =
			[
				IconPackEntry() with
				{
					LatestVersion = "2.0.0",
					LatestRelease = IconPackRelease("2.0.0", latest),
					Releases = [IconPackRelease("2.0.0", latest), IconPackRelease("1.0.0", older)]
				}
			]
		});
		var operation = _tracker.Create(StoreOperationKind.Install,
			StoreExtensionKind.IconPack,
			IconPackId,
			"1.0.0",
			"Store Icons",
			previousVersion: null,
			versionPinned: true);

		await _executor.Execute(operation.Id);

		var record = _installations.Find(StoreExtensionKind.IconPack, IconPackId);
		Assert.Multiple(() =>
		{
			Assert.That(_tracker.Find(operation.Id)!.State, Is.EqualTo(StoreOperationState.Completed));
			Assert.That(_iconHarness.Cache.GetAllPacks().Single().Version, Is.EqualTo("1.0.0"));
			Assert.That(record?.Version, Is.EqualTo("1.0.0"));
			Assert.That(record?.Held, Is.True);
		});
	}

	private static StoreReleaseManifest IconPackRelease(string version, byte[] artifact) => new()
	{
		Version = version,
		ArtifactUrl = new Uri($"https://cdn.example/store-icons-{version}.macroDeckIconPack"),
		Sha256 = Convert.ToHexStringLower(SHA256.HashData(artifact)),
		Size = artifact.LongLength
	};

	private sealed record BuiltPlugin(string Version, string Path, byte[] Bytes)
	{
		public string Sha256 => Convert.ToHexStringLower(SHA256.HashData(Bytes));
	}

	private BuiltPlugin PluginArtifact(string version, string? extraManifestBlocks = null)
	{
		var directory = Path.Combine(_paths.BaseDirectory, "artifacts", version);
		Directory.CreateDirectory(directory);
		var path = new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build(version, PluginId, extraManifestBlocks))
			.WithFile(ManifestJson.EntrypointExecutable, "binary " + version)
			.WriteTo(directory);
		return new BuiltPlugin(version, path, File.ReadAllBytes(path));
	}

	private static StoreReleaseManifest Release(string version, BuiltPlugin artifact) => new()
	{
		Version = version,
		ArtifactUrl = new Uri($"https://cdn.example/store-plugin-{version}.macroDeckPlugin"),
		Sha256 = artifact.Sha256,
		Size = artifact.Bytes.LongLength
	};

	private void ServePlugins(params BuiltPlugin[] releases)
	{
		var latest = releases[^1];
		_catalog.Swap(new StoreCatalogSnapshot
		{
			Sequence = 1,
			Entries =
			[
				PluginEntry() with
				{
					LatestVersion = latest.Version,
					LatestRelease = Release(latest.Version, latest),
					Releases = releases.Select(release => Release(release.Version, release)).ToList()
				}
			]
		});
	}

	private void Withdraw(string version) =>
		_catalog.Swap(_catalog.Snapshot with
		{
			RemovedPackages = [new StoreRemovedPackage { Id = PluginId, Version = version, Reason = "Compromised" }]
		});

	private async Task<StoreOperation> Run(StoreOperationKind kind, string version, string? previousVersion, bool pinned)
	{
		var operation = _tracker.Create(kind,
			StoreExtensionKind.Plugin,
			PluginId,
			version,
			"Store Plugin",
			previousVersion,
			versionPinned: pinned);
		await _executor.Execute(operation.Id);
		_pluginCatalog.Invalidate();
		return _tracker.Find(operation.Id)!;
	}

	private string? ActivePluginVersion()
	{
		_pluginCatalog.Invalidate();
		return _pluginCatalog.Discover().SingleOrDefault(plugin => plugin.PluginId == PluginId)?.ActiveVersion?.Version;
	}

	private string PluginVersionDirectory(string version) =>
		PluginInstallPaths.VersionDirectory(_paths.PluginsDirectory, PluginId, version);

	private async Task InstallActivePlugin(BuiltPlugin artifact)
	{
		var result = await _pluginInstaller.Install(PluginArtifactSource.FromPath(artifact.Path), new PluginInstallRequest());
		Assert.That(result.Success, Is.True, result.ErrorMessage);
		_pluginCatalog.Invalidate();
	}

	private StoreInstallExecutor ExecutorWith(IPluginInstaller installer) =>
		new(_catalog,
			new StoreCatalogQueryService(_catalog, _pluginCatalog, _installations, new JsonStoreTestInstallationStore(_paths, Serilog.Core.Logger.None), new InstalledPluginSigners()),
			_tracker,
			new StoreArtifactDownloader(_httpClientFactory, StoreRegistryOptions.Default, _paths, TimeProvider.System),
			installer,
			_iconHarness.Cache,
			_installations,
			new StoreInstallConsent(),
			new StoreInstallBackupBatches(),
			_scopeFactory,
			_paths,
			StoreRegistryOptions.Default,
			TimeProvider.System,
			Serilog.Core.Logger.None);

	private IconPackOwnerRegistry OwnerRegistry()
		=> new([
			new StoreIconPackOwner(_installations,
				new StoreUpdateDetector(_catalog,
					_pluginCatalog,
					_installations,
					new StoreUpdateState(),
					new StoreWithdrawalState(),
					StoreRegistryOptions.Default),
				Serilog.Core.Logger.None)
		]);

	private void ServeIconPack(byte[] artifact, string version)
	{
		_httpClientFactory.Body = artifact;
		_catalog.Swap(new StoreCatalogSnapshot
		{
			Sequence = 1,
			Entries =
			[
				IconPackEntry() with
				{
					LatestVersion = version,
					LatestRelease = new StoreReleaseManifest
					{
						Version = version,
						ArtifactUrl = new Uri($"https://cdn.example/store-icons-{version}.macroDeckIconPack"),
						Sha256 = Convert.ToHexStringLower(SHA256.HashData(artifact)),
						Size = artifact.LongLength
					}
				}
			]
		});
	}

	[Test]
	public async Task A_Store_signed_icon_pack_in_the_format_that_carries_size_files_still_installs_and_serves_sizes()
	{
		var iconId = Guid.CreateVersion7();
		var directory = Directory.CreateTempSubdirectory("macrodeck-store-signed-pack-").FullName;
		try
		{
			var unsigned = Path.Combine(directory, "pack.macroDeckIconPack");
			await File.WriteAllBytesAsync(unsigned, PackWithSizeFiles(iconId, IconImage(1024)));
			var signed = await SignIconPack(unsigned, directory);
			var delivered = await File.ReadAllBytesAsync(signed);
			var verified = await PackageVerifier.VerifyAsync(signed, new PluginManifestReader(), TestPki.Root.PublicKey);
			ServeIconPack(delivered, "1.0.0");

			var operation = await RunIconPack(StoreOperationKind.Install, "1.0.0", previousVersion: null);
			var icon = _iconHarness.Cache.GetIconsByPackId(_iconHarness.Cache.GetAllPacks().Single().Id).Single();
			var edge = await ServedEdge(icon.Id, 128);

			Assert.Multiple(() =>
			{
				Assert.That(verified.Success, Is.True, verified.Message);
				Assert.That(operation.State, Is.EqualTo(StoreOperationState.Completed), operation.ErrorMessage);
				Assert.That(edge, Is.EqualTo(128));
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task A_Store_update_with_unchanged_icons_keeps_the_size_variants_an_older_host_installed()
	{
		var iconId = Guid.CreateVersion7();
		var master = IconImage(1024);
		ServeIconPack(PackWithSizeFiles(iconId, master), "1.0.0");
		await RunIconPack(StoreOperationKind.Install, "1.0.0", previousVersion: null);
		var icon = _iconHarness.Cache.GetIconsByPackId(_iconHarness.Cache.GetAllPacks().Single().Id).Single();
		var olderHostVariant = IconImage(128);
		await _iconHarness.Storage.WriteVariant(icon.PackId, icon.Id, "128", olderHostVariant, CancellationToken.None);
		icon.AvailableSizes = [128];
		await _iconHarness.Cache.UpdateIcon(icon);

		ServeIconPack(PackWithSizeFiles(iconId, master), "1.1.0");
		var update = await RunIconPack(StoreOperationKind.Update, "1.1.0", previousVersion: "1.0.0");
		await using var served = _iconHarness.Storage.OpenVariant(icon.PackId, icon.Id, "128")!;
		using var buffer = new MemoryStream();
		await served.CopyToAsync(buffer);

		Assert.Multiple(() =>
		{
			Assert.That(update.State, Is.EqualTo(StoreOperationState.Completed), update.ErrorMessage);
			Assert.That(_iconHarness.Cache.GetIconById(icon.Id)!.AvailableSizes, Is.EqualTo(new[] { 128 }));
			Assert.That(buffer.ToArray(), Is.EqualTo(olderHostVariant));
		});
	}

	private async Task<StoreOperation> RunIconPack(StoreOperationKind kind, string version, string? previousVersion)
	{
		var operation = _tracker.Create(kind, StoreExtensionKind.IconPack, IconPackId, version, "Store Icons", previousVersion);
		await _executor.Execute(operation.Id);
		return _tracker.Find(operation.Id)!;
	}

	private async Task<int> ServedEdge(Guid iconId, int size)
	{
		var service = new IconService(_iconHarness.Cache,
			_iconHarness.Storage,
			_iconHarness.FallbackStore,
			_iconHarness.VariantDeriver,
			_iconHarness.Coalescer,
			_iconHarness.Mediator,
			OwnerRegistry());
		var result = await service.GetImage(iconId, size, acceptWebp: true, staticFrame: false, CancellationToken.None);
		await using var content = result.Data!.Content;
		using var image = await SixLabors.ImageSharp.Image.LoadAsync(content);
		return Math.Max(image.Width, image.Height);
	}

	private static byte[] PackWithSizeFiles(Guid iconId, byte[] master)
	{
		var contents = new List<(string Path, byte[] Content)> { ($"icons/{iconId}/master.webp", master) };
		contents.AddRange(new[] { 128, 256, 512 }.Select(size => ($"icons/{iconId}/{size}.webp", IconImage(size))));
		var manifest = new IconPackManifest
		{
			Id = Guid.CreateVersion7(),
			Name = "Store Icons",
			Icons =
			[
				new IconManifestEntry
				{
					Id = iconId,
					Name = "home",
					State = IconProcessingState.Ready,
					Width = 1024,
					Height = 1024,
					AvailableSizes = [128, 256, 512],
					MasterContentHash = MasterContentHash.Compute(master).Value
				}
			],
			Files = contents
				.Select(file => new PackageFileDigest
				{
					Path = file.Path,
					Sha256 = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(file.Content)),
					Size = file.Content.Length
				})
				.ToList()
		};

		using var stream = new MemoryStream();
		using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
		{
			WriteEntry(zip, "pack.json", JsonSerializer.SerializeToUtf8Bytes(manifest, PersistenceJsonOptions.Default));
			foreach (var (path, content) in contents)
			{
				WriteEntry(zip, path, content);
			}
		}

		return stream.ToArray();
	}

	private static void WriteEntry(ZipArchive zip, string name, byte[] content)
	{
		using var entry = zip.CreateEntry(name).Open();
		entry.Write(content);
	}

	private static byte[] IconImage(int edge)
	{
		using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(edge,
			edge,
			new SixLabors.ImageSharp.PixelFormats.Rgba32(30, 120, 220));
		using var stream = new MemoryStream();
		SixLabors.ImageSharp.ImageExtensions.SaveAsWebp(image, stream);
		return stream.ToArray();
	}

	private static async Task<string> SignIconPack(string package, string directory)
	{
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(issuer: issuer);
		var chain = SigningCertificateChain.Verify(certificate.CertificateBytes,
			certificate.CertificateSignatureBytes,
			issuer.CertificateBytes,
			issuer.CertificateSignatureBytes,
			TestPki.Root.PublicKey,
			SigningCertificateChain.PackageKeyUsage);
		using var signer = SigningMaterial.Create(certificate.PrivateKey, chain.TrustedCertificate!.Certificate).Material!;
		var output = Path.Combine(directory, "signed.macroDeckIconPack");
		var result = await PackageSigner.SignAsync(package,
			output,
			signer,
			certificate.CertificateBytes,
			certificate.CertificateSignatureBytes,
			issuer.CertificateBytes,
			issuer.CertificateSignatureBytes,
			new PluginManifestReader());
		Assert.That(result.Success, Is.True, result.Message);
		return output;
	}

	private async Task<byte[]> BuildIconPackArtifact(string version)
	{
		var source = await _iconHarness.CreatePack("Store Icons");
		source.Version = version;
		await _iconHarness.Cache.AddOrUpdatePack(source);
		await _iconHarness.AddReadyIcon(source.Id, "home", Encoding.UTF8.GetBytes(version));

		var stream = new MemoryStream();
		var export = await _iconHarness.CreateExportService().Export(source.Id, stream, CancellationToken.None);
		Assert.That(export.Success, Is.True);
		await _iconHarness.Cache.RemovePack(source.Id);
		return stream.ToArray();
	}
}

internal sealed class InMemoryStoreOperationStore : IStoreOperationStore
{
	private IReadOnlyList<StoreOperation> _operations = [];

	public IReadOnlyList<StoreOperation> LoadAll() => _operations;

	public void SaveAll(IReadOnlyList<StoreOperation> operations) => _operations = operations;
}
