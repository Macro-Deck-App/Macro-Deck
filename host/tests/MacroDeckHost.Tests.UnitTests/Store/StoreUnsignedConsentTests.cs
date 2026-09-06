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

namespace MacroDeckHost.Tests.UnitTests.Store;

/// <summary>Acceptance tests for the store's unsigned-consent exception (issue #517, ADR 0044): a store install of an unsigned plugin is permitted, but only after the user asked for it
/// and only while the host itself reports developer mode on. Run against real signed, unsigned and
/// tampered <c>.macroDeckPlugin</c> archives served over the artifact URL, with a real
/// <see cref="PluginTrustEvaluator" /> anchored to <see cref="TestPki.Root" /> - a fake verdict here would
/// prove nothing about the boundary being tested.</summary>
[TestFixture]
internal sealed class StoreUnsignedConsentTests
{
	private const string PluginId = "com.acme.store-consent";

	private TestPaths _paths = null!;
	private FakeUrlHttpClientFactory _httpClientFactory = null!;
	private StoreOperationTracker _tracker = null!;
	private StoreCatalog _catalog = null!;
	private StoreInstallConsent _consent = null!;
	private StoreInstallCoordinator _coordinator = null!;
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
			provider.GetRequiredService<IServiceScopeFactory>(),
			options,
			TimeProvider.System,
			Serilog.Core.Logger.None);

		var catalogQuery = new StoreCatalogQueryService(_catalog, _pluginCatalog, installations);
		_coordinator = new StoreInstallCoordinator(catalogQuery,
			_tracker,
			new StoreOperationChannel(),
			new StoreOperationCancellation(),
			_consent);

		_executor = new StoreInstallExecutor(_catalog,
			catalogQuery,
			_tracker,
			new StoreArtifactDownloader(_httpClientFactory, StoreRegistryOptions.Default, _paths, TimeProvider.System),
			_pluginInstaller,
			_iconHarness.Cache,
			installations,
			_consent,
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

	private Task<string> TrustedArtifact(string version = "1.0.0")
		=> SignedPluginArtifacts.CreateSignedAsync(_sourceDirectory,
			PluginId,
			version,
			fileName: $"trusted-{version}-{Guid.NewGuid():N}.macroDeckPlugin");

	private string UnsignedArtifact(string version = "1.0.0")
		=> new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build(version, PluginId, extraBlocks: null))
			.WithFile(ManifestJson.EntrypointExecutable, "binary")
			.WriteTo(_sourceDirectory, $"unsigned-{version}-{Guid.NewGuid():N}.macroDeckPlugin");

	/// <summary>A signed package whose certificate cannot be read at all: verdict Malformed.</summary>
	private async Task<string> MalformedArtifact(string version = "1.0.0")
	{
		var artifact = await TrustedArtifact(version);
		PackageArchiveFixtures.ReplaceEntry(artifact, PluginArtifactFiles.CertificateFileName, "not a certificate");
		return artifact;
	}

	/// <summary>A signed package whose payload no longer matches what the signature covers.</summary>
	private async Task<string> TamperedPayloadArtifact(string version = "1.0.0")
	{
		var artifact = await TrustedArtifact(version);
		PackageArchiveFixtures.WriteEntryText(artifact, ManifestJson.EntrypointExecutable, "tampered");
		return artifact;
	}

	/// <summary>Publishes <paramref name="artifactPath" /> as the catalog's only entry and serves its exact
	/// bytes from the artifact URL under its true digest, so nothing but the signature is ever in doubt.
	/// </summary>
	private async Task PublishAndServe(string artifactPath, string version = "1.0.0")
	{
		var bytes = await File.ReadAllBytesAsync(artifactPath);
		_httpClientFactory.Body = bytes;
		_catalog.Swap(new StoreCatalogSnapshot
		{
			Sequence = 1,
			Entries =
			[
				new StoreCatalogEntry
				{
					Kind = StoreExtensionKind.Plugin,
					Id = PluginId,
					Name = "Consent Plugin",
					LatestVersion = version,
					LatestRelease = new StoreReleaseManifest
					{
						Version = version,
						ArtifactUrl = new Uri($"https://cdn.example/{Path.GetFileName(artifactPath)}"),
						Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
						Size = bytes.LongLength
					}
				}
			]
		});
	}

	private async Task<StoreOperation> InstallFromStore(bool allowUnsigned)
	{
		var operation = _coordinator.Install(StoreExtensionKind.Plugin, PluginId, version: null, allowUnsigned);
		await _executor.Execute(operation.Id);
		return _tracker.Find(operation.Id)!;
	}

	private bool PluginTreeExists()
		=> Directory.Exists(PluginInstallPaths.PluginDirectory(_paths.PluginsDirectory, PluginId));

	[Test]
	public async Task An_unsigned_store_plugin_installs_with_consent_in_developer_mode_and_is_recorded_as_unsigned()
	{
		_preferences.DeveloperMode = true;
		await PublishAndServe(UnsignedArtifact());

		var result = await InstallFromStore(allowUnsigned: true);

		var record = await _trustRecords.GetVersion(PluginId, "1.0.0");
		Assert.Multiple(() =>
		{
			Assert.That(result.State, Is.EqualTo(StoreOperationState.Completed), result.ErrorMessage);
			Assert.That(record?.AdmittedVerdict, Is.EqualTo(PluginTrustRecordVerdicts.Unsigned));
		});
	}

	[Test]
	public async Task An_unsigned_store_plugin_without_consent_is_refused_even_in_developer_mode()
	{
		_preferences.DeveloperMode = true;
		await PublishAndServe(UnsignedArtifact());

		var result = await InstallFromStore(allowUnsigned: false);

		Assert.Multiple(() =>
		{
			Assert.That(result.State, Is.EqualTo(StoreOperationState.Failed));
			Assert.That(result.Error, Is.EqualTo(StoreOperationError.UnsignedNotPermitted));
			Assert.That(PluginTreeExists(), Is.False);
		});
	}

	[Test]
	public async Task Consent_without_developer_mode_is_refused_and_installs_nothing()
	{
		_preferences.DeveloperMode = false;
		await PublishAndServe(UnsignedArtifact());

		var result = await InstallFromStore(allowUnsigned: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.State, Is.EqualTo(StoreOperationState.Failed));
			Assert.That(result.Error, Is.EqualTo(StoreOperationError.UnsignedNotPermitted));
			Assert.That(PluginTreeExists(), Is.False);
			Assert.That(_pluginCatalog.Discover(), Is.Empty);
		});
	}

	/// <summary>The point of the whole exception: consent covers "carries no signature", never "carries a
	/// signature that does not verify". Neither fixture here may be admitted, and neither may collapse
	/// into the consentable unsigned bucket. A tampered payload is reported by the installer's own
	/// declared-digest check before the signature verdict is reached, so it lands on ChecksumMismatch
	/// rather than on a trust verdict - the assertion is that it is refused and is not treated as
	/// unsigned, not which of the two refusals names it.</summary>
	[Test]
	public async Task A_signature_that_does_not_verify_is_refused_even_with_consent_in_developer_mode()
	{
		var failures = new List<string>();

		foreach (var (name, artifact) in new[]
			{
				("Malformed", await MalformedArtifact()),
				("TamperedPayload", await TamperedPayloadArtifact())
			})
		{
			_preferences.DeveloperMode = true;
			await PublishAndServe(artifact);

			var result = await InstallFromStore(allowUnsigned: true);

			if (result.State != StoreOperationState.Failed)
			{
				failures.Add($"{name} was admitted: {result.State}.");
			}

			if (result.Error == StoreOperationError.UnsignedNotPermitted)
			{
				failures.Add($"{name} collapsed into the consentable unsigned bucket.");
			}

			if (PluginTreeExists())
			{
				failures.Add($"{name} left a plugin tree behind.");
			}
		}

		Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
	}

	[Test]
	public async Task Consent_does_not_lift_the_monotonic_rule_for_a_plugin_admitted_as_trusted()
	{
		var trusted = await _pluginInstaller.Install(PluginArtifactSource.FromPath(await TrustedArtifact()),
			new PluginInstallRequest());
		Assert.That(trusted.Success, Is.True, trusted.ErrorMessage);

		_preferences.DeveloperMode = true;
		await PublishAndServe(UnsignedArtifact("1.1.0"), "1.1.0");

		var result = await InstallFromStore(allowUnsigned: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.State, Is.EqualTo(StoreOperationState.Failed));
			Assert.That(result.Error, Is.EqualTo(StoreOperationError.TrustDowngrade));
			Assert.That(_pluginCatalog.Discover().First(plugin => plugin.PluginId == PluginId).ActiveVersion?.Version,
				Is.EqualTo("1.0.0"));
		});
	}
}
