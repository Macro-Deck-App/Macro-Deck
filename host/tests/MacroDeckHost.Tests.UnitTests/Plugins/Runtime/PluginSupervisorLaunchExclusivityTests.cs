using MacroDeck.Plugin.Packaging.Manifest;
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
internal sealed class PluginSupervisorLaunchExclusivityTests
{
	private const string PluginId = "com.example.plugin";
	private const string Version = "1.0.0";

	private ManualTimeProvider _time = null!;
	private FakePluginManifestReader _manifestReader = null!;
	private FakePluginInstallationCatalog _catalog = null!;
	private FakePluginRuntimeStateStore _stateStore = null!;
	private FakePluginProcessJournal _journal = null!;
	private FakePluginProcessLauncher _launcher = null!;
	private PluginSessionRegistry _sessionRegistry = null!;
	private PluginLaunchTokenService _launchTokenService = null!;
	private GatedTrustEvaluator _trust = null!;
	private PluginSupervisorOptions _options = null!;
	private List<FakePluginProcess> _spawned = null!;
	private int _spawnsWhileAnotherWasAlive;
	private Action? _onSpawn;
	private PluginSupervisor _supervisor = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_manifestReader = new FakePluginManifestReader { Default = ManifestWithShutdown(10) };
		_catalog = new FakePluginInstallationCatalog();
		_catalog.Plugins.Add(Installed());
		_stateStore = new FakePluginRuntimeStateStore();
		_journal = new FakePluginProcessJournal();
		_sessionRegistry = new PluginSessionRegistry(_time, Serilog.Core.Logger.None);
		_launchTokenService = new PluginLaunchTokenService(_time, _sessionRegistry);
		_trust = new GatedTrustEvaluator();
		_spawned = [];
		_spawnsWhileAnotherWasAlive = 0;
		_onSpawn = null;
		_launcher = new FakePluginProcessLauncher
		{
			OnStart = _ =>
			{
				if (_spawned.Any(process => !process.HasExited))
				{
					_spawnsWhileAnotherWasAlive++;
				}

				var process = new FakePluginProcess { Id = 1000 + _spawned.Count };
				_spawned.Add(process);
				_onSpawn?.Invoke();
				return process;
			}
		};
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
	public async Task A_restart_racing_a_reconcile_tick_launches_one_process()
	{
		_manifestReader.Default = ManifestWithShutdown(0);
		var supervisor = CreateSupervisor();
		await StartFirstProcess(supervisor);

		var held = _trust.Hold();
		var restart = supervisor.Restart(PluginId);
		await held.Entered;
		await supervisor.Reconcile();
		held.Release();
		var result = await restart;

		AssertSingleLiveProcess(result, 2);
	}

	[Test]
	public async Task A_start_from_backoff_racing_a_reconcile_tick_launches_one_process()
	{
		var supervisor = CreateSupervisor();
		await StartFirstProcess(supervisor);
		await CrashIntoBackoff();

		var held = _trust.Hold();
		var start = supervisor.Start(PluginId);
		await held.Entered;
		await supervisor.Reconcile();
		held.Release();
		var result = await start;

		AssertSingleLiveProcess(result, 2);
	}

	[Test]
	public async Task Two_concurrent_starts_launch_one_process()
	{
		var supervisor = CreateSupervisor();

		var held = _trust.Hold();
		var first = supervisor.Start(PluginId);
		await held.Entered;
		var second = supervisor.Start(PluginId);
		held.Release();
		var firstResult = await first;
		var secondResult = await second;

		Assert.That(secondResult.Error, Is.EqualTo(PluginSupervisorError.AlreadyRunning));
		AssertSingleLiveProcess(firstResult, 1);
	}

	[Test]
	public async Task Two_concurrent_restarts_leave_one_live_process()
	{
		_manifestReader.Default = ManifestWithShutdown(0);
		var supervisor = CreateSupervisor();
		await StartFirstProcess(supervisor);

		var held = _trust.Hold();
		var first = supervisor.Restart(PluginId);
		await held.Entered;
		var second = supervisor.Restart(PluginId);
		held.Release();
		var firstResult = await first;
		var secondResult = await second;

		Assert.That(firstResult.Success, Is.True, firstResult.Message);
		AssertSingleLiveProcess(secondResult, 3);
	}

	[Test]
	public async Task A_stop_during_a_start_from_backoff_wins()
	{
		var supervisor = CreateSupervisor();
		await StartFirstProcess(supervisor);
		await CrashIntoBackoff();

		var held = _trust.Hold();
		var start = supervisor.Start(PluginId);
		await held.Entered;
		var stop = await supervisor.Stop(PluginId, PluginStopReason.UserRequested);
		held.Release();
		var result = await start;

		AssertStopWonWithoutLaunching(stop, result, 1);
	}

	[Test]
	public async Task A_stop_during_a_start_from_stopped_wins()
	{
		var supervisor = CreateSupervisor();

		var held = _trust.Hold();
		var start = supervisor.Start(PluginId);
		await held.Entered;
		var stop = await supervisor.Stop(PluginId, PluginStopReason.UserRequested);
		held.Release();
		var result = await start;

		AssertStopWonWithoutLaunching(stop, result, 0);
	}

	[Test]
	public async Task A_stop_that_lands_while_the_process_is_being_spawned_stops_that_process()
	{
		var supervisor = CreateSupervisor();
		Task<PluginSupervisorResult>? stop = null;
		_onSpawn = () =>
		{
			_onSpawn = null;
			stop = supervisor.Stop(PluginId, PluginStopReason.UserRequested);
		};

		var result = await supervisor.Start(PluginId);
		var stopResult = await stop!;
		var process = _spawned.Single();
		await AdvanceUntil(() => process.HasExited && Snapshot().State == PluginRuntimeState.Stopped);

		var snapshot = Snapshot();
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(result.Success, Is.False, "the start was overtaken by the stop");
			Assert.That(stopResult.Success, Is.True, stopResult.Message);
			Assert.That(snapshot.LastStopReason, Is.EqualTo(PluginStopReason.UserRequested));
			Assert.That(_launchTokenService.HasActiveLaunch(PluginId), Is.False);
			Assert.That(_journal.Load().Entries, Is.Empty);
			Assert.That(_stateStore.States[PluginId], Is.False);

			await supervisor.Reconcile();
			Assert.That(_launcher.Requests, Has.Count.EqualTo(1), "the stopped plugin is not relaunched");
		});
	}

	[Test]
	public async Task A_start_while_the_previous_process_is_still_stopping_launches_nothing()
	{
		var supervisor = CreateSupervisor();
		var first = await StartFirstProcess(supervisor);

		var scheduled = _time.ScheduledCount;
		var stop = supervisor.Stop(PluginId, PluginStopReason.UserRequested);
		await _time.WaitForScheduleAsync(scheduled);
		var start = await supervisor.Start(PluginId);
		first.CompleteExit(0);
		await stop;

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(start.Error, Is.EqualTo(PluginSupervisorError.AlreadyRunning));
			Assert.That(_launcher.Requests, Has.Count.EqualTo(1));
			Assert.That(_spawnsWhileAnotherWasAlive, Is.Zero);
			Assert.That(Snapshot().State, Is.EqualTo(PluginRuntimeState.Stopped));
			Assert.That(_journal.Load().Entries, Is.Empty);
			Assert.That(_launchTokenService.HasActiveLaunch(PluginId), Is.False);

			await supervisor.Reconcile();
			Assert.That(_launcher.Requests, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_restart_while_the_previous_process_is_still_stopping_waits_for_it()
	{
		var supervisor = CreateSupervisor();
		var first = await StartFirstProcess(supervisor);

		var scheduled = _time.ScheduledCount;
		var stop = supervisor.Stop(PluginId, PluginStopReason.UserRequested);
		await _time.WaitForScheduleAsync(scheduled);
		var restart = supervisor.Restart(PluginId);
		first.CompleteExit(0);
		await stop;
		var result = await restart;

		Assert.That(_stateStore.States[PluginId], Is.True, "a restart asks for the plugin to keep running");
		AssertSingleLiveProcess(result, 2);
	}

	[Test]
	public async Task A_restart_whose_old_process_survives_the_kill_launches_nothing()
	{
		_manifestReader.Default = ManifestWithShutdown(0);
		var supervisor = CreateSupervisor();
		var first = await StartFirstProcess(supervisor);
		first.ExitsOnKill = false;

		var scheduled = _time.ScheduledCount;
		var restart = supervisor.Restart(PluginId);
		await _time.WaitForScheduleAsync(scheduled);
		_time.Advance(TimeSpan.FromSeconds(3));
		var result = await restart;

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(first.KillTreeCallCount, Is.GreaterThan(0), "precondition: the kill was attempted");
			Assert.That(_launcher.Requests, Has.Count.EqualTo(1));
			Assert.That(_spawnsWhileAnotherWasAlive, Is.Zero);
			Assert.That(_journal.Load().Entries, Has.Count.EqualTo(1), "the surviving process stays journaled");
		});
	}

	[Test]
	public async Task A_stop_during_the_terminate_phase_of_a_restart_wins()
	{
		var supervisor = CreateSupervisor();
		var first = await StartFirstProcess(supervisor);

		var scheduled = _time.ScheduledCount;
		var restart = supervisor.Restart(PluginId);
		await _time.WaitForScheduleAsync(scheduled);
		var stop = supervisor.Stop(PluginId, PluginStopReason.UserRequested);
		first.CompleteExit(0);
		var stopResult = await stop;
		var restartResult = await restart;
		await supervisor.Reconcile();

		Assert.Multiple(() =>
		{
			Assert.That(stopResult.Success, Is.True, stopResult.Message);
			Assert.That(restartResult.Success, Is.False, "the restart was overtaken by the stop");
			Assert.That(_launcher.Requests, Has.Count.EqualTo(1));
			Assert.That(Snapshot().State, Is.EqualTo(PluginRuntimeState.Stopped));
			Assert.That(Snapshot().LastStopReason, Is.EqualTo(PluginStopReason.UserRequested));
			Assert.That(_stateStore.States[PluginId], Is.False);
			Assert.That(_journal.Load().Entries, Is.Empty);
		});
	}

	[Test]
	public async Task A_tick_does_not_relaunch_a_plugin_stopped_while_it_worked_on_another_one()
	{
		const string otherId = "com.example.other";
		_catalog.Plugins.Insert(0, Installed(otherId));
		var supervisor = CreateSupervisor();
		await supervisor.Reconcile();
		await StartFirstProcess(supervisor);
		var plugin = _spawned.Single();
		_stateStore.States[otherId] = true;

		var held = _trust.Hold();
		var tick = supervisor.Reconcile();
		await held.Entered;
		var stop = supervisor.Stop(PluginId, PluginStopReason.UserRequested);
		plugin.CompleteExit(0);
		await stop;
		held.Release();
		await tick;

		Assert.Multiple(() =>
		{
			Assert.That(LaunchesOf(otherId), Is.EqualTo(1), "precondition: the tick launched the other plugin");
			Assert.That(LaunchesOf(PluginId), Is.EqualTo(1), "the stopped plugin was relaunched");
			Assert.That(_stateStore.States[PluginId], Is.False);
			Assert.That(Snapshot().State, Is.EqualTo(PluginRuntimeState.Stopped));
		});
	}

	[Test]
	public async Task A_caller_cancelled_while_the_process_is_spawned_still_leaves_it_managed()
	{
		var supervisor = CreateSupervisor();
		using var cancellation = new CancellationTokenSource();
		_onSpawn = () =>
		{
			_onSpawn = null;
			cancellation.Cancel();
		};

		await supervisor.Start(PluginId, cancellation.Token);
		var process = _spawned.Single();
		var published = Snapshot().State;

		var scheduled = _time.ScheduledCount;
		var stop = supervisor.Stop(PluginId, PluginStopReason.UserRequested);
		await _time.WaitForScheduleAsync(scheduled).WaitAsync(TimeSpan.FromSeconds(10));
		process.CompleteExit(0);
		var stopResult = await stop;
		var restart = await supervisor.Start(PluginId);

		Assert.Multiple(() =>
		{
			Assert.That(published, Is.EqualTo(PluginRuntimeState.Starting), "the spawned process was published");
			Assert.That(stopResult.Success, Is.True, stopResult.Message);
			Assert.That(restart.Success, Is.True, restart.Message);
			Assert.That(_spawnsWhileAnotherWasAlive, Is.Zero);
			Assert.That(_journal.Load().Entries.Select(entry => entry.ProcessId),
				Is.EqualTo(new[] { _spawned[^1].Id }));
		});
	}

	[Test]
	public async Task Host_shutdown_that_lands_while_the_process_is_being_spawned_stops_that_process()
	{
		var supervisor = CreateSupervisor();
		Task? stopAll = null;
		_onSpawn = () =>
		{
			_onSpawn = null;
			stopAll = supervisor.StopAll(PluginStopReason.HostShutdown);
		};

		var result = await supervisor.Start(PluginId);
		await stopAll!;
		var process = _spawned.Single();
		await AdvanceUntil(() => process.HasExited && Snapshot().State == PluginRuntimeState.Stopped);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(PluginSupervisorError.HostShuttingDown));
			Assert.That(Snapshot().LastStopReason, Is.EqualTo(PluginStopReason.HostShutdown));
			Assert.That(_launchTokenService.HasActiveLaunch(PluginId), Is.False);
			Assert.That(_journal.Load().Entries, Is.Empty);
		});
	}

	[Test]
	public async Task Host_shutdown_is_not_blocked_by_a_launch_in_progress_and_the_launch_spawns_nothing()
	{
		var supervisor = CreateSupervisor();

		var held = _trust.Hold();
		var start = supervisor.Start(PluginId);
		await held.Entered;
		await supervisor.StopAll(PluginStopReason.HostShutdown);
		held.Release();
		var result = await start;

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(PluginSupervisorError.HostShuttingDown));
			Assert.That(_launcher.Requests, Is.Empty);
			Assert.That(_launchTokenService.HasActiveLaunch(PluginId), Is.False);
		});
	}

	private async Task<FakePluginProcess> StartFirstProcess(PluginSupervisor supervisor)
	{
		var result = await supervisor.Start(PluginId);
		Assert.That(result.Success, Is.True, result.Message);
		return _spawned.Single();
	}

	private async Task CrashIntoBackoff()
	{
		_spawned[^1].CompleteExit(1);
		await AdvanceUntil(() => Snapshot().State == PluginRuntimeState.Backoff, false);
	}

	private void AssertSingleLiveProcess(PluginSupervisorResult result, int expectedLaunches)
	{
		var live = _spawned.Where(process => !process.HasExited).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.Message);
			Assert.That(_launcher.Requests, Has.Count.EqualTo(expectedLaunches));
			Assert.That(_spawnsWhileAnotherWasAlive, Is.Zero, "a process was spawned while another was alive");
			Assert.That(live, Has.Count.EqualTo(1));
			Assert.That(_journal.Load().Entries.Select(entry => entry.ProcessId), Is.EqualTo(live.Select(p => p.Id)));
			Assert.That(Snapshot().State, Is.EqualTo(PluginRuntimeState.Starting));
		});
	}

	private void AssertStopWonWithoutLaunching(
		PluginSupervisorResult stop,
		PluginSupervisorResult start,
		int launchesBefore)
	{
		var snapshot = Snapshot();

		Assert.Multiple(() =>
		{
			Assert.That(stop.Success, Is.True, stop.Message);
			Assert.That(start.Success, Is.False, "the start was overtaken by the stop");
			Assert.That(_launcher.Requests, Has.Count.EqualTo(launchesBefore), "no process after the stop");
			Assert.That(snapshot.State, Is.EqualTo(PluginRuntimeState.Stopped));
			Assert.That(_stateStore.States[PluginId], Is.False);
			Assert.That(_launchTokenService.HasActiveLaunch(PluginId), Is.False);
			Assert.That(_journal.Load().Entries, Is.Empty);
		});
	}

	private PluginRuntimeSnapshot Snapshot()
	{
		return _supervisor.Snapshot().Single(snapshot => snapshot.PluginId == PluginId);
	}

	private async Task AdvanceUntil(Func<bool> condition, bool advanceTime = true)
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (!condition())
		{
			if (DateTime.UtcNow >= deadline)
			{
				Assert.Fail("The expected supervisor state was never reached.");
			}

			if (advanceTime)
			{
				_time.Advance(TimeSpan.FromSeconds(1));
			}

			await Task.Delay(10);
		}
	}

	private PluginSupervisor CreateSupervisor()
	{
		var services = new ServiceCollection()
			.AddSingleton<Mediator.IMediator>(new RecordingMediator())
			.AddSingleton<IPluginTrustRecordRepository, InMemoryPluginTrustRecordRepository>()
			.AddSingleton<IPluginTrustBaseline, FakePluginTrustBaseline>()
			.BuildServiceProvider();

		var listenerState = new HostListenerState(PublicEndpointSet.HttpOnly(50000), false);
		listenerState.SetLoopbackPort(50001);

		_supervisor = new PluginSupervisor(_manifestReader,
			_catalog,
			new CopyingStateStore(_stateStore),
			_journal,
			_launcher,
			new FakeDotnetMuxerLocator(),
			new FakePluginHealthProbe(),
			_sessionRegistry,
			_launchTokenService,
			listenerState,
			_trust,
			new PluginTakeoverRegistry(),
			services.GetRequiredService<IServiceScopeFactory>(),
			_time,
			_options,
			Serilog.Core.Logger.None);
		return _supervisor;
	}

	private static PluginManifestReadResult ManifestWithShutdown(int gracefulTimeoutSeconds)
	{
		return PluginManifestReadResult.Ok(new PluginManifest
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
	}

	private int LaunchesOf(string pluginId)
	{
		return _launcher.Requests.Count(request => request.Environment["MACRO_DECK_PLUGIN_ID"] == pluginId);
	}

	private static InstalledPlugin Installed(string pluginId = PluginId)
	{
		var directory = Path.Combine(Path.GetTempPath(), "md-plugins-fake", pluginId);
		var version = new InstalledPluginVersion
		{
			Version = Version,
			VersionDirectory = Path.Combine(directory, "versions", Version),
			ManifestPath = Path.Combine(directory, "versions", Version, "manifest.json")
		};

		return new InstalledPlugin
		{
			PluginId = pluginId,
			PluginDirectory = directory,
			Versions = [version],
			ActiveVersion = version
		};
	}

	// Like the persisted store, a load is a copy: a later save must not change a tick's earlier read.
	private sealed class CopyingStateStore : IPluginRuntimeStateStore
	{
		private readonly FakePluginRuntimeStateStore _inner;

		public CopyingStateStore(FakePluginRuntimeStateStore inner)
		{
			_inner = inner;
		}

		public IReadOnlyDictionary<string, bool> Load()
		{
			return new Dictionary<string, bool>(_inner.States, StringComparer.Ordinal);
		}

		public Task Save(string pluginId, bool started)
		{
			return _inner.Save(pluginId, started);
		}

		public Task Remove(string pluginId)
		{
			return _inner.Remove(pluginId);
		}
	}

	private sealed class GatedTrustEvaluator : IPluginTrustEvaluator
	{
		private TrustHold? _next;

		public TrustHold Hold()
		{
			var hold = new TrustHold();
			_next = hold;
			return hold;
		}

		public async Task<PluginTrustResult> EvaluateInstalledAsync(
			string versionDirectory,
			PluginRevocationCheck revocationCheck,
			CancellationToken cancellationToken = default)
		{
			if (Interlocked.Exchange(ref _next, null) is { } hold)
			{
				hold.SignalEntered();
				await hold.Released.WaitAsync(cancellationToken);
			}

			return PluginTrustResult.Of(PluginTrustVerdict.Trusted, "cert-fake");
		}
	}

	private sealed class TrustHold
	{
		private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task Entered => _entered.Task;

		public Task Released => _released.Task;

		public void SignalEntered()
		{
			_entered.TrySetResult();
		}

		public void Release()
		{
			_released.TrySetResult();
		}
	}
}
