using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Plugins.Trust;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Plugins.Installation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Installation;

[TestFixture]
internal sealed class PluginUpdateActivationRaceTests
{
	private const string PluginId = ManifestJson.DefaultPluginId;

	private TestPaths _paths = null!;
	private PluginInstallationCatalog _catalog = null!;
	private FakePluginProcessLauncher _launcher = null!;
	private FakePluginHealthProbe _healthProbe = null!;
	private FakePluginRuntimeStateStore _stateStore = null!;
	private HookedRegistrationRepository _registrations = null!;
	private HookedTrustRecordRepository _trustRecords = null!;
	private HookedTrustEvaluator _trust = null!;
	private string _currentVersionTemplate = string.Empty;
	private PluginSupervisor _supervisor = null!;
	private PluginInstaller _installer = null!;
	private string _sourceDirectory = null!;
	private readonly SemaphoreSlim _tickLock = new(1, 1);
	private readonly HashSet<string> _unhealthyVersions = new(StringComparer.Ordinal);

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.PluginsDirectory);
		Directory.CreateDirectory(_paths.PluginStagingDirectory);
		Directory.CreateDirectory(_paths.PluginCacheDirectory);
		_sourceDirectory = Path.Combine(_paths.BaseDirectory, "artifacts");
		Directory.CreateDirectory(_sourceDirectory);
		_unhealthyVersions.Clear();

		_catalog = new PluginInstallationCatalog(_paths, Serilog.Core.Logger.None);
		_registrations = new HookedRegistrationRepository();
		_trustRecords = new HookedTrustRecordRepository();

		var services = new ServiceCollection();
		services.AddSingleton<IPluginRegistrationRepository>(_registrations);
		services.AddSingleton<IPluginTrustRecordRepository>(_trustRecords);
		services.AddSingleton<IPluginTrustBaseline>(new FakePluginTrustBaseline());
		services.AddSingleton<IPluginRegistrationService>(new RevokingRegistrationService(_registrations));
		var provider = services.BuildServiceProvider();
		var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

		var manifestReader = new PluginManifestReader();
		var trust = _trust = new HookedTrustEvaluator();
		var sessions = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		var takeovers = new PluginTakeoverRegistry();
		var listenerState = new HostListenerState(PublicEndpointSet.HttpOnly(50200), false);
		listenerState.SetLoopbackPort(50201);

		_launcher = new FakePluginProcessLauncher();
		_healthProbe = new FakePluginHealthProbe { OnProbe = (port, _) => !IsUnhealthy(port) };
		_stateStore = new FakePluginRuntimeStateStore();

		_supervisor = new PluginSupervisor(manifestReader,
			_catalog,
			_stateStore,
			new FakePluginProcessJournal(),
			_launcher,
			new FakeDotnetMuxerLocator(),
			_healthProbe,
			sessions,
			new PluginLaunchTokenService(TimeProvider.System, sessions),
			listenerState,
			trust,
			takeovers,
			scopeFactory,
			TimeProvider.System,
			new PluginSupervisorOptions
			{
				GracefulShutdownTimeout = TimeSpan.FromMilliseconds(50),
				StartupGrace = TimeSpan.FromSeconds(30),
				HealthPollInterval = TimeSpan.Zero,
				HealthTimeout = TimeSpan.FromSeconds(2),
				UnhealthyThreshold = 3,
				StableRuntime = TimeSpan.FromMinutes(2),
				MaxRestarts = 3,
				RestartWindow = TimeSpan.FromMinutes(10),
				BootstrapOutputLines = 100,
				BootstrapOutputBytes = 16 * 1024
			},
			Serilog.Core.Logger.None);

		var installerOptions = PluginInstallerOptions.Default with
		{
			ActivationHealthTimeout = TimeSpan.FromSeconds(2)
		};
		_installer = new PluginInstaller(_paths,
			new PluginArtifactReader(manifestReader, Serilog.Core.Logger.None),
			new PluginArtifactAcquirer(new NoHttpClientFactory(), installerOptions, Serilog.Core.Logger.None),
			new PluginArtifactCache(_paths, installerOptions, Serilog.Core.Logger.None),
			trust,
			new PluginDependencyResolver(_catalog, manifestReader),
			manifestReader,
			_catalog,
			_supervisor,
			new FakeIntegrationRegistrar(),
			sessions,
			takeovers,
			scopeFactory,
			installerOptions,
			TimeProvider.System,
			Serilog.Core.Logger.None);
	}

	[TearDown]
	public async Task TearDown()
	{
		await _supervisor.StopAll(PluginStopReason.HostShutdown);
		_paths.Cleanup();
	}

	[OneTimeTearDown]
	public void OneTimeTearDown()
	{
		_tickLock.Dispose();
	}

	[Test]
	public async Task A_reconcile_tick_inside_the_activation_window_does_not_fail_the_update()
	{
		await InstallVersion("1.0.0");
		var launchesDuringTick = -1;
		_registrations.BeforeRevoke = async () =>
		{
			var before = _launcher.Requests.Count;
			await Tick();
			launchesDuringTick = _launcher.Requests.Count - before;
		};

		var result = await InstallVersion("2.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Error, Is.Null);
			Assert.That(launchesDuringTick, Is.Zero, "the reconcile tick launched the plugin mid-activation");
			Assert.That(ActiveVersion(), Is.EqualTo("2.0.0"));
			Assert.That(Directory.Exists(VersionDirectory("2.0.0")), Is.True, "the new version was deleted");
			Assert.That(LaunchesOf("2.0.0"), Is.EqualTo(1));
			Assert.That(RunningVersion(), Is.EqualTo("2.0.0"));
		});
	}

	[Test]
	public async Task A_reconcile_tick_inside_the_rollback_does_not_relaunch_the_failed_version()
	{
		await InstallVersion("1.0.0");
		_unhealthyVersions.Add("2.0.0");
		var launchesBeforeRollbackTick = -1;
		_trustRecords.BeforeDeleteVersion = async version =>
		{
			if (version != "2.0.0")
			{
				return;
			}

			launchesBeforeRollbackTick = _launcher.Requests.Count;
			await Tick();
		};

		var result = await InstallVersion("2.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.HealthValidationFailed));
			Assert.That(result.RolledBack, Is.True);
			Assert.That(launchesBeforeRollbackTick, Is.GreaterThan(0), "the rollback never reached the tick");
			Assert.That(_launcher.Requests.Skip(launchesBeforeRollbackTick).Select(VersionOf),
				Has.None.EqualTo("2.0.0"),
				"the failed version was relaunched during its own rollback");
			Assert.That(ActiveVersion(), Is.EqualTo("1.0.0"));
			Assert.That(Directory.Exists(VersionDirectory("2.0.0")), Is.False);
			Assert.That(RunningVersion(), Is.EqualTo("1.0.0"));
		});
	}

	[Test]
	public async Task Repeated_updates_under_a_free_running_reconcile_loop_all_succeed()
	{
		await InstallVersion("1.0.0");

		using var loop = new CancellationTokenSource();
		var reconcileLoop = Task.Run(async () =>
		{
			while (!loop.IsCancellationRequested)
			{
				if (CurrentState() != PluginRuntimeState.Running)
				{
					await Tick();
				}

				await Task.Yield();
			}
		});

		var failures = new List<string>();
		for (var minor = 1; minor <= 8; minor++)
		{
			var version = $"1.{minor}.0";
			var result = await InstallVersion(version, false);
			if (!result.Success || ActiveVersion() != version || !Directory.Exists(VersionDirectory(version)))
			{
				failures.Add($"{version}: {result.Error} {result.ErrorMessage}");
			}
		}

		await loop.CancelAsync();
		await reconcileLoop;

		Assert.That(failures, Is.Empty);
	}

	[Test]
	public async Task A_genuinely_unhealthy_update_still_rolls_back_to_the_running_previous_version()
	{
		await InstallVersion("1.0.0");
		_unhealthyVersions.Add("2.0.0");

		var result = await InstallVersion("2.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.HealthValidationFailed));
			Assert.That(result.RolledBack, Is.True);
			Assert.That(ActiveVersion(), Is.EqualTo("1.0.0"));
			Assert.That(Directory.Exists(VersionDirectory("2.0.0")), Is.False);
			Assert.That(RunningVersion(), Is.EqualTo("1.0.0"));
		});
	}

	[Test]
	public async Task Cancelling_during_activation_leaves_the_plugin_launchable()
	{
		await InstallVersion("1.0.0");
		using var cancellation = new CancellationTokenSource();
		_registrations.BeforeRevoke = () => cancellation.CancelAsync();

		var result = await _installer.Install(PluginArtifactSource.FromPath(BuildArtifact("2.0.0")),
			new PluginInstallRequest(),
			cancellation.Token);
		_registrations.BeforeRevoke = null;

		var launchesBefore = _launcher.Requests.Count;
		await Tick();

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(_launcher.Requests,
				Has.Count.EqualTo(launchesBefore + 1),
				"the reconcile loop could no longer launch the plugin after a cancelled activation");
		});
	}

	[Test]
	public async Task Stop_and_host_shutdown_are_not_blocked_by_an_activation_in_progress()
	{
		await InstallVersion("1.0.0");
		Task<PluginSupervisorResult>? stop = null;
		Task? stopAll = null;
		_registrations.BeforeRevoke = async () =>
		{
			stop = _supervisor.Stop(PluginId, PluginStopReason.UserRequested);
			stopAll = _supervisor.StopAll(PluginStopReason.HostShutdown);
			await Task.WhenAny(Task.WhenAll(stop, stopAll), Task.Delay(TimeSpan.FromSeconds(5)));
		};

		await InstallVersion("2.0.0", false);

		Assert.Multiple(() =>
		{
			Assert.That(stop, Is.Not.Null);
			Assert.That(stop!.IsCompleted, Is.True, "Stop waited for the activation");
			Assert.That(stopAll!.IsCompleted, Is.True, "host shutdown waited for the activation");
		});
	}

	[Test]
	public async Task A_user_start_during_a_hold_waits_for_it_and_only_one_process_is_launched()
	{
		await InstallVersion("1.0.0");
		await using var hold = await _supervisor.HoldLaunches(PluginId);
		await _supervisor.Stop(PluginId, PluginStopReason.Update);
		var launchesBefore = _launcher.Requests.Count;

		var userStart = _supervisor.Start(PluginId);
		await Tick();
		var waitedForHold = !userStart.IsCompleted;
		var launchesWhileHeld = _launcher.Requests.Count - launchesBefore;

		var holderStart = await hold.Start();
		await hold.DisposeAsync();
		var userResult = await userStart;

		Assert.Multiple(() =>
		{
			Assert.That(waitedForHold, Is.True, "the user start did not wait for the hold");
			Assert.That(launchesWhileHeld, Is.Zero);
			Assert.That(holderStart.Success, Is.True, holderStart.Message);
			Assert.That(userResult.Error, Is.EqualTo(PluginSupervisorError.AlreadyRunning));
			Assert.That(_launcher.Requests, Has.Count.EqualTo(launchesBefore + 1));
		});
	}

	[Test]
	public async Task A_user_restart_during_a_hold_waits_for_it_and_never_launches_inside_it()
	{
		await InstallVersion("1.0.0");
		var hold = await _supervisor.HoldLaunches(PluginId);
		await _supervisor.Stop(PluginId, PluginStopReason.Update);
		var launchesBefore = _launcher.Requests.Count;

		var restart = _supervisor.Restart(PluginId);
		var waitedForHold = !restart.IsCompleted;
		var holderStart = await hold.Start();
		var launchesWhileHeld = _launcher.Requests.Count - launchesBefore;
		await hold.DisposeAsync();
		var restartResult = await restart;

		Assert.Multiple(() =>
		{
			Assert.That(waitedForHold, Is.True, "the restart did not wait for the hold");
			Assert.That(holderStart.Success, Is.True, holderStart.Message);
			Assert.That(launchesWhileHeld, Is.EqualTo(1));
			Assert.That(restartResult.Success, Is.True, restartResult.Message);
			Assert.That(_launcher.StartedProcesses.Count(process => !process.HasExited), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task An_uninstall_during_a_restart_leaves_no_process_behind()
	{
		await InstallVersion("1.0.0");
		var launchesBefore = _launcher.Requests.Count;
		using var entered = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		_trust.BeforeEvaluate = _ =>
		{
			_trust.BeforeEvaluate = null;
			entered.Set();
			release.Wait(TimeSpan.FromSeconds(10));
		};

		var restart = Task.Run(() => _supervisor.Restart(PluginId));
		Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True, "precondition: the restart reached trust");
		var uninstall = _installer.Uninstall(PluginId, new PluginUninstallRequest());
		for (var i = 0; i < 500 && LastStopReason() != PluginStopReason.UserRequested; i++)
		{
			await Task.Delay(TimeSpan.FromMilliseconds(10));
		}

		release.Set();
		var restartResult = await restart;
		var uninstalled = await uninstall;

		Assert.Multiple(() =>
		{
			Assert.That(uninstalled.Success, Is.True, uninstalled.ErrorMessage);
			Assert.That(restartResult.Success, Is.False, "the restart was overtaken by the uninstall");
			Assert.That(_launcher.Requests, Has.Count.EqualTo(launchesBefore), "nothing launched after the stop");
			Assert.That(_launcher.StartedProcesses.Count(process => !process.HasExited), Is.Zero);
		});
	}

	[Test]
	public async Task A_cancelled_wait_for_a_hold_does_not_take_it()
	{
		await InstallVersion("1.0.0");
		var first = await _supervisor.HoldLaunches(PluginId);
		using var cancellation = new CancellationTokenSource();
		var second = _supervisor.HoldLaunches(PluginId, cancellation.Token);
		await cancellation.CancelAsync();

		Assert.That(async () => await second, Throws.InstanceOf<OperationCanceledException>());

		await first.DisposeAsync();
		await using var third = await _supervisor.HoldLaunches(PluginId);
		Assert.That(third, Is.Not.Null);
	}

	[Test]
	public async Task A_backoff_relaunch_from_a_tick_that_read_the_catalog_before_an_activation_starts_the_new_version()
	{
		const string otherPluginId = "com.suchbyte.other-plugin";
		await InstallVersion("1.0.0", true, otherPluginId);
		await InstallVersion("1.0.0");
		await InstallVersion("2.0.0");
		_currentVersionTemplate = await File.ReadAllTextAsync(CurrentFilePath());

		await _supervisor.Stop(otherPluginId, PluginStopReason.Update);
		WriteCurrentVersion("1.0.0");
		_unhealthyVersions.Add("1.0.0");
		await _supervisor.Restart(PluginId);
		LaunchesOfPlugin(PluginId)
			.Select(request => _launcher.StartedProcesses[_launcher.Requests.IndexOf(request)])
			.Last()
			.CompleteExit(1);
		await WaitForState(PluginRuntimeState.Backoff);

		_trust.BeforeEvaluate = directory =>
		{
			if (directory.Contains(otherPluginId, StringComparison.Ordinal))
			{
				WriteCurrentVersion("2.0.0");
				_trust.BeforeEvaluate = null;
			}
		};
		var launchesBefore = LaunchesOfPlugin(PluginId).Count;

		await Tick();

		var relaunches = LaunchesOfPlugin(PluginId).Skip(launchesBefore).Select(VersionOf).ToList();
		Assert.That(relaunches, Is.EqualTo(new[] { "2.0.0" }));
	}

	private async Task<PluginInstallResult> InstallVersion(
		string version,
		bool pumpUntilRunning = true,
		string pluginId = PluginId)
	{
		var install = _installer.Install(PluginArtifactSource.FromPath(BuildArtifact(version, pluginId)),
			new PluginInstallRequest());

		while (!install.IsCompleted)
		{
			if (pumpUntilRunning && CurrentState(pluginId) == PluginRuntimeState.Starting)
			{
				await Tick();
			}

			await Task.WhenAny(install, Task.Delay(TimeSpan.FromMilliseconds(10)));
		}

		var result = await install;
		if (pumpUntilRunning)
		{
			for (var i = 0; i < 200 && CurrentState(pluginId) == PluginRuntimeState.Starting; i++)
			{
				await Tick();
				await Task.Delay(TimeSpan.FromMilliseconds(5));
			}
		}

		return result;
	}

	private async Task Tick()
	{
		await _tickLock.WaitAsync();
		try
		{
			await _supervisor.Reconcile();
		}
		finally
		{
			_tickLock.Release();
		}
	}

	private PluginRuntimeState? CurrentState(string pluginId = PluginId)
	{
		return _supervisor.Snapshot().FirstOrDefault(s => s.PluginId == pluginId)?.State;
	}

	private PluginStopReason? LastStopReason()
	{
		return _supervisor.Snapshot().FirstOrDefault(s => s.PluginId == PluginId)?.LastStopReason;
	}

	private async Task WaitForState(PluginRuntimeState state)
	{
		for (var i = 0; i < 500 && CurrentState() != state; i++)
		{
			await Task.Delay(TimeSpan.FromMilliseconds(10));
		}

		Assert.That(CurrentState(), Is.EqualTo(state));
	}

	private string? RunningVersion()
	{
		var snapshot = _supervisor.Snapshot().FirstOrDefault(s => s.PluginId == PluginId);
		return snapshot?.State is PluginRuntimeState.Starting or PluginRuntimeState.Running
			? VersionOf(LaunchesOfPlugin(PluginId)[^1])
			: null;
	}

	private bool IsUnhealthy(int port)
	{
		var request = _launcher.Requests.LastOrDefault(r =>
			r.Environment["ASPNETCORE_URLS"] == $"http://127.0.0.1:{port}");
		return request is not null && _unhealthyVersions.Contains(VersionOf(request));
	}

	private int LaunchesOf(string version)
	{
		return LaunchesOfPlugin(PluginId).Count(request => VersionOf(request) == version);
	}

	private List<PluginProcessStartRequest> LaunchesOfPlugin(string pluginId)
	{
		return _launcher.Requests.Where(request => request.Environment["MACRO_DECK_PLUGIN_ID"] == pluginId).ToList();
	}

	private static string VersionOf(PluginProcessStartRequest request)
	{
		return Path.GetFileName(request.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar));
	}

	private string BuildArtifact(string version, string pluginId = PluginId)
	{
		return new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build(version, pluginId, null))
			.WithFile(ManifestJson.EntrypointExecutable, "binary " + version)
			.WriteTo(_sourceDirectory, $"plugin-{version}-{Guid.NewGuid():N}.macroDeckPlugin");
	}

	private string VersionDirectory(string version)
	{
		return PluginInstallPaths.VersionDirectory(_paths.PluginsDirectory, PluginId, version);
	}

	private string? ActiveVersion()
	{
		_catalog.Invalidate();
		return _catalog.Discover().FirstOrDefault(p => p.PluginId == PluginId)?.ActiveVersion?.Version;
	}

	private void WriteCurrentVersion(string version)
	{
		File.WriteAllText(CurrentFilePath(),
			_currentVersionTemplate.Replace("2.0.0", version, StringComparison.Ordinal));
		_catalog.Invalidate();
	}

	private string CurrentFilePath()
	{
		return PluginInstallPaths.CurrentFilePath(_paths.PluginsDirectory, PluginId);
	}

	private sealed class NoHttpClientFactory : IHttpClientFactory
	{
		public HttpClient CreateClient(string name)
		{
			throw new NotSupportedException();
		}
	}

	private sealed class RevokingRegistrationService : IPluginRegistrationService
	{
		private readonly IPluginRegistrationRepository _registrations;

		public RevokingRegistrationService(IPluginRegistrationRepository registrations)
		{
			_registrations = registrations;
		}

		public Task<PluginRegistrationResult> Register(
			string pluginId,
			string displayName,
			Guid? accessTokenId,
			string origin,
			bool allowInstalledId = false)
		{
			throw new NotSupportedException();
		}

		public Task<PluginRegistrationResult> ReplaceSecret(
			string pluginId,
			string displayName,
			Guid? accessTokenId,
			string origin)
		{
			throw new NotSupportedException();
		}

		public Task<PluginRegistrationEntity?> Authenticate(string pluginId, string secret)
		{
			throw new NotSupportedException();
		}

		public Task Revoke(string pluginId)
		{
			return _registrations.Revoke(pluginId, DateTime.UtcNow);
		}
	}

	private sealed class HookedTrustEvaluator : IPluginTrustEvaluator
	{
		private readonly FakePluginTrustEvaluator _inner = new();

		public Action<string>? BeforeEvaluate { get; set; }

		public Task<PluginTrustResult> EvaluateInstalledAsync(
			string versionDirectory,
			PluginRevocationCheck revocationCheck,
			CancellationToken cancellationToken = default)
		{
			BeforeEvaluate?.Invoke(versionDirectory);
			return _inner.EvaluateInstalledAsync(versionDirectory, revocationCheck, cancellationToken);
		}
	}

	private sealed class HookedRegistrationRepository : IPluginRegistrationRepository
	{
		private readonly InMemoryPluginRegistrationRepository _inner = new();

		public Func<Task>? BeforeRevoke { get; set; }

		public Task<PluginRegistrationEntity?> GetByPluginId(string pluginId)
		{
			return _inner.GetByPluginId(pluginId);
		}

		public Task<IReadOnlyList<PluginRegistrationEntity>> GetByAccessTokenId(Guid accessTokenId)
		{
			return _inner.GetByAccessTokenId(accessTokenId);
		}

		public Task<IReadOnlyList<PluginRegistrationEntity>> GetAll()
		{
			return _inner.GetAll();
		}

		public Task<IReadOnlyList<PluginRegistrationEntity>> GetNonRevoked()
		{
			return _inner.GetNonRevoked();
		}

		public Task Create(PluginRegistrationEntity registration)
		{
			return _inner.Create(registration);
		}

		public Task<bool> Reactivate(
			string pluginId,
			string displayName,
			string secretHash,
			Guid? accessTokenId,
			string origin,
			DateTime at)
		{
			return _inner.Reactivate(pluginId, displayName, secretHash, accessTokenId, origin, at);
		}

		public Task<bool> RotateSecret(
			string pluginId,
			string displayName,
			string secretHash,
			Guid? accessTokenId,
			string origin)
		{
			return _inner.RotateSecret(pluginId, displayName, secretHash, accessTokenId, origin);
		}

		public Task TouchLastSeen(string pluginId, DateTime at)
		{
			return _inner.TouchLastSeen(pluginId, at);
		}

		public async Task Revoke(string pluginId, DateTime at)
		{
			if (BeforeRevoke is { } hook)
			{
				await hook();
			}

			await _inner.Revoke(pluginId, at);
		}

		public Task<IReadOnlyList<string>> RevokeByAccessTokenId(Guid accessTokenId, DateTime at)
		{
			return _inner.RevokeByAccessTokenId(accessTokenId, at);
		}

		public Task<IReadOnlyList<string>> DeleteByAccessTokenId(Guid accessTokenId)
		{
			return _inner.DeleteByAccessTokenId(accessTokenId);
		}
	}

	private sealed class HookedTrustRecordRepository : IPluginTrustRecordRepository
	{
		private readonly InMemoryPluginTrustRecordRepository _inner = new();

		public Func<string, Task>? BeforeDeleteVersion { get; set; }

		public Task<PluginTrustRecordEntity?> GetVersion(string pluginId, string version)
		{
			return _inner.GetVersion(pluginId, version);
		}

		public Task<IReadOnlyList<PluginTrustRecordEntity>> GetForPlugin(string pluginId)
		{
			return _inner.GetForPlugin(pluginId);
		}

		public Task Upsert(
			string pluginId,
			string version,
			string admittedVerdict,
			string? certificateId,
			PluginSignerIdentity? signer,
			DateTime installedAt)
		{
			return _inner.Upsert(pluginId, version, admittedVerdict, certificateId, signer, installedAt);
		}

		public Task DeleteForPlugin(string pluginId)
		{
			return _inner.DeleteForPlugin(pluginId);
		}

		public async Task DeleteVersion(string pluginId, string version)
		{
			if (BeforeDeleteVersion is { } hook)
			{
				await hook(version);
			}

			await _inner.DeleteVersion(pluginId, version);
		}
	}
}
