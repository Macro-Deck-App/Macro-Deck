using System.Security.Cryptography;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Signing.TestSupport;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Application.Plugins.Trust;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Application.Store.Reviews;
using MacroDeckHost.Application.Store.Testing;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Plugins.Installation;
using MacroDeckHost.Infrastructure.Plugins.Trust;
using MacroDeckHost.Infrastructure.Store;
using MacroDeckHost.Tests.UnitTests.Icons;
using MacroDeckHost.Tests.UnitTests.Plugins;
using MacroDeckHost.Tests.UnitTests.Plugins.Installation;
using MacroDeckHost.Tests.UnitTests.Plugins.Trust;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using MacroDeckHost.Application.Plugins.Runtime;

using MacroDeckHost.Tests.UnitTests.Store.Reviews;

namespace MacroDeckHost.Tests.UnitTests.Store;

// A tester installs a build its creator shared before review: unsigned, with consent, without developer mode,
// replacing a trusted Store install. Run against real archives and a real trust evaluator, like the consent tests.
[TestFixture]
internal sealed class StoreTestBuildInstallTests
{
	private const string PluginId = "com.acme.store-consent";

	private TestPaths _paths = null!;
	private FakeUrlHttpClientFactory _httpClientFactory = null!;
	private FakeStorePlatformClient _platform = null!;
	private JsonStoreTestInstallationStore _testInstallations = null!;
	private StoreOperationTracker _tracker = null!;
	private StoreCatalog _catalog = null!;
	private StoreInstallConsent _consent = null!;
	private StoreInstallCoordinator _coordinator = null!;
	private StoreTestService _tests = null!;
	private StoreInstallExecutor _executor = null!;
	private PluginInstallationCatalog _pluginCatalog = null!;
	private PluginInstaller _pluginInstaller = null!;
	private InMemoryPluginTrustRecordRepository _trustRecords = null!;
	private FakeDeveloperModePreferences _preferences = null!;
	private IconTestHarness _iconHarness = null!;
	private string _sourceDirectory = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.PluginsDirectory);
		Directory.CreateDirectory(_paths.PluginStagingDirectory);
		Directory.CreateDirectory(_paths.PluginCacheDirectory);
		Directory.CreateDirectory(_paths.StoreDirectory);
		Directory.CreateDirectory(_paths.StoreStagingDirectory);

		_sourceDirectory = Path.Combine(_paths.BaseDirectory, "artifacts");
		Directory.CreateDirectory(_sourceDirectory);

		_httpClientFactory = new FakeUrlHttpClientFactory();
		_platform = new FakeStorePlatformClient();
		_testInstallations = new JsonStoreTestInstallationStore(_paths, Serilog.Core.Logger.None);
		_tracker = new StoreOperationTracker(new InMemoryStoreOperationStore(), TimeProvider.System);
		_catalog = new StoreCatalog();
		_consent = new StoreInstallConsent();
		_preferences = new FakeDeveloperModePreferences();
		_trustRecords = new InMemoryPluginTrustRecordRepository();
		_iconHarness = new IconTestHarness();

		var manifestReader = new PluginManifestReader();
		var options = PluginInstallerOptions.Default with { ActivationHealthTimeout = TimeSpan.FromSeconds(2) };
		_pluginCatalog = new PluginInstallationCatalog(_paths, Serilog.Core.Logger.None);
		var sessionRegistry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		var installations = new JsonStoreInstallationStore(_paths, Serilog.Core.Logger.None);

		var services = new ServiceCollection();
		services.AddSingleton<IPluginRegistrationRepository, InMemoryPluginRegistrationRepository>();
		services.AddSingleton<IPluginAccessTokenRepository, InMemoryPluginAccessTokenRepository>();
		services.AddSingleton<IPluginTrustRecordRepository>(_trustRecords);
		services.AddSingleton<IPluginTrustBaseline, FakePluginTrustBaseline>();
		services.AddScoped<IAppPreferenceService>(_ => _preferences);
		services.AddScoped<IIconPackRestoreService>(_ => _iconHarness.RestoreService);
		services.AddScoped<IProfilePortabilityService>(_ => throw new NotSupportedException());
		services.AddSingleton<IStorePlatformClient>(_platform);
		services.AddSingleton<IStoreTestInstallationStore>(_testInstallations);
		var provider = services.BuildServiceProvider();

		var trustEvaluator = new PluginTrustEvaluator(manifestReader,
			new NoRevocationDataSource(),
			new PluginTrustOptions { RootPublicKeyOverride = TestPki.Root.PublicKey },
			Serilog.Core.Logger.None);

		_pluginInstaller = new PluginInstaller(_paths,
			new PluginArtifactReader(manifestReader, Serilog.Core.Logger.None),
			new PluginArtifactAcquirer(_httpClientFactory, options, Serilog.Core.Logger.None),
			new PluginArtifactCache(_paths, options, Serilog.Core.Logger.None),
			trustEvaluator,
			new PluginDependencyResolver(_pluginCatalog, manifestReader),
			manifestReader,
			_pluginCatalog,
			new FakeInstallSupervisor(_pluginCatalog, sessionRegistry),
			new FakeIntegrationRegistrar(),
			sessionRegistry,
			new PluginTakeoverRegistry(),
			provider.GetRequiredService<IServiceScopeFactory>(),
			options,
			TimeProvider.System,
			Serilog.Core.Logger.None);

		var catalogQuery = new StoreCatalogQueryService(_catalog, _pluginCatalog, installations);
		_coordinator = new StoreInstallCoordinator(catalogQuery,
			_tracker,
			new StoreOperationChannel(),
			new StoreOperationCancellation(),
			_consent,
			new StoreInstallBackupBatches());

		_tests = new StoreTestService(_platform,
			_pluginCatalog,
			_testInstallations,
			_tracker,
			_coordinator,
			catalogQuery);

		_executor = new StoreInstallExecutor(_catalog,
			catalogQuery,
			_tracker,
			new StoreArtifactDownloader(_httpClientFactory, StoreRegistryOptions.Default, _paths, TimeProvider.System),
			_pluginInstaller,
			_iconHarness.Cache,
			installations,
			_consent,
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

	private Task<string> TrustedArtifact(string version = "1.0.0", string pluginId = PluginId)
		=> SignedPluginArtifacts.CreateSignedAsync(_sourceDirectory,
			pluginId,
			version,
			fileName: $"trusted-{version}-{Guid.NewGuid():N}.macroDeckPlugin");

	private string UnsignedArtifact(string version = "1.0.0", string pluginId = PluginId)
		=> new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build(version, pluginId, extraBlocks: null))
			.WithFile(ManifestJson.EntrypointExecutable, "binary")
			.WriteTo(_sourceDirectory, $"unsigned-{version}-{Guid.NewGuid():N}.macroDeckPlugin");

	private async Task<StorePlatformTestBuild> ShareTestBuild(string artifactPath, string version, string build, string? servedDigest = null)
	{
		var bytes = await File.ReadAllBytesAsync(artifactPath);
		var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
		var testBuild = new StorePlatformTestBuild(Guid.NewGuid(),
			version,
			build,
			null,
			Path.GetFileName(artifactPath),
			sha256,
			bytes.LongLength,
			DateTimeOffset.UnixEpoch,
			DateTimeOffset.UnixEpoch);
		_httpClientFactory.Body = bytes;
		_platform.Tests.Clear();
		_platform.Tests.Add(new StorePlatformTest(PluginId, "Consent Plugin", DateTimeOffset.UnixEpoch, [testBuild]));
		_platform.TestBuildDownload = StorePlatformResult.Ok(new StorePlatformTestBuildDownload(
			new Uri($"https://staging.example/{Path.GetFileName(artifactPath)}"),
			Path.GetFileName(artifactPath),
			servedDigest ?? sha256,
			bytes.LongLength,
			DateTimeOffset.UtcNow.AddMinutes(10)));
		return testBuild;
	}

	private async Task<StoreOperation> InstallTestBuild(StorePlatformTestBuild build, bool consent = true)
	{
		var started = await _tests.Install(PluginId, build.Id, consent);
		Assert.That(started.Success, Is.True, started.Failure.ToString());
		await _executor.Execute(started.Operation!.Id);
		return _tracker.Find(started.Operation.Id)!;
	}

	private string? ActiveVersion()
		=> _pluginCatalog.Discover().FirstOrDefault(plugin => plugin.PluginId == PluginId)?.ActiveVersion?.Version;

	[Test]
	public async Task An_unsigned_test_build_installs_with_consent_without_developer_mode_and_is_remembered()
	{
		_preferences.DeveloperMode = false;
		var build = await ShareTestBuild(UnsignedArtifact("1.2.0"), "1.2.0", "42");

		var result = await InstallTestBuild(build);
		var listed = await _tests.GetTests();

		Assert.Multiple(() =>
		{
			Assert.That(result.State, Is.EqualTo(StoreOperationState.Completed), result.ErrorMessage);
			Assert.That(result.Kind, Is.EqualTo(StoreOperationKind.TestInstall));
			Assert.That(result.CanRetry, Is.False);
			Assert.That(ActiveVersion(), Is.EqualTo("1.2.0"));
			Assert.That(listed.Tests.Single().InstalledTestBuildId, Is.EqualTo(build.Id));
		});
	}

	[Test]
	public async Task A_test_build_without_consent_installs_nothing_and_asks_the_platform_nothing()
	{
		var build = await ShareTestBuild(UnsignedArtifact("1.2.0"), "1.2.0", "42");

		var result = await InstallTestBuild(build, consent: false);

		Assert.Multiple(() =>
		{
			Assert.That(result.State, Is.EqualTo(StoreOperationState.Failed));
			Assert.That(result.Error, Is.EqualTo(StoreOperationError.TestConsentRequired));
			Assert.That(_platform.TestBuildDownloadRequests, Is.Empty);
			Assert.That(ActiveVersion(), Is.Null);
		});
	}

	[Test]
	public async Task A_test_build_replaces_a_trusted_store_install_of_the_same_plugin()
	{
		var trusted = await _pluginInstaller.Install(PluginArtifactSource.FromPath(await TrustedArtifact("1.1.0")),
			new PluginInstallRequest());
		Assert.That(trusted.Success, Is.True, trusted.ErrorMessage);
		var build = await ShareTestBuild(UnsignedArtifact("1.2.0"), "1.2.0", "42");

		var result = await InstallTestBuild(build);

		Assert.Multiple(() =>
		{
			Assert.That(result.State, Is.EqualTo(StoreOperationState.Completed), result.ErrorMessage);
			Assert.That(ActiveVersion(), Is.EqualTo("1.2.0"));
		});
	}

	[Test]
	public async Task A_newer_build_of_the_same_version_replaces_the_installed_one()
	{
		var first = await ShareTestBuild(UnsignedArtifact("1.2.0"), "1.2.0", "42");
		Assert.That((await InstallTestBuild(first)).State, Is.EqualTo(StoreOperationState.Completed));
		var second = await ShareTestBuild(UnsignedArtifact("1.2.0"), "1.2.0", "43");

		var result = await InstallTestBuild(second);

		Assert.Multiple(() =>
		{
			Assert.That(result.State, Is.EqualTo(StoreOperationState.Completed), result.ErrorMessage);
			Assert.That(_testInstallations.Find(PluginId)?.BuildId, Is.EqualTo(second.Id));
		});
	}

	[Test]
	public async Task A_build_of_another_plugin_is_refused_and_installs_nothing()
	{
		var build = await ShareTestBuild(UnsignedArtifact("1.2.0", "com.evil.other"), "1.2.0", "42");

		var result = await InstallTestBuild(build);

		Assert.Multiple(() =>
		{
			Assert.That(result.State, Is.EqualTo(StoreOperationState.Failed));
			Assert.That(result.Error, Is.EqualTo(StoreOperationError.TestBuildMismatch));
			Assert.That(_pluginCatalog.Discover(), Is.Empty);
			Assert.That(_testInstallations.Find(PluginId), Is.Null);
		});
	}

	[Test]
	public async Task Bytes_that_do_not_match_the_platforms_digest_are_refused()
	{
		var build = await ShareTestBuild(UnsignedArtifact("1.2.0"), "1.2.0", "42", servedDigest: new string('0', 64));

		var result = await InstallTestBuild(build);

		Assert.Multiple(() =>
		{
			Assert.That(result.State, Is.EqualTo(StoreOperationState.Failed));
			Assert.That(result.Error, Is.EqualTo(StoreOperationError.ChecksumMismatch));
			Assert.That(ActiveVersion(), Is.Null);
		});
	}

	[Test]
	public async Task A_build_the_platform_does_not_list_for_this_account_starts_no_operation()
	{
		await ShareTestBuild(UnsignedArtifact("1.2.0"), "1.2.0", "42");

		var result = await _tests.Install(PluginId, Guid.NewGuid(), consent: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Failure, Is.EqualTo(StorePlatformFailure.NotFound));
			Assert.That(_tracker.Snapshot(), Is.Empty);
		});
	}

	[Test]
	public async Task A_signature_that_does_not_verify_is_refused_even_as_a_test_build()
	{
		var artifact = await TrustedArtifact("1.2.0");
		PackageArchiveFixtures.ReplaceEntry(artifact, PluginArtifactFiles.CertificateFileName, "not a certificate");
		var build = await ShareTestBuild(artifact, "1.2.0", "42");

		var result = await InstallTestBuild(build);

		Assert.Multiple(() =>
		{
			Assert.That(result.State, Is.EqualTo(StoreOperationState.Failed));
			Assert.That(result.Error, Is.Not.EqualTo(StoreOperationError.TestConsentRequired));
			Assert.That(ActiveVersion(), Is.Null);
		});
	}
}
