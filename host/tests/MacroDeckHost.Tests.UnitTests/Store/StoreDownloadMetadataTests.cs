using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Plugins.Installation;
using MacroDeckHost.Infrastructure.Store;
using MacroDeckHost.Tests.UnitTests.Http;
using MacroDeckHost.Tests.UnitTests.Icons;
using MacroDeckHost.Tests.UnitTests.Plugins;
using MacroDeckHost.Tests.UnitTests.Plugins.Installation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Store;

[TestFixture]
internal sealed class StoreDownloadMetadataTests
{
	private const string PluginId = "com.acme.store-plugin";
	private const string IconPackId = "com.acme.store-icons";
	private const string TemplateId = "com.acme.store-template";
	private const string StoreAssets = "https://store-assets.macro-deck.app";
	private const string WorkerUuid = "^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$";

	private TestPaths _paths = null!;
	private RecordingHttpClientFactory _http = null!;
	private StoreOperationTracker _tracker = null!;
	private StoreCatalog _catalog = null!;
	private JsonStoreInstallationStore _installations = null!;
	private FakePluginInstallationCatalog _installedPlugins = null!;
	private IconTestHarness _iconHarness = null!;
	private StoreInstallCoordinator _coordinator = null!;
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

		_http = new RecordingHttpClientFactory { Body = "not-the-declared-bytes"u8.ToArray() };
		_tracker = new StoreOperationTracker(new InMemoryStoreOperationStore(), TimeProvider.System);
		_catalog = new StoreCatalog();
		_installations = new JsonStoreInstallationStore(_paths, Serilog.Core.Logger.None);
		_installedPlugins = new FakePluginInstallationCatalog();
		_iconHarness = new IconTestHarness();

		var manifestReader = new PluginManifestReader();
		var pluginOptions = PluginInstallerOptions.Default;
		var pluginCatalog = new PluginInstallationCatalog(_paths, Serilog.Core.Logger.None);
		var sessionRegistry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);

		var services = new ServiceCollection();
		services.AddSingleton<IPluginRegistrationRepository, InMemoryPluginRegistrationRepository>();
		services.AddSingleton<IPluginAccessTokenRepository, InMemoryPluginAccessTokenRepository>();
		services.AddScoped<IIconPackRestoreService>(_ => _iconHarness.RestoreService);
		services.AddScoped<IProfilePortabilityService>(_ => throw new NotSupportedException());
		var provider = services.BuildServiceProvider();

		var pluginInstaller = new PluginInstaller(_paths,
			new PluginArtifactReader(manifestReader, Serilog.Core.Logger.None),
			new PluginArtifactAcquirer(_http, pluginOptions, Serilog.Core.Logger.None),
			new PluginArtifactCache(_paths, pluginOptions, Serilog.Core.Logger.None),
			new FakePluginTrustEvaluator(),
			new PluginDependencyResolver(pluginCatalog, manifestReader),
			manifestReader,
			pluginCatalog,
			new FakeInstallSupervisor(pluginCatalog, sessionRegistry),
			new FakeIntegrationRegistrar(),
			sessionRegistry,
			new PluginTakeoverRegistry(),
			provider.GetRequiredService<IServiceScopeFactory>(),
			pluginOptions,
			TimeProvider.System,
			Serilog.Core.Logger.None);

		var catalogQuery = new StoreCatalogQueryService(_catalog, _installedPlugins, _installations);
		_coordinator = new StoreInstallCoordinator(catalogQuery,
			_tracker,
			new StoreOperationChannel(),
			new StoreOperationCancellation(),
			new StoreInstallConsent(),
			new StoreInstallBackupBatches());

		_executor = new StoreInstallExecutor(_catalog,
			catalogQuery,
			_tracker,
			new StoreArtifactDownloader(_http, StoreRegistryOptions.Default, _paths, TimeProvider.System),
			pluginInstaller,
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
		_http.Dispose();
	}

	private void Publish(StoreExtensionKind kind, string id, string latestVersion, string artifactUrl) =>
		Publish(Entry(kind, id, latestVersion, artifactUrl));

	private void Publish(params StoreCatalogEntry[] entries) =>
		_catalog.Swap(new StoreCatalogSnapshot { Sequence = 1, Entries = entries });

	private StoreCatalogEntry Entry(StoreExtensionKind kind, string id, string latestVersion, string artifactUrl) =>
		new()
		{
			Kind = kind,
			Id = id,
			Name = id,
			LatestVersion = latestVersion,
			LatestRelease = new StoreReleaseManifest
			{
				Version = latestVersion,
				ArtifactUrl = new Uri(artifactUrl),
				Sha256 = new string('a', 64),
				Size = _http.Body.LongLength
			}
		};

	private void InstalledOutsideTheStore(string pluginId, string version) =>
		_installedPlugins.Plugins.Add(new InstalledPlugin
		{
			PluginId = pluginId,
			PluginDirectory = "/plugins/" + pluginId,
			Versions = [],
			ActiveVersion = new InstalledPluginVersion
			{
				Version = version,
				VersionDirectory = "/plugins/" + pluginId + "/" + version,
				ManifestPath = "/plugins/" + pluginId + "/" + version + "/manifest.json"
			}
		});

	private void RecordedByTheStore(StoreExtensionKind kind, string id, string version) =>
		_installations.Save(new StoreInstallationRecord
		{
			Origin = StoreRegistryOptions.Default.BaseUrl.ToString(),
			Kind = kind,
			PackageId = id,
			Version = version,
			ArtifactSha256 = new string('b', 64),
			DisplayName = id,
			RegistrySequence = 1,
			InstalledAt = DateTimeOffset.UnixEpoch,
			TargetIds = [Guid.NewGuid()]
		});

	private async Task<StoreOperation> Run(StoreOperation operation)
	{
		await _executor.Execute(operation.Id);
		return _tracker.Find(operation.Id)!;
	}

	[Test]
	public async Task A_first_store_install_of_a_plugin_sends_install_and_a_worker_valid_operation_id_without_a_version()
	{
		Publish(StoreExtensionKind.Plugin, PluginId, "1.3.0", $"{StoreAssets}/plugins/{PluginId}/1.3.0/p.macroDeckPlugin");

		var operation = await Run(_coordinator.Install(StoreExtensionKind.Plugin, PluginId));

		var request = _http.Requests.Single();
		Assert.Multiple(() =>
		{
			Assert.That(request.Header("X-MacroDeck-Operation"), Is.EqualTo("install"));
			Assert.That(request.Header("X-MacroDeck-Operation-Id"), Is.EqualTo(operation.Id.ToString("D")));
			Assert.That(request.Header("X-MacroDeck-Operation-Id"), Does.Match(WorkerUuid));
			Assert.That(request.Header("X-MacroDeck-Current-Version"), Is.Null);
		});
	}

	[Test]
	public async Task Updating_a_plugin_installed_outside_the_store_sends_update_with_its_active_version()
	{
		InstalledOutsideTheStore(PluginId, "1.2.0");
		Publish(StoreExtensionKind.Plugin, PluginId, "1.3.0", $"{StoreAssets}/plugins/{PluginId}/1.3.0/p.macroDeckPlugin");

		await Run(_coordinator.Install(StoreExtensionKind.Plugin, PluginId));

		var request = _http.Requests.Single();
		Assert.Multiple(() =>
		{
			Assert.That(request.Header("X-MacroDeck-Operation"), Is.EqualTo("update"));
			Assert.That(request.Header("X-MacroDeck-Current-Version"), Is.EqualTo("1.2.0"));
		});
	}

	[Test]
	public async Task Reinstalling_the_installed_plugin_version_sends_repair_with_that_version()
	{
		InstalledOutsideTheStore(PluginId, "1.3.0");
		Publish(StoreExtensionKind.Plugin, PluginId, "1.3.0", $"{StoreAssets}/plugins/{PluginId}/1.3.0/p.macroDeckPlugin");

		await Run(_coordinator.Install(StoreExtensionKind.Plugin, PluginId));

		var request = _http.Requests.Single();
		Assert.Multiple(() =>
		{
			Assert.That(request.Header("X-MacroDeck-Operation"), Is.EqualTo("repair"));
			Assert.That(request.Header("X-MacroDeck-Current-Version"), Is.EqualTo("1.3.0"));
		});
	}

	[Test]
	public async Task An_icon_pack_update_downloaded_through_the_artifact_downloader_sends_update_with_the_recorded_version()
	{
		RecordedByTheStore(StoreExtensionKind.IconPack, IconPackId, "1.2.0");
		Publish(StoreExtensionKind.IconPack, IconPackId, "1.3.0", $"{StoreAssets}/icon-packs/{IconPackId}/1.3.0/i.macroDeckIconPack");

		var operation = await Run(_coordinator.Install(StoreExtensionKind.IconPack, IconPackId));

		var request = _http.Requests.Single();
		Assert.Multiple(() =>
		{
			Assert.That(request.Header("X-MacroDeck-Operation"), Is.EqualTo("update"));
			Assert.That(request.Header("X-MacroDeck-Current-Version"), Is.EqualTo("1.2.0"));
			Assert.That(request.Header("X-MacroDeck-Operation-Id"), Is.EqualTo(operation.Id.ToString("D")));
			Assert.That(operation.Error, Is.EqualTo(StoreOperationError.ChecksumMismatch),
				"digest verification still applies to a download that carries metadata");
		});
	}

	[Test]
	public async Task Reinstalling_the_recorded_template_version_sends_repair_with_that_version()
	{
		RecordedByTheStore(StoreExtensionKind.ProfileTemplate, TemplateId, "1.3.0");
		Publish(StoreExtensionKind.ProfileTemplate,
			TemplateId,
			"1.3.0",
			$"{StoreAssets}/templates/profiles/{TemplateId}/1.3.0/t.macroDeckProfile");

		await Run(_coordinator.Install(StoreExtensionKind.ProfileTemplate, TemplateId));

		var request = _http.Requests.Single();
		Assert.Multiple(() =>
		{
			Assert.That(request.Header("X-MacroDeck-Operation"), Is.EqualTo("repair"));
			Assert.That(request.Header("X-MacroDeck-Current-Version"), Is.EqualTo("1.3.0"));
		});
	}

	[Test]
	public async Task Retrying_a_failed_install_and_then_the_failed_retry_keeps_the_first_operations_id()
	{
		Publish(StoreExtensionKind.Plugin, PluginId, "1.3.0", $"{StoreAssets}/plugins/{PluginId}/1.3.0/p.macroDeckPlugin");

		var first = await Run(_coordinator.Install(StoreExtensionKind.Plugin, PluginId));
		var retry = await Run(_coordinator.Retry(first.Id)!);
		var retryOfRetry = await Run(_coordinator.Retry(retry.Id)!);

		var ids = _http.Requests.Select(request => request.Header("X-MacroDeck-Operation-Id")).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(new[] { first.State, retry.State, retryOfRetry.State },
				Is.All.EqualTo(StoreOperationState.Failed));
			Assert.That(new[] { first.Id, retry.Id, retryOfRetry.Id }, Is.Unique);
			Assert.That(ids, Has.Count.EqualTo(3));
			Assert.That(ids, Is.All.EqualTo(first.Id.ToString("D")));
		});
	}

	[Test]
	public async Task Retrying_an_operation_restored_from_an_older_host_uses_that_operations_own_id()
	{
		Publish(StoreExtensionKind.Plugin, PluginId, "1.3.0", $"{StoreAssets}/plugins/{PluginId}/1.3.0/p.macroDeckPlugin");
		var restored = new StoreOperation
		{
			Id = Guid.CreateVersion7(),
			Kind = StoreOperationKind.Install,
			ExtensionKind = StoreExtensionKind.Plugin,
			PackageId = PluginId,
			Version = "1.3.0",
			DisplayName = PluginId,
			State = StoreOperationState.Downloading
		};
		_tracker.Restore([restored]);

		await Run(_coordinator.Retry(restored.Id)!);

		Assert.That(_http.Requests.Single().Header("X-MacroDeck-Operation-Id"), Is.EqualTo(restored.Id.ToString("D")));
	}

	[Test]
	public async Task A_new_install_started_after_a_finished_one_gets_a_new_operation_id()
	{
		Publish(StoreExtensionKind.Plugin, PluginId, "1.3.0", $"{StoreAssets}/plugins/{PluginId}/1.3.0/p.macroDeckPlugin");

		var first = await Run(_coordinator.Install(StoreExtensionKind.Plugin, PluginId));
		var second = await Run(_coordinator.Install(StoreExtensionKind.Plugin, PluginId));

		var ids = _http.Requests.Select(request => request.Header("X-MacroDeck-Operation-Id")).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(ids, Is.EqualTo(new[] { first.Id.ToString("D"), second.Id.ToString("D") }));
			Assert.That(ids, Is.Unique);
		});
	}

	[TestCase("https://cdn.example/plugins/p.macroDeckPlugin")]
	[TestCase("https://store-assets.macro-deck.app.evil.example/plugins/p.macroDeckPlugin")]
	[TestCase("https://sub.store-assets.macro-deck.app/plugins/p.macroDeckPlugin")]
	public async Task A_store_package_hosted_anywhere_but_the_store_asset_host_is_downloaded_without_metadata(string url)
	{
		Publish(Entry(StoreExtensionKind.Plugin, PluginId, "1.3.0", url),
			Entry(StoreExtensionKind.IconPack, IconPackId, "1.3.0", url + ".macroDeckIconPack"));

		await Run(_coordinator.Install(StoreExtensionKind.Plugin, PluginId));
		await Run(_coordinator.Install(StoreExtensionKind.IconPack, IconPackId));

		Assert.Multiple(() =>
		{
			Assert.That(_http.Requests, Has.Count.EqualTo(2));
			Assert.That(_http.Requests.Where(request => request.HasMacroDeckHeaders), Is.Empty);
		});
	}

	[TestCase("http://store-assets.macro-deck.app/plugins/p.macroDeckPlugin")]
	[TestCase("https://cdn.example/plugins/p.macroDeckPlugin")]
	[TestCase("https://store-assets.macro-deck.app.evil.example/p.macroDeckPlugin")]
	[TestCase("https://sub.store-assets.macro-deck.app/p.macroDeckPlugin")]
	[TestCase("https://store-assets.macro-deck.app@evil.example/p.macroDeckPlugin")]
	public void Metadata_is_never_applied_to_a_request_for_another_host_or_scheme(string url)
	{
		var metadata = new StoreDownloadMetadata
		{
			Operation = StoreDownloadOperation.Update,
			OperationId = Guid.NewGuid(),
			CurrentVersion = "1.2.0"
		};
		using var request = new HttpRequestMessage(HttpMethod.Get, url);

		metadata.ApplyTo(request);

		Assert.That(request.Headers.Select(header => header.Key), Has.None.StartsWith("X-MacroDeck-"));
	}

	[TestCase("1.2.0\r\nX-Injected: yes")]
	[TestCase("")]
	[TestCase("-1.0")]
	public void An_installed_version_the_worker_would_reject_is_left_out_instead_of_breaking_the_request(string version)
	{
		var metadata = new StoreDownloadMetadata
		{
			Operation = StoreDownloadOperation.Update,
			OperationId = Guid.NewGuid(),
			CurrentVersion = version
		};
		using var request = new HttpRequestMessage(HttpMethod.Get, $"{StoreAssets}/plugins/p.macroDeckPlugin");

		metadata.ApplyTo(request);

		Assert.Multiple(() =>
		{
			Assert.That(request.Headers.Contains("X-MacroDeck-Current-Version"), Is.False);
			Assert.That(request.Headers.GetValues("X-MacroDeck-Operation"), Is.EqualTo(new[] { "update" }));
		});
	}
}
