using System.Text.Json.Nodes;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Signing.TestSupport;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Plugins.Trust;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Plugins.Installation;
using MacroDeckHost.Infrastructure.Plugins.Trust;
using MacroDeckHost.Tests.UnitTests.Plugins.Installation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Trust;

/// <summary>Acceptance tests for launch-time signature enforcement (issue #610): installs a real signed
/// plugin to disk with <see cref="PluginInstaller" />, then exercises a real <see cref="PluginSupervisor" />
/// against it with a real <see cref="PluginTrustEvaluator" /> anchored to <see cref="TestPki.Root" />.
/// Only the process launcher, health probe and session registry are faked - nothing about trust evaluation
/// or manifest/catalog reading is.</summary>
[TestFixture]
internal sealed class PluginSupervisorTrustAcceptanceTests
{
	private const string PluginId = "com.example.launch-trust-plugin";

	private TestPaths _paths = null!;
	private PluginInstallationCatalog _catalog = null!;
	private InMemoryPluginTrustRecordRepository _trustRecords = null!;
	private FakePluginTrustBaseline _baseline = null!;
	private FakePluginProcessLauncher _launcher = null!;
	private PluginSupervisor _supervisor = null!;
	private MutablePluginRevocationSource _revocation = null!;
	private string _sourceDirectory = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.PluginsDirectory);
		Directory.CreateDirectory(_paths.PluginStagingDirectory);
		Directory.CreateDirectory(_paths.PluginCacheDirectory);

		_sourceDirectory = Path.Combine(_paths.BaseDirectory, "artifacts");
		Directory.CreateDirectory(_sourceDirectory);

		_catalog = new PluginInstallationCatalog(_paths, Serilog.Core.Logger.None);
		_trustRecords = new InMemoryPluginTrustRecordRepository();
		_baseline = new FakePluginTrustBaseline();

		var manifestReader = new PluginManifestReader();
		_revocation = new MutablePluginRevocationSource();
		var trustEvaluator = new PluginTrustEvaluator(manifestReader,
			_revocation,
			new PluginTrustOptions { RootPublicKeyOverride = TestPki.Root.PublicKey },
			Serilog.Core.Logger.None);

		var services = new ServiceCollection();
		services.AddSingleton<IPluginTrustRecordRepository>(_trustRecords);
		services.AddSingleton<IPluginTrustBaseline>(_baseline);
		var provider = services.BuildServiceProvider();

		var installerOptions = PluginInstallerOptions.Default with
		{
			ActivationHealthTimeout = TimeSpan.FromSeconds(2)
		};
		var installerSessions = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		var installer = new PluginInstaller(_paths,
			new PluginArtifactReader(manifestReader, Serilog.Core.Logger.None),
			new PluginArtifactAcquirer(new NoHttpClientFactory(), installerOptions, Serilog.Core.Logger.None),
			new PluginArtifactCache(_paths, installerOptions, Serilog.Core.Logger.None),
			trustEvaluator,
			new PluginDependencyResolver(_catalog, manifestReader),
			manifestReader,
			_catalog,
			new FakeInstallSupervisor(_catalog, installerSessions),
			new FakeIntegrationRegistrar(),
			installerSessions,
			provider.GetRequiredService<IServiceScopeFactory>(),
			installerOptions,
			TimeProvider.System,
			Serilog.Core.Logger.None);

		_launcher = new FakePluginProcessLauncher();
		var sessionRegistry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		var listenerState = new HostListenerState(PublicEndpointSet.HttpOnly(50100), false);
		listenerState.SetLoopbackPort(50101);

		var supervisorOptions = new PluginSupervisorOptions
		{
			GracefulShutdownTimeout = TimeSpan.FromSeconds(10),
			StartupGrace = TimeSpan.FromSeconds(30),
			HealthPollInterval = TimeSpan.FromSeconds(15),
			HealthTimeout = TimeSpan.FromSeconds(2),
			UnhealthyThreshold = 3,
			StableRuntime = TimeSpan.FromMinutes(2),
			MaxRestarts = 3,
			RestartWindow = TimeSpan.FromMinutes(10),
			BootstrapOutputLines = 100,
			BootstrapOutputBytes = 16 * 1024
		};

		_supervisor = new PluginSupervisor(manifestReader,
			_catalog,
			new FakePluginRuntimeStateStore(),
			new FakePluginProcessJournal(),
			_launcher,
			new FakeDotnetMuxerLocator(),
			new FakePluginHealthProbe(),
			sessionRegistry,
			new PluginLaunchTokenService(TimeProvider.System, sessionRegistry),
			listenerState,
			trustEvaluator,
			provider.GetRequiredService<IServiceScopeFactory>(),
			TimeProvider.System,
			supervisorOptions,
			Serilog.Core.Logger.None);

		Installer = installer;
	}

	private PluginInstaller Installer { get; set; } = null!;

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	private async Task InstallTrusted(string version = "1.0.0")
	{
		var artifact = await SignedPluginArtifacts.CreateSignedAsync(_sourceDirectory,
			PluginId,
			version,
			fileName: $"trusted-{version}-{Guid.NewGuid():N}.macroDeckPlugin");

		var result = await Installer.Install(PluginArtifactSource.FromPath(artifact), new PluginInstallRequest());
		Assert.That(result.Success, Is.True, result.ErrorMessage);
	}

	private void TamperInstalledEntrypoint()
	{
		var installed = _catalog.Discover().First(p => p.PluginId == PluginId);
		var entrypointPath = Path.Combine(installed.ActiveVersion!.VersionDirectory, ManifestJson.EntrypointExecutable);
		File.WriteAllText(entrypointPath, "tampered bytes that do not match the signed digest");
		_catalog.Invalidate();
	}

	private void StripSignature()
	{
		var installed = _catalog.Discover().First(p => p.PluginId == PluginId);
		var manifestPath = Path.Combine(installed.ActiveVersion!.VersionDirectory, "manifest.json");
		var certPath = Path.Combine(installed.ActiveVersion!.VersionDirectory, PluginArtifactFiles.CertificateFileName);
		var sigPath = Path.Combine(installed.ActiveVersion!.VersionDirectory,
			PluginArtifactFiles.CertificateSignatureFileName);

		var node = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
		node.Remove("signature");
		File.WriteAllText(manifestPath, node.ToJsonString());
		File.Delete(certPath);
		File.Delete(sigPath);
		_catalog.Invalidate();
	}

	[Test]
	public async Task S33_an_untouched_trusted_plugin_still_launches()
	{
		await InstallTrusted();

		var result = await _supervisor.Start(PluginId);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.Message);
			Assert.That(_launcher.Requests, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task S34_a_byte_flipped_after_a_successful_launch_refuses_the_next_start_and_spawns_no_process()
	{
		await InstallTrusted();

		var firstStart = await _supervisor.Start(PluginId);
		Assert.That(firstStart.Success, Is.True, firstStart.Message);
		Assert.That(_launcher.Requests, Has.Count.EqualTo(1));

		await _supervisor.Stop(PluginId, PluginStopReason.UserRequested);
		TamperInstalledEntrypoint();

		var secondStart = await _supervisor.Start(PluginId);

		Assert.Multiple(() =>
		{
			Assert.That(secondStart.Success, Is.False);
			Assert.That(secondStart.Error, Is.EqualTo(PluginSupervisorError.IntegrityFailed));
			Assert.That(secondStart.Message, Does.Contain("ContentMismatch"));
			Assert.That(_launcher.Requests, Has.Count.EqualTo(1), "no new process should have been spawned");
		});
	}

	[Test]
	public async Task
		S35_a_stripped_signature_is_refused_not_downgraded_to_unsigned_and_the_recorded_tier_is_untouched()
	{
		await InstallTrusted();
		StripSignature();

		var result = await _supervisor.Start(PluginId);
		var record = await _trustRecords.GetVersion(PluginId, "1.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginSupervisorError.IntegrityFailed));
			Assert.That(_launcher.Requests, Is.Empty);
			Assert.That(record?.AdmittedVerdict, Is.EqualTo(PluginTrustRecordVerdicts.Trusted));
		});
	}

	[Test]
	public async Task S37_a_grandfathered_plugin_with_no_record_before_the_baseline_launches_and_reads_back_not_signed()
	{
		await InstallTrusted();
		await _trustRecords.DeleteForPlugin(PluginId);
		_baseline.ExistsValue = false;

		var result = await _supervisor.Start(PluginId);
		var record = await _trustRecords.GetVersion(PluginId, "1.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.Message);

			// The artifact itself still verifies as Trusted - "grandfathered" here means "backfilled from
			// no record", not "downgraded". S37 asks for the wire verdict a plugin with NO record at all
			// would carry; that is exercised directly against the wire mapping in PluginTrustWireMappingTests.
			// The backfill itself, however, is pinned here: PluginTrustGate's "missing record, before
			// baseline" case backfills at the verdict the artifact verifies as right now (Trusted), not at
			// PluginTrustRecordVerdicts.Unsigned - that Unsigned grandfathering is
			// PluginTrustBaselineBackgroundService's one-time job, a different code path entirely.
			Assert.That(record?.AdmittedVerdict, Is.EqualTo(PluginTrustRecordVerdicts.Trusted));
		});
	}

	[Test]
	public async Task S37b_a_missing_record_after_the_baseline_is_refused()
	{
		await InstallTrusted();
		await _trustRecords.DeleteForPlugin(PluginId);
		_baseline.ExistsValue = true;

		var result = await _supervisor.Start(PluginId);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginSupervisorError.IntegrityFailed));
			Assert.That(_launcher.Requests, Is.Empty);
		});
	}

	[Test]
	public async Task S39_activation_and_launch_both_refuse_once_revocation_reports_revoked()
	{
		await InstallTrusted();
		var firstStart = await _supervisor.Start(PluginId);
		Assert.That(firstStart.Success, Is.True, firstStart.Message);

		await _supervisor.Stop(PluginId, PluginStopReason.UserRequested);

		var activation = await Installer.Activate(PluginId, "1.0.0");
		Assert.That(activation.Success, Is.True, activation.ErrorMessage);

		_revocation.Status = PluginRevocationStatus.Revoked;

		var revokedActivation = await Installer.Activate(PluginId, "1.0.0");
		var restart = await _supervisor.Start(PluginId);

		Assert.Multiple(() =>
		{
			Assert.That(revokedActivation.Success, Is.False);
			Assert.That(revokedActivation.Error, Is.EqualTo(PluginInstallError.SignatureRevoked));
			Assert.That(restart.Success, Is.False);
			Assert.That(restart.Error, Is.EqualTo(PluginSupervisorError.IntegrityFailed));
			Assert.That(_launcher.Requests, Has.Count.EqualTo(1), "no new process should have been spawned");
		});
	}

	[Test]
	public async Task S40_the_shipped_no_revocation_data_source_reports_trusted_not_revocation_unavailable()
	{
		var trustRecords = new InMemoryPluginTrustRecordRepository();
		var manifestReader = new PluginManifestReader();
		var trustEvaluator = new PluginTrustEvaluator(manifestReader,
			new NoRevocationDataSource(),
			new PluginTrustOptions { RootPublicKeyOverride = TestPki.Root.PublicKey },
			Serilog.Core.Logger.None);

		var services = new ServiceCollection();
		services.AddSingleton<IPluginTrustRecordRepository>(trustRecords);
		services.AddSingleton<IPluginTrustBaseline>(new FakePluginTrustBaseline());
		var provider = services.BuildServiceProvider();

		var options = PluginInstallerOptions.Default with { ActivationHealthTimeout = TimeSpan.FromSeconds(2) };
		var installerSessions = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		var installer = new PluginInstaller(_paths,
			new PluginArtifactReader(manifestReader, Serilog.Core.Logger.None),
			new PluginArtifactAcquirer(new NoHttpClientFactory(), options, Serilog.Core.Logger.None),
			new PluginArtifactCache(_paths, options, Serilog.Core.Logger.None),
			trustEvaluator,
			new PluginDependencyResolver(_catalog, manifestReader),
			manifestReader,
			_catalog,
			new FakeInstallSupervisor(_catalog, installerSessions),
			new FakeIntegrationRegistrar(),
			installerSessions,
			provider.GetRequiredService<IServiceScopeFactory>(),
			options,
			TimeProvider.System,
			Serilog.Core.Logger.None);

		var artifact = await SignedPluginArtifacts.CreateSignedAsync(_sourceDirectory,
			"com.example.no-revocation-plugin",
			"1.0.0",
			fileName: $"no-revocation-{Guid.NewGuid():N}.macroDeckPlugin");

		var install = await installer.Install(PluginArtifactSource.FromPath(artifact), new PluginInstallRequest());
		var record = await trustRecords.GetVersion("com.example.no-revocation-plugin", "1.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(install.Success, Is.True, install.ErrorMessage);
			Assert.That(install.Signature?.Verdict, Is.EqualTo(PluginTrustVerdict.Trusted));
			Assert.That(record?.AdmittedVerdict, Is.EqualTo(PluginTrustRecordVerdicts.Trusted));
		});
	}

	// Regression for issue #610: PluginInstaller.InstallLocked used to write the trust record AFTER
	// ActivateAndValidate started the plugin. Once the baseline is established, PluginSupervisor's real
	// gate refuses to launch an installed version that has no trust record yet - so with the record written
	// too late, every install that starts its plugin (signed or unsigned) would fail health validation and
	// roll itself back. Wiring the installer to this fixture's REAL PluginSupervisor - not
	// FakeInstallSupervisor, which never consults the gate - is what makes this test able to see that at all.
	[Test]
	public async Task S52_install_succeeds_with_an_established_baseline_and_the_real_supervisor_gate()
	{
		_baseline.ExistsValue = true;

		var manifestReader = new PluginManifestReader();
		var options = PluginInstallerOptions.Default with { ActivationHealthTimeout = TimeSpan.FromSeconds(5) };

		var services = new ServiceCollection();
		services.AddSingleton<IPluginTrustRecordRepository>(_trustRecords);
		services.AddSingleton<IPluginTrustBaseline>(_baseline);
		var provider = services.BuildServiceProvider();

		var trustEvaluator = new PluginTrustEvaluator(manifestReader,
			_revocation,
			new PluginTrustOptions { RootPublicKeyOverride = TestPki.Root.PublicKey },
			Serilog.Core.Logger.None);

		var installer = new PluginInstaller(_paths,
			new PluginArtifactReader(manifestReader, Serilog.Core.Logger.None),
			new PluginArtifactAcquirer(new NoHttpClientFactory(), options, Serilog.Core.Logger.None),
			new PluginArtifactCache(_paths, options, Serilog.Core.Logger.None),
			trustEvaluator,
			new PluginDependencyResolver(_catalog, manifestReader),
			manifestReader,
			_catalog,
			_supervisor,
			new FakeIntegrationRegistrar(),
			new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None),
			provider.GetRequiredService<IServiceScopeFactory>(),
			options,
			TimeProvider.System,
			Serilog.Core.Logger.None);

		var artifact = await SignedPluginArtifacts.CreateSignedAsync(_sourceDirectory,
			PluginId,
			"1.0.0",
			fileName: $"baseline-established-{Guid.NewGuid():N}.macroDeckPlugin");

		// Nothing in this fixture runs PluginSupervisor.Reconcile() on a timer the way the host's
		// reconcile-loop background service does, and the real supervisor only leaves Starting for Running
		// once a health probe succeeds through Reconcile. Installer.Install's own health-gating loop polls
		// Snapshot() but never drives Reconcile itself, so it is pumped here - cooperatively, on this same
		// async flow - to let the launch this test is actually checking, the real gate permitting a
		// freshly-recorded install, reach Running at all.
		//
		// Reconcile stops being called the moment Running is first observed: EvaluateStarting sets Health
		// to Healthy directly on that one transition, but EvaluateRunning recomputes it from scratch on
		// every later tick and - with no live plugin session in this fixture - settles back to Unknown.
		// Calling Reconcile again after Running would race that recomputation against Install's own
		// 250ms Snapshot() poll for no reason; leaving the entry alone lets that poll see the stable
		// Healthy state Install itself is checking for.
		var installTask = installer.Install(PluginArtifactSource.FromPath(artifact), new PluginInstallRequest());

		while (!installTask.IsCompleted)
		{
			var snapshot = _supervisor.Snapshot().FirstOrDefault(s => s.PluginId == PluginId);
			if (snapshot?.State == PluginRuntimeState.Running)
			{
				break;
			}

			await _supervisor.Reconcile();
			await Task.Delay(TimeSpan.FromMilliseconds(20));
		}

		var result = await installTask;

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(_launcher.Requests, Has.Count.EqualTo(1));
			Assert.That(_supervisor.Snapshot().First(s => s.PluginId == PluginId).State,
				Is.EqualTo(PluginRuntimeState.Running));
		});
	}

	private sealed class NoHttpClientFactory : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) => throw new NotSupportedException();
	}

	private sealed class MutablePluginRevocationSource : IPluginRevocationSource
	{
		public PluginRevocationStatus Status { get; set; } = PluginRevocationStatus.NotRevoked;

		public Task<PluginRevocationResult> CheckAsync(string certificateId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(new PluginRevocationResult(Status, null));
	}
}
