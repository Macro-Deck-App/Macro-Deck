using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Plugins.Trust;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.Plugins.Installation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Runtime;

[TestFixture]
internal sealed class PluginSupervisorTakeoverTests
{
	private const string PluginId = "com.example.plugin";
	private const string Version = "1.0.0";

	private ManualTimeProvider _time = null!;
	private FakePluginManifestReader _manifestReader = null!;
	private FakePluginInstallationCatalog _catalog = null!;
	private FakePluginRuntimeStateStore _stateStore = null!;
	private FakePluginProcessLauncher _launcher = null!;
	private FakePluginHealthProbe _healthProbe = null!;
	private PluginSessionRegistry _sessionRegistry = null!;
	private PluginLaunchTokenService _launchTokenService = null!;
	private PluginTakeoverRegistry _takeovers = null!;
	private PluginSupervisorOptions _options = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_manifestReader = new FakePluginManifestReader { Default = ManifestWithShutdown(gracefulTimeoutSeconds: 10) };
		_catalog = new FakePluginInstallationCatalog();
		_catalog.Plugins.Add(Installed());
		_stateStore = new FakePluginRuntimeStateStore();
		_launcher = new FakePluginProcessLauncher();
		_healthProbe = new FakePluginHealthProbe();
		_sessionRegistry = new PluginSessionRegistry(_time, Serilog.Core.Logger.None);
		_launchTokenService = new PluginLaunchTokenService(_time, _sessionRegistry);
		_takeovers = new PluginTakeoverRegistry();
		_options = new PluginSupervisorOptions
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
	}

	[Test]
	public async Task A_suspend_returns_while_a_slow_plugin_is_still_in_its_graceful_wait()
	{
		_manifestReader.Default = ManifestWithShutdown(gracefulTimeoutSeconds: 60);
		var supervisor = CreateSupervisor();
		var process = await StartRunning(supervisor);
		var managedConnection = await AttachSession(PluginSessionOrigin.Managed);

		_takeovers.Begin(PluginId);
		var suspend = supervisor.SuspendForTakeover(PluginId);
		var returnedWithoutWaiting = await Task.WhenAny(suspend, Task.Delay(TimeSpan.FromSeconds(5))) == suspend;
		var duringWait = Snapshot(supervisor);

		Assert.Multiple(() =>
		{
			Assert.That(returnedWithoutWaiting,
				Is.True,
				"a pairing redemption must not wait out the plugin's 60 s shutdown budget");
			Assert.That(process.HasExited, Is.False);
			Assert.That(duringWait.State, Is.EqualTo(PluginRuntimeState.Stopping));
			Assert.That(_launchTokenService.HasActiveLaunch(PluginId), Is.False);
			Assert.That(managedConnection.Sent.Select(envelope => envelope.Type),
				Does.Contain(MessageTypes.SessionGoodbye));
			Assert.That(managedConnection.Closes.Select(close => close.CloseCode),
				Does.Contain(ProtocolCloseCodes.SupervisorShutdown));
		});

		_time.Advance(TimeSpan.FromSeconds(61));
		await WaitUntil(() => Snapshot(supervisor).State == PluginRuntimeState.Stopped);
		var afterWait = Snapshot(supervisor);

		Assert.Multiple(() =>
		{
			Assert.That(process.KillTreeCallCount, Is.EqualTo(1));
			Assert.That(afterWait.LastStopReason, Is.EqualTo(PluginStopReason.DevelopmentTakeover));
			Assert.That(afterWait.Health, Is.EqualTo(PluginHealthState.Unknown));
			Assert.That(afterWait.RestartCount, Is.EqualTo(0));
		});
	}

	[Test]
	public async Task While_taken_over_the_plugin_is_never_relaunched_and_cannot_be_started_by_hand()
	{
		var supervisor = CreateSupervisor();
		var process = await StartRunning(supervisor);

		await TakeOver(supervisor, process);
		await supervisor.Reconcile();
		_time.Advance(TimeSpan.FromMinutes(5));
		await supervisor.Reconcile();
		var start = await supervisor.Start(PluginId);
		var restart = await supervisor.Restart(PluginId);
		var snapshot = Snapshot(supervisor);

		Assert.Multiple(() =>
		{
			Assert.That(_launcher.Requests, Has.Count.EqualTo(1));
			Assert.That(start.Error, Is.EqualTo(PluginSupervisorError.TakenOverByDevelopmentBuild));
			Assert.That(restart.Error, Is.EqualTo(PluginSupervisorError.TakenOverByDevelopmentBuild));
			Assert.That(snapshot.State, Is.EqualTo(PluginRuntimeState.Stopped));
			Assert.That(snapshot.TakenOverByDevelopmentBuild, Is.True);
			Assert.That(_stateStore.States[PluginId], Is.True, "a takeover never writes the desired state");
		});
	}

	[Test]
	public async Task Every_ended_takeover_resumes_the_installed_plugin_without_counting_a_crash()
	{
		var supervisor = CreateSupervisor();
		var process = await StartRunning(supervisor);

		for (var cycle = 1; cycle <= 3; cycle++)
		{
			await TakeOver(supervisor, process);
			_takeovers.Finish(PluginId);

			_time.Advance(_options.HealthPollInterval + TimeSpan.FromSeconds(1));
			await supervisor.Reconcile();
			await supervisor.Reconcile();

			var snapshot = Snapshot(supervisor);
			Assert.Multiple(() =>
			{
				Assert.That(snapshot.State, Is.EqualTo(PluginRuntimeState.Running), $"cycle {cycle}");
				Assert.That(snapshot.RestartCount, Is.EqualTo(0), $"cycle {cycle}");
				Assert.That(snapshot.TakenOverByDevelopmentBuild, Is.False, $"cycle {cycle}");
				Assert.That(_launcher.Requests, Has.Count.EqualTo(cycle + 1), $"cycle {cycle}");
			});

			process = _launcher.StartedProcesses[^1];
		}

		Assert.That(_stateStore.States[PluginId], Is.True);
	}

	[Test]
	public async Task A_suspend_leaves_the_development_session_connected()
	{
		var supervisor = CreateSupervisor();
		var developmentConnection = await AttachSession(PluginSessionOrigin.SelfRegistered);

		_takeovers.Begin(PluginId);
		await supervisor.SuspendForTakeover(PluginId);
		await supervisor.SuspendForTakeover(PluginId);

		Assert.Multiple(() =>
		{
			Assert.That(developmentConnection.Closes, Is.Empty);
			Assert.That(developmentConnection.Sent, Is.Empty, "no goodbye reaches the development build");
			Assert.That(_sessionRegistry.Snapshot().Single().Origin, Is.EqualTo(PluginSessionOrigin.SelfRegistered));
		});
	}

	[Test]
	public async Task A_launch_interrupted_by_a_takeover_ends_stopped_and_resumes_once_the_takeover_ends()
	{
		var trust = new BlockingTrustEvaluator();
		var supervisor = CreateSupervisor(trust);
		_stateStore.States[PluginId] = true;

		var start = supervisor.Start(PluginId);
		_takeovers.Begin(PluginId);
		await supervisor.SuspendForTakeover(PluginId);
		trust.Release.SetResult();
		var result = await start;
		var interrupted = Snapshot(supervisor);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(PluginSupervisorError.TakenOverByDevelopmentBuild));
			Assert.That(_launcher.Requests, Is.Empty);
			Assert.That(interrupted.State, Is.EqualTo(PluginRuntimeState.Stopped));
			Assert.That(interrupted.LastStopReason, Is.EqualTo(PluginStopReason.DevelopmentTakeover));
			Assert.That(_launchTokenService.HasActiveLaunch(PluginId), Is.False);
		});

		_takeovers.Finish(PluginId);
		await supervisor.Reconcile();

		Assert.That(_launcher.Requests, Has.Count.EqualTo(1), "the installed plugin resumes after the takeover");
	}

	[Test]
	public async Task A_process_that_started_as_a_takeover_began_is_stopped_and_leaves_no_launch_token()
	{
		var supervisor = CreateSupervisor();
		FakePluginProcess? started = null;
		_launcher.OnStart = _ =>
		{
			_takeovers.Begin(PluginId);
			started = new FakePluginProcess();
			return started;
		};

		var result = await supervisor.Start(PluginId);
		var secret = _launcher.Requests.Single().Environment["MACRO_DECK_PLUGIN_SECRET"]!;
		_time.Advance(_options.GracefulShutdownTimeout + TimeSpan.FromSeconds(1));
		await WaitUntil(() => Snapshot(supervisor).State == PluginRuntimeState.Stopped);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(PluginSupervisorError.TakenOverByDevelopmentBuild));
			Assert.That(started!.HasExited, Is.True);
			Assert.That(_launchTokenService.TryAcquire(PluginId, secret, out _), Is.False);
			Assert.That(_launchTokenService.HasActiveLaunch(PluginId), Is.False);
			Assert.That(Snapshot(supervisor).LastStopReason, Is.EqualTo(PluginStopReason.DevelopmentTakeover));
		});
	}

	private async Task<FakePluginProcess> StartRunning(PluginSupervisor supervisor)
	{
		var result = await supervisor.Start(PluginId);
		Assert.That(result.Success, Is.True, result.Message);
		await supervisor.Reconcile();
		Assert.That(Snapshot(supervisor).State, Is.EqualTo(PluginRuntimeState.Running), "precondition: running");
		return _launcher.StartedProcesses[^1];
	}

	private async Task TakeOver(PluginSupervisor supervisor, FakePluginProcess process)
	{
		_takeovers.Begin(PluginId);
		await supervisor.SuspendForTakeover(PluginId);
		process.CompleteExit(0);
		await WaitUntil(() => Snapshot(supervisor).State == PluginRuntimeState.Stopped);
	}

	private async Task<FakePluginConnection> AttachSession(PluginSessionOrigin origin)
	{
		var sessionId = $"session-{Guid.NewGuid():N}";
		await _sessionRegistry.Create(new PluginSessionRecord
		{
			SessionId = sessionId,
			PluginId = PluginId,
			DisplayName = "Example",
			Origin = origin,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, CapabilityNegotiationResult>(),
			DeclaredCapabilities = [],
			CreatedAt = _time.GetUtcNow()
		});

		var connection = new FakePluginConnection();
		_sessionRegistry.TryAttach(sessionId, connection, instanceId: null);
		return connection;
	}

	private static PluginRuntimeSnapshot Snapshot(PluginSupervisor supervisor)
		=> supervisor.Snapshot().Single(snapshot => snapshot.PluginId == PluginId);

	private static async Task WaitUntil(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (!condition())
		{
			if (DateTime.UtcNow >= deadline)
			{
				Assert.Fail("The expected supervisor state was never reached.");
			}

			await Task.Delay(10);
		}
	}

	private PluginSupervisor CreateSupervisor(IPluginTrustEvaluator? trustEvaluator = null)
	{
		var services = new ServiceCollection()
			.AddSingleton<Mediator.IMediator>(new RecordingMediator())
			.AddSingleton<IPluginTrustRecordRepository, InMemoryPluginTrustRecordRepository>()
			.AddSingleton<IPluginTrustBaseline, FakePluginTrustBaseline>()
			.BuildServiceProvider();

		var listenerState = new HostListenerState(PublicEndpointSet.HttpOnly(50000), false);
		listenerState.SetLoopbackPort(50001);

		return new PluginSupervisor(_manifestReader,
			_catalog,
			_stateStore,
			new FakePluginProcessJournal(),
			_launcher,
			new FakeDotnetMuxerLocator(),
			_healthProbe,
			_sessionRegistry,
			_launchTokenService,
			listenerState,
			trustEvaluator ?? new FakePluginTrustEvaluator(),
			_takeovers,
			services.GetRequiredService<IServiceScopeFactory>(),
			_time,
			_options,
			Serilog.Core.Logger.None);
	}

	private static PluginManifestReadResult ManifestWithShutdown(int gracefulTimeoutSeconds)
		=> PluginManifestReadResult.Ok(new PluginManifest
		{
			ManifestVersion = 1,
			Id = PluginId,
			Name = "Example",
			Version = Version,
			Entrypoints = new Dictionary<string, PluginEntrypoint>
			{
				[PluginRuntimeIdentifiers.Current] = new() { Executable = "app" }
			},
			Shutdown = new PluginShutdownSettings { GracefulTimeoutSeconds = gracefulTimeoutSeconds }
		});

	private static InstalledPlugin Installed()
	{
		var directory = Path.Combine(Path.GetTempPath(), "md-plugins-fake", PluginId);
		var version = new InstalledPluginVersion
		{
			Version = Version,
			VersionDirectory = Path.Combine(directory, "versions", Version),
			ManifestPath = Path.Combine(directory, "versions", Version, "manifest.json")
		};

		return new InstalledPlugin
		{
			PluginId = PluginId,
			PluginDirectory = directory,
			Versions = [version],
			ActiveVersion = version
		};
	}

	private sealed class BlockingTrustEvaluator : IPluginTrustEvaluator
	{
		public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public async Task<PluginTrustResult> EvaluateInstalledAsync(string versionDirectory,
			CancellationToken cancellationToken = default)
		{
			await Release.Task.WaitAsync(cancellationToken);
			return PluginTrustResult.Of(PluginTrustVerdict.Trusted, "cert-fake");
		}
	}
}
