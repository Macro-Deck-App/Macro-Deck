using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Plugins.Installation;
using MacroDeckHost.Infrastructure.Store;
using MacroDeckHost.Tests.UnitTests.Icons;
using MacroDeckHost.Tests.UnitTests.Plugins;
using MacroDeckHost.Tests.UnitTests.Plugins.Installation;
using MacroDeckHost.Tests.UnitTests.Plugins.Trust;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

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
		services.AddScoped<IProfilePortabilityService>(_ => throw new NotSupportedException());
		var provider = services.BuildServiceProvider();

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
}

internal sealed class InMemoryStoreOperationStore : IStoreOperationStore
{
	private IReadOnlyList<StoreOperation> _operations = [];

	public IReadOnlyList<StoreOperation> LoadAll() => _operations;

	public void SaveAll(IReadOnlyList<StoreOperation> operations) => _operations = operations;
}
