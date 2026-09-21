using System.Security.Cryptography;
using System.Text;
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
			new StoreCatalogQueryService(_catalog, _pluginCatalog, _installations),
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
			new StoreCatalogQueryService(_catalog, _pluginCatalog, _installations),
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
			new StoreCatalogQueryService(_catalog, _pluginCatalog, _installations),
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

	private IconPackOwnerRegistry OwnerRegistry()
		=> new([
			new StoreIconPackOwner(_installations,
				new StoreUpdateDetector(_catalog, _pluginCatalog, _installations, new StoreUpdateState()),
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
