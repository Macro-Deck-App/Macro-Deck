using System.Globalization;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Events;
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
internal sealed class PluginSupervisorTests
{
	private const string PluginId = "com.example.plugin";
	private const string Version = "1.0.0";

	private ManualTimeProvider _time = null!;
	private FakePluginManifestReader _manifestReader = null!;
	private FakePluginInstallationCatalog _catalog = null!;
	private FakePluginRuntimeStateStore _stateStore = null!;
	private FakePluginProcessJournal _journal = null!;
	private FakePluginProcessLauncher _launcher = null!;
	private FakeDotnetMuxerLocator _muxerLocator = null!;
	private FakePluginHealthProbe _healthProbe = null!;
	private PluginSessionRegistry _sessionRegistry = null!;
	private PluginLaunchTokenService _launchTokenService = null!;
	private HostListenerState _listenerState = null!;
	private RecordingMediator _mediator = null!;
	private PluginSupervisorOptions _options = null!;
	private FakePluginTrustEvaluator _trustEvaluator = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_manifestReader = new FakePluginManifestReader { Default = OkManifestResult() };
		_catalog = new FakePluginInstallationCatalog();
		_catalog.Plugins.Add(InstalledFor(PluginId, Version));
		_stateStore = new FakePluginRuntimeStateStore();
		_journal = new FakePluginProcessJournal();
		_launcher = new FakePluginProcessLauncher();
		_muxerLocator = new FakeDotnetMuxerLocator();
		_healthProbe = new FakePluginHealthProbe();
		_sessionRegistry = new PluginSessionRegistry(_time, Serilog.Core.Logger.None);
		_launchTokenService = new PluginLaunchTokenService(_time, _sessionRegistry);
		_listenerState = new HostListenerState(PublicEndpointSet.HttpOnly(50000), false);
		_listenerState.SetLoopbackPort(50001);
		_mediator = new RecordingMediator();
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
		_trustEvaluator = new FakePluginTrustEvaluator
		{
			InstalledResult = PluginTrustResult.Of(PluginTrustVerdict.Trusted, "cert-fake")
		};
	}

	private static PluginManifestReadResult OkManifestResult() => PluginManifestReadResult.Ok(new PluginManifest
	{
		ManifestVersion = 1,
		Id = PluginId,
		Name = "Example",
		Version = Version,
		Entrypoints = new Dictionary<string, PluginEntrypoint>
		{
			[PluginRuntimeIdentifiers.Current] = new() { Executable = "app" }
		}
	});

	private static InstalledPlugin InstalledFor(string pluginId, string version) => new()
	{
		PluginId = pluginId,
		PluginDirectory = Path.Combine(Path.GetTempPath(), "md-plugins-fake", pluginId),
		Versions =
		[
			new InstalledPluginVersion
			{
				Version = version,
				VersionDirectory = Path.Combine(Path.GetTempPath(), "md-plugins-fake", pluginId, "versions", version),
				ManifestPath = Path.Combine(Path.GetTempPath(),
					"md-plugins-fake",
					pluginId,
					"versions",
					version,
					"manifest.json")
			}
		],
		ActiveVersion = new InstalledPluginVersion
		{
			Version = version,
			VersionDirectory = Path.Combine(Path.GetTempPath(), "md-plugins-fake", pluginId, "versions", version),
			ManifestPath = Path.Combine(Path.GetTempPath(),
				"md-plugins-fake",
				pluginId,
				"versions",
				version,
				"manifest.json")
		}
	};

	private void AddInstalledPlugin(string pluginId)
	{
		var installed = InstalledFor(pluginId, Version);
		_catalog.Plugins.Add(installed);
		_manifestReader.ResultsByPath[installed.ActiveVersion!.ManifestPath] =
			PluginManifestReadResult.Ok(new PluginManifest
			{
				ManifestVersion = 1,
				Id = pluginId,
				Name = pluginId,
				Version = Version,
				Entrypoints = new Dictionary<string, PluginEntrypoint>
				{
					[PluginRuntimeIdentifiers.Current] = new() { Executable = "app" }
				}
			});
	}

	private PluginSupervisor CreateSupervisor()
	{
		var services = new ServiceCollection()
			.AddSingleton<Mediator.IMediator>(_mediator)
			.AddSingleton<IPluginTrustRecordRepository, InMemoryPluginTrustRecordRepository>()
			.AddSingleton<IPluginTrustBaseline, FakePluginTrustBaseline>()
			.BuildServiceProvider();

		return new PluginSupervisor(_manifestReader,
			_catalog,
			_stateStore,
			_journal,
			_launcher,
			_muxerLocator,
			_healthProbe,
			_sessionRegistry,
			_launchTokenService,
			_listenerState,
			_trustEvaluator,
			services.GetRequiredService<IServiceScopeFactory>(),
			_time,
			_options,
			Serilog.Core.Logger.None);
	}

	[Test]
	public async Task Start_sets_all_ten_env_vars_with_the_self_registering_ones_absent()
	{
		var supervisor = CreateSupervisor();

		var result = await supervisor.Start(PluginId);

		Assert.That(result.Success, Is.True, result.Message);
		Assert.That(_launcher.Requests, Has.Count.EqualTo(1));
		var env = _launcher.Requests[0].Environment;

		Assert.Multiple(() =>
		{
			Assert.That(env["MACRO_DECK_PLUGIN_MODE"], Is.EqualTo("Managed"));
			Assert.That(env["MACRO_DECK_PLUGIN_HOST_URL"], Is.EqualTo("http://127.0.0.1:50001"));
			Assert.That(env["MACRO_DECK_PLUGIN_ID"], Is.EqualTo(PluginId));
			Assert.That(env["MACRO_DECK_PLUGIN_SECRET"], Is.Not.Null.And.Not.Empty);
			Assert.That(env["MACRO_DECK_PLUGIN_INSTANCE_ID"], Is.Not.Null.And.Not.Empty);
			Assert.That(env["MACRO_DECK_PLUGIN_LAUNCH_ID"], Is.Not.Null.And.Not.Empty);
			Assert.That(env["MACRO_DECK_PLUGIN_DATA_DIRECTORY"],
				Is.EqualTo(Path.Combine(_catalog.Plugins[0].PluginDirectory, "data")));
			Assert.That(env.Keys, Does.Contain("ASPNETCORE_URLS"));
			Assert.That(env.Keys, Has.No.Member("MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN"));
			Assert.That(env.Keys, Has.No.Member("MACRO_DECK_PLUGIN_STATE_DIRECTORY"));
			Assert.That(env["MACRO_DECK_PLUGIN_HOST_PROCESS_ID"],
				Is.EqualTo(Environment.ProcessId.ToString(CultureInfo.InvariantCulture)));
			Assert.That(DateTimeOffset.TryParse(env["MACRO_DECK_PLUGIN_HOST_STARTED_AT"],
					CultureInfo.InvariantCulture,
					DateTimeStyles.RoundtripKind,
					out _),
				Is.True,
				"the host start time must be a round-trippable timestamp");
			Assert.That(env, Has.Count.EqualTo(10));
			Assert.That(_launcher.Requests[0].WorkingDirectory,
				Is.EqualTo(_catalog.Plugins[0].ActiveVersion!.VersionDirectory));
		});
	}

	[Test]
	public async Task An_inherited_secret_in_the_hosts_own_environment_does_not_leak_into_the_request()
	{
		Environment.SetEnvironmentVariable("MACRO_DECK_PLUGIN_SECRET", "leaked-ambient-secret");
		try
		{
			var supervisor = CreateSupervisor();
			await supervisor.Start(PluginId);

			var env = _launcher.Requests[0].Environment;
			Assert.That(env["MACRO_DECK_PLUGIN_SECRET"], Is.Not.EqualTo("leaked-ambient-secret"));
		}
		finally
		{
			Environment.SetEnvironmentVariable("MACRO_DECK_PLUGIN_SECRET", null);
		}
	}

	[Test]
	public async Task An_unexpected_exit_crashes_then_backs_off_then_restarts_with_a_new_launch_id()
	{
		var supervisor = CreateSupervisor();
		await supervisor.Start(PluginId);
		var firstProcess = _launcher.StartedProcesses[0];
		var firstLaunchId = _launcher.Requests[0].Environment["MACRO_DECK_PLUGIN_LAUNCH_ID"]!;
		var firstSecret = _launcher.Requests[0].Environment["MACRO_DECK_PLUGIN_SECRET"]!;

		firstProcess.CompleteExit(-1);
		await Task.Delay(50); // let the fire-and-forget exit observer run

		var snapshot = supervisor.Snapshot().Single(s => s.PluginId == PluginId);
		Assert.That(snapshot.State, Is.EqualTo(PluginRuntimeState.Backoff));
		Assert.That(snapshot.Health, Is.EqualTo(PluginHealthState.Crashed));

		Assert.That(_launchTokenService.TryAcquire(PluginId, firstSecret, out _), Is.False);

		await supervisor.Reconcile();

		Assert.That(_launcher.Requests, Has.Count.EqualTo(2));
		var secondLaunchId = _launcher.Requests[1].Environment["MACRO_DECK_PLUGIN_LAUNCH_ID"]!;
		var secondSecret = _launcher.Requests[1].Environment["MACRO_DECK_PLUGIN_SECRET"]!;
		Assert.That(secondLaunchId, Is.Not.EqualTo(firstLaunchId));
		Assert.That(_launchTokenService.TryAcquire(PluginId, secondSecret, out _), Is.True);
	}

	[Test]
	public async Task Stop_with_UserRequested_does_not_restart()
	{
		var supervisor = CreateSupervisor();
		await supervisor.Start(PluginId);
		var process = _launcher.StartedProcesses[0];
		process.ExitsOnKill = true;

		var stopTask = supervisor.Stop(PluginId, PluginStopReason.UserRequested);
		process.CompleteExit(0);
		await stopTask;

		await supervisor.Reconcile();

		Assert.That(_launcher.Requests, Has.Count.EqualTo(1), "no automatic restart after a user-requested stop");
		var snapshot = supervisor.Snapshot().Single(s => s.PluginId == PluginId);
		Assert.That(snapshot.State, Is.EqualTo(PluginRuntimeState.Stopped));
		Assert.That(_stateStore.States[PluginId], Is.False);
	}

	[Test]
	public async Task StopAll_with_HostShutdown_restarts_nothing_and_leaves_desired_state_intact()
	{
		var supervisor = CreateSupervisor();
		await supervisor.Start(PluginId);
		var process = _launcher.StartedProcesses[0];

		var stopAllTask = supervisor.StopAll(PluginStopReason.HostShutdown);
		process.CompleteExit(0);
		await stopAllTask;

		// StopAll itself must not trigger a relaunch, and must leave desired state alone - a later
		// reconcile tick relaunching a desired-started plugin (autostart at host ready) is the whole
		// point of leaving that flag untouched, so it is deliberately not asserted away here.
		Assert.That(_launcher.Requests, Has.Count.EqualTo(1));
		Assert.That(_stateStore.States[PluginId], Is.True, "HostShutdown must not touch desired state");
		Assert.That(supervisor.Snapshot().Single(s => s.PluginId == PluginId).State,
			Is.EqualTo(PluginRuntimeState.Stopped));
	}

	[Test]
	public async Task StopAll_terminates_plugins_concurrently_so_shutdown_costs_one_shutdown_budget()
	{
		string[] pluginIds = ["com.example.one", "com.example.two", "com.example.three"];
		foreach (var pluginId in pluginIds)
		{
			AddInstalledPlugin(pluginId);
		}

		var supervisor = CreateSupervisor();
		foreach (var pluginId in pluginIds)
		{
			await supervisor.Start(pluginId);
		}

		var processes = _launcher.StartedProcesses.ToList();
		Assert.That(processes, Has.Count.EqualTo(pluginIds.Length));

		var stopAll = supervisor.StopAll(PluginStopReason.HostShutdown);

		var step = TimeSpan.FromSeconds(1);
		var advanced = TimeSpan.Zero;
		var limit = TimeSpan.FromMinutes(5);
		while (!stopAll.IsCompleted && advanced < limit)
		{
			await Task.Delay(1);
			_time.Advance(step);
			advanced += step;
		}

		await stopAll;

		Assert.That(advanced,
			Is.LessThan(_options.GracefulShutdownTimeout * 2),
			"plugins must be stopped concurrently, not one graceful budget after another");
		Assert.That(processes.Select(p => p.KillTreeCallCount), Is.All.EqualTo(1));
	}

	[Test]
	public async Task A_graceful_stop_sends_goodbye_and_does_not_kill_when_the_process_exits_within_the_grace()
	{
		var supervisor = CreateSupervisor();
		await supervisor.Start(PluginId);
		var process = _launcher.StartedProcesses[0];
		process.ExitsOnKill = false;

		var stopTask = supervisor.Stop(PluginId, PluginStopReason.UserRequested);
		process.CompleteExit(0);
		await stopTask;

		Assert.That(process.KillTreeCallCount, Is.EqualTo(0));
	}

	[Test]
	public async Task A_process_that_never_exits_is_killed_exactly_once_after_the_grace()
	{
		var supervisor = CreateSupervisor();
		await supervisor.Start(PluginId);
		var process = _launcher.StartedProcesses[0];
		process.ExitsOnKill = true;

		var stopTask = supervisor.Stop(PluginId, PluginStopReason.UserRequested);
		_time.Advance(_options.GracefulShutdownTimeout + TimeSpan.FromSeconds(1));
		await stopTask;

		Assert.That(process.KillTreeCallCount, Is.EqualTo(1));
	}

	[Test]
	public async Task Two_consecutive_health_failures_degrade_without_restart_the_third_restarts()
	{
		_healthProbe.Result = false;
		var supervisor = CreateSupervisor();
		await supervisor.Start(PluginId);
		var process = _launcher.StartedProcesses[0];

		_healthProbe.Result = true;
		await supervisor.Reconcile();
		Assert.That(supervisor.Snapshot().Single(s => s.PluginId == PluginId).State,
			Is.EqualTo(PluginRuntimeState.Running));

		_healthProbe.Result = false;

		_time.Advance(_options.HealthPollInterval + TimeSpan.FromSeconds(1));
		await supervisor.Reconcile();
		var afterFirst = supervisor.Snapshot().Single(s => s.PluginId == PluginId);
		Assert.That(afterFirst.Health, Is.EqualTo(PluginHealthState.Degraded));
		Assert.That(process.KillTreeCallCount, Is.EqualTo(0));

		_time.Advance(_options.HealthPollInterval + TimeSpan.FromSeconds(1));
		await supervisor.Reconcile();
		var afterSecond = supervisor.Snapshot().Single(s => s.PluginId == PluginId);
		Assert.That(afterSecond.Health, Is.EqualTo(PluginHealthState.Degraded));
		Assert.That(process.KillTreeCallCount, Is.EqualTo(0), "must not restart after only two missed checks");

		// Probe #3 reaches the (default) threshold of 3 -> Unhealthy -> restart. The termination itself
		// runs in the background (it must not stall the reconcile tick), so give it the grace period
		// and a moment to actually land before asserting.
		_time.Advance(_options.HealthPollInterval + TimeSpan.FromSeconds(1));
		await supervisor.Reconcile();
		_time.Advance(_options.GracefulShutdownTimeout + TimeSpan.FromSeconds(1));
		await Task.Delay(50);

		var afterThird = supervisor.Snapshot().Single(s => s.PluginId == PluginId);
		Assert.That(afterThird.LastStopReason, Is.EqualTo(PluginStopReason.HealthFailure));
	}

	[Test]
	public async Task A_probe_failure_with_a_connected_and_fresh_session_never_escalates_past_degraded()
	{
		_healthProbe.Result = true;
		var supervisor = CreateSupervisor();
		await supervisor.Start(PluginId);

		var launchId = _launcher.Requests[0].Environment["MACRO_DECK_PLUGIN_LAUNCH_ID"]!;
		await AttachSession(launchId);

		await supervisor.Reconcile(); // Starting -> Running via session attach
		Assert.That(supervisor.Snapshot().Single(s => s.PluginId == PluginId).State,
			Is.EqualTo(PluginRuntimeState.Running));

		_healthProbe.Result = false;
		for (var i = 0; i < 5; i++)
		{
			_sessionRegistry.Touch(SessionIdFor(launchId), _time.GetUtcNow());
			_time.Advance(_options.HealthPollInterval + TimeSpan.FromSeconds(1));
			await supervisor.Reconcile();
		}

		var snapshot = supervisor.Snapshot().Single(s => s.PluginId == PluginId);
		Assert.That(snapshot.Health, Is.Not.EqualTo(PluginHealthState.Unhealthy));
		Assert.That(_launcher.Requests, Has.Count.EqualTo(1), "a live session must prevent a health-driven restart");
	}

	[Test]
	public async Task Budget_exhaustion_fails_permanently_and_a_manual_start_clears_the_counters()
	{
		var supervisor = CreateSupervisor();
		await supervisor.Start(PluginId);

		// The very first early exit gets one free startup-grace retry that does not count against the
		// budget (the TOCTOU health-port race), so exhausting a MaxRestarts budget takes one extra crash.
		for (var i = 0; i < _options.MaxRestarts + 1; i++)
		{
			var process = _launcher.StartedProcesses[^1];
			process.CompleteExit(-1);
			await Task.Delay(30);
			_time.Advance(TimeSpan.FromMinutes(1));
			await supervisor.Reconcile();
		}

		var snapshot = supervisor.Snapshot().Single(s => s.PluginId == PluginId);
		Assert.That(snapshot.State, Is.EqualTo(PluginRuntimeState.Failed));

		var launchCountAtFailure = _launcher.Requests.Count;
		await supervisor.Reconcile();
		Assert.That(_launcher.Requests,
			Has.Count.EqualTo(launchCountAtFailure),
			"Failed must not launch again on its own");

		var restart = await supervisor.Start(PluginId);
		Assert.That(restart.Success, Is.True);
		Assert.That(supervisor.Snapshot().Single(s => s.PluginId == PluginId).State,
			Is.Not.EqualTo(PluginRuntimeState.Failed));
	}

	[Test]
	public async Task A_self_registering_plugin_short_circuits_before_the_launcher()
	{
		_catalog.Plugins.Clear();
		await CreateSelfRegisteringSession(PluginId);
		var supervisor = CreateSupervisor();

		var start = await supervisor.Start(PluginId);
		var stop = await supervisor.Stop(PluginId, PluginStopReason.UserRequested);
		var restart = await supervisor.Restart(PluginId);

		Assert.Multiple(() =>
		{
			Assert.That(start.Error, Is.EqualTo(PluginSupervisorError.SelfRegistering));
			Assert.That(stop.Error, Is.EqualTo(PluginSupervisorError.SelfRegistering));
			Assert.That(restart.Error, Is.EqualTo(PluginSupervisorError.SelfRegistering));
			Assert.That(_launcher.Requests, Is.Empty);
		});
	}

	[Test]
	public async Task A_self_registering_plugins_snapshot_reports_its_declared_version()
	{
		_catalog.Plugins.Clear();
		await CreateSelfRegisteringSession(PluginId, declaredVersion: "1.0.0");
		var supervisor = CreateSupervisor();

		var snapshot = supervisor.Snapshot().Single(s => s.PluginId == PluginId);

		Assert.That(snapshot.Version, Is.EqualTo("1.0.0"));
	}

	[Test]
	public async Task A_plugin_directory_with_no_versions_left_is_not_reported_or_started()
	{
		_catalog.Plugins.Add(new InstalledPlugin
		{
			PluginId = "com.example.uninstalled",
			PluginDirectory = Path.Combine(Path.GetTempPath(), "md-plugins-fake", "com.example.uninstalled"),
			Versions = [],
			ActiveVersion = null
		});
		_stateStore.States[PluginId] = true;
		_stateStore.States["com.example.uninstalled"] = true;
		var supervisor = CreateSupervisor();

		await supervisor.Reconcile();

		Assert.Multiple(() =>
		{
			Assert.That(supervisor.Snapshot().Select(snapshot => snapshot.PluginId),
				Is.EquivalentTo(new[] { PluginId }));
			Assert.That(_launcher.Requests.Select(request => request.Environment["MACRO_DECK_PLUGIN_ID"]),
				Is.EquivalentTo(new[] { PluginId }));
		});
	}

	[Test]
	public async Task Forgetting_a_plugin_drops_its_row_and_its_desired_start_state()
	{
		_stateStore.States[PluginId] = true;
		var supervisor = CreateSupervisor();
		await supervisor.Reconcile();

		_catalog.Plugins.Clear();
		await supervisor.Forget(PluginId);

		Assert.Multiple(() =>
		{
			Assert.That(supervisor.Snapshot(), Is.Empty);
			Assert.That(_stateStore.States.ContainsKey(PluginId), Is.False);
		});
	}

	[Test]
	public async Task An_id_that_is_neither_installed_nor_self_registering_reports_NotInstalled()
	{
		_catalog.Plugins.Clear();
		var supervisor = CreateSupervisor();

		var start = await supervisor.Start(PluginId);
		var stop = await supervisor.Stop(PluginId, PluginStopReason.UserRequested);
		var restart = await supervisor.Restart(PluginId);

		Assert.Multiple(() =>
		{
			Assert.That(start.Error, Is.EqualTo(PluginSupervisorError.NotInstalled));
			Assert.That(stop.Error, Is.EqualTo(PluginSupervisorError.NotInstalled));
			Assert.That(restart.Error, Is.EqualTo(PluginSupervisorError.NotInstalled));
			Assert.That(_launcher.Requests, Is.Empty);
		});
	}

	[Test]
	public async Task An_installed_plugin_that_has_never_started_reports_its_manifest_name_and_installed_version()
	{
		_manifestReader.Default = PluginManifestReadResult.Ok(new PluginManifest
		{
			ManifestVersion = 1,
			Id = PluginId,
			Name = "Weather Widget",
			Version = Version,
			Entrypoints = new Dictionary<string, PluginEntrypoint>
			{
				[PluginRuntimeIdentifiers.Current] = new() { Executable = "app" }
			}
		});
		var supervisor = CreateSupervisor();

		await supervisor.Reconcile();

		var snapshot = supervisor.Snapshot().Single(s => s.PluginId == PluginId);
		Assert.Multiple(() =>
		{
			Assert.That(snapshot.DisplayName, Is.EqualTo("Weather Widget"));
			Assert.That(snapshot.Version, Is.EqualTo("1.0.0"));
			Assert.That(snapshot.Managed, Is.True);
		});

		Assert.That(_launcher.Requests, Is.Empty);
	}

	[Test]
	public async Task An_installed_plugin_whose_manifest_cannot_be_read_still_reports_a_version()
	{
		const string brokenId = "com.example.broken";

		var broken = InstalledFor(brokenId, "1.0.0");
		_catalog.Plugins.Add(broken);
		_manifestReader.ResultsByPath[broken.ActiveVersion!.ManifestPath] =
			PluginManifestReadResult.Fail(PluginManifestError.Malformed, "Manifest is corrupt.");

		var supervisor = CreateSupervisor();
		await supervisor.Reconcile();

		var brokenSnapshot = supervisor.Snapshot().Single(snapshot => snapshot.PluginId == brokenId);

		Assert.Multiple(() =>
		{
			Assert.That(brokenSnapshot.Version, Is.EqualTo("1.0.0"));
			Assert.That(brokenSnapshot.Version, Is.Not.EqualTo(string.Empty));
			Assert.That(brokenSnapshot.DisplayName, Is.EqualTo(brokenId));
		});
	}

	[Test]
	public async Task Repeated_reconcile_ticks_neither_re_read_a_manifest_nor_re_broadcast()
	{
		var supervisor = CreateSupervisor();
		await supervisor.Reconcile();

		var readsAfterFirstTick = _manifestReader.Reads.Count;
		var publishedAfterFirstTick = _mediator.Published.Count;

		for (var i = 0; i < 4; i++)
		{
			await supervisor.Reconcile();
		}

		Assert.Multiple(() =>
		{
			Assert.That(_manifestReader.Reads.Count, Is.EqualTo(readsAfterFirstTick));
			Assert.That(_mediator.Published.Count, Is.EqualTo(publishedAfterFirstTick));
		});
	}

	[Test]
	public async Task A_state_change_still_broadcasts()
	{
		var supervisor = CreateSupervisor();

		var result = await supervisor.Start(PluginId);

		Assert.That(result.Success, Is.True, result.Message);
		Assert.That(_mediator.Published, Has.Some.TypeOf<PluginRuntimeChangedNotification>());
	}

	[Test]
	public async Task Bootstrap_output_survives_the_crash_that_makes_it_worth_reading()
	{
		var supervisor = CreateSupervisor();
		await supervisor.Start(PluginId);
		var process = _launcher.StartedProcesses[0];
		process.BootstrapOutput = ["could not bind", "fatal: giving up"];

		process.CompleteExit(-1);
		await Task.Delay(50); // let the fire-and-forget exit observer run

		string[] expected = ["could not bind", "fatal: giving up"];
		var snapshot = supervisor.Snapshot().Single(s => s.PluginId == PluginId);
		Assert.That(snapshot.BootstrapOutput, Is.EqualTo(expected));
	}

	[Test]
	public async Task A_throwing_launcher_reports_LaunchFailed_and_does_not_propagate()
	{
		_launcher.ThrowOnStart = new InvalidOperationException("boom");
		var supervisor = CreateSupervisor();

		PluginSupervisorResult? result = null;
		Assert.DoesNotThrowAsync(async () => result = await supervisor.Start(PluginId));

		Assert.Multiple(() =>
		{
			Assert.That(result!.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginSupervisorError.LaunchFailed));
		});
	}

	private async Task CreateSelfRegisteringSession(string pluginId, string? declaredVersion = null)
	{
		await _sessionRegistry.Create(new PluginSessionRecord
		{
			SessionId = $"self-{pluginId}",
			PluginId = pluginId,
			DisplayName = "Self-registering",
			AccessTokenId = Guid.CreateVersion7(),
			Origin = PluginSessionOrigin.SelfRegistered,
			NegotiatedVersion = 1,
			DeclaredVersion = declaredVersion,
			Capabilities = new Dictionary<string, MacroDeck.Plugin.Protocol.Versioning.CapabilityNegotiationResult>(),
			DeclaredCapabilities = [],
			CreatedAt = _time.GetUtcNow()
		});
	}

	private async Task AttachSession(string launchId)
	{
		// The supervisor already minted and holds the real secret internally, which the test cannot see.
		// Standing in for the SDK, mint a fresh matching secret through the same service by re-minting
		// for the same launch id (Start's own Mint already registered this launch id).
		var freshSecret = _launchTokenService.Mint(PluginId, launchId, "Example", Version);
		Assert.That(_launchTokenService.TryAcquire(PluginId, freshSecret, out var launch), Is.True);

		var sessionId = SessionIdFor(launchId);
		await _sessionRegistry.Create(new PluginSessionRecord
		{
			SessionId = sessionId,
			PluginId = PluginId,
			DisplayName = "Example",
			AccessTokenId = null,
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, MacroDeck.Plugin.Protocol.Versioning.CapabilityNegotiationResult>(),
			DeclaredCapabilities = [],
			CreatedAt = _time.GetUtcNow()
		});
		_sessionRegistry.TryAttach(sessionId, new FakePluginConnection(), null);
		_launchTokenService.Bind(launch!.LaunchId, sessionId);
	}

	private static string SessionIdFor(string launchId) => $"session-{launchId}";

	[Test]
	public async Task A_started_plugin_is_journalled_with_its_pid_start_time_and_launch_id()
	{
		var supervisor = CreateSupervisor();

		await supervisor.Start(PluginId);

		var process = _launcher.StartedProcesses[0];
		var entry = _journal.Entries.Single();
		Assert.Multiple(() =>
		{
			Assert.That(entry.PluginId, Is.EqualTo(PluginId));
			Assert.That(entry.ProcessId, Is.EqualTo(process.Id));
			Assert.That(entry.StartedAt, Is.EqualTo(process.StartedAt));
			Assert.That(entry.LaunchId, Is.EqualTo(_launcher.Requests[0].Environment["MACRO_DECK_PLUGIN_LAUNCH_ID"]));
		});
	}

	[Test]
	public async Task A_clean_stop_leaves_nothing_in_the_journal()
	{
		var supervisor = CreateSupervisor();
		await supervisor.Start(PluginId);
		var process = _launcher.StartedProcesses[0];

		var stopTask = supervisor.Stop(PluginId, PluginStopReason.UserRequested);
		process.CompleteExit(0);
		await stopTask;

		Assert.That(_journal.Entries, Is.Empty);
	}

	[Test]
	public async Task StopAll_leaves_every_plugin_exited_and_the_journal_empty()
	{
		string[] pluginIds = ["com.example.one", "com.example.two", "com.example.three"];
		foreach (var pluginId in pluginIds)
		{
			AddInstalledPlugin(pluginId);
		}

		var supervisor = CreateSupervisor();
		foreach (var pluginId in pluginIds)
		{
			await supervisor.Start(pluginId);
		}

		var processes = _launcher.StartedProcesses.ToList();
		var stopAll = supervisor.StopAll(PluginStopReason.HostShutdown);
		await AdvanceUntilComplete(stopAll);

		Assert.Multiple(() =>
		{
			Assert.That(processes.Select(process => process.HasExited), Is.All.True);
			Assert.That(_journal.Entries, Is.Empty);
		});
	}

	[Test]
	public async Task A_process_that_survives_its_kill_is_still_journalled_until_its_exit_is_observed()
	{
		var supervisor = CreateSupervisor();
		await supervisor.Start(PluginId);
		var process = _launcher.StartedProcesses[0];
		process.ExitsOnKill = false;

		var stopTask = supervisor.Stop(PluginId, PluginStopReason.UserRequested);
		await AdvanceUntilComplete(stopTask);

		Assert.That(process.KillTreeCallCount, Is.EqualTo(1));

		process.CompleteExit(-1);
		await WaitUntil(() => _journal.Entries.Count == 0);

		Assert.That(_journal.Entries, Is.Empty);
	}

	private async Task AdvanceUntilComplete(Task task)
	{
		var advanced = TimeSpan.Zero;
		var limit = TimeSpan.FromMinutes(5);
		while (!task.IsCompleted && advanced < limit)
		{
			await Task.Delay(1);
			_time.Advance(TimeSpan.FromSeconds(1));
			advanced += TimeSpan.FromSeconds(1);
		}

		await task;
	}

	private static async Task WaitUntil(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (DateTime.UtcNow < deadline && !condition())
		{
			await Task.Delay(5);
		}
	}
}
