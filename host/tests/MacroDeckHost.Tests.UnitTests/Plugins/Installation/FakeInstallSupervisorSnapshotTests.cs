using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Plugins.Trust;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Installation;

// A caller that reads Snapshot() decides what to do from what it finds there, so a fake whose snapshot
// is composed differently from the real supervisor's makes such a caller untestable: PR #694's uninstall
// residue check passed against a Start()-populated dictionary while being inert in production for every
// installed plugin and live for exactly the developer sessions it was meant to exclude.
[TestFixture]
internal sealed class FakeInstallSupervisorSnapshotTests
{
	private const string InstalledId = "com.example.installed";
	private const string DeveloperId = "com.example.developer";

	private ManualTimeProvider _time = null!;
	private FakePluginInstallationCatalog _catalog = null!;
	private PluginSessionRegistry _sessions = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_catalog = new FakePluginInstallationCatalog();
		_sessions = new PluginSessionRegistry(_time, Serilog.Core.Logger.None);
	}

	[Test]
	public async Task An_installed_plugin_is_managed_and_only_an_uninstalled_developer_session_is_unmanaged()
	{
		_catalog.Plugins.Add(Installed(InstalledId));
		await SelfRegisteredSession(DeveloperId, connected: true);
		await SelfRegisteredSession(InstalledId, connected: true);
		var supervisor = new FakeInstallSupervisor(_catalog, _sessions);

		var beforeStart = supervisor.Snapshot();

		Assert.Multiple(() =>
		{
			Assert.That(beforeStart.Select(Row),
				Is.EquivalentTo(new[]
				{
					(InstalledId, PluginRuntimeState.Stopped, PluginHealthState.Unknown, true),
					(DeveloperId, PluginRuntimeState.Running, PluginHealthState.Healthy, false)
				}));
			Assert.That(supervisor.IsRunning(InstalledId), Is.False);
		});

		await supervisor.Start(InstalledId);

		Assert.That(supervisor.Snapshot().Single(entry => entry.PluginId == InstalledId).State,
			Is.EqualTo(PluginRuntimeState.Running));
	}

	[Test]
	public async Task An_uninstalled_plugin_directory_and_a_managed_session_are_reported_by_neither_supervisor()
	{
		_catalog.Plugins.Add(Installed(InstalledId));
		_catalog.Plugins.Add(new InstalledPlugin
		{
			PluginId = "com.example.no-versions-left",
			PluginDirectory = Path.Combine(Path.GetTempPath(), "md-plugins-fake", "com.example.no-versions-left"),
			Versions = [],
			ActiveVersion = null
		});
		await SelfRegisteredSession(DeveloperId, connected: false);
		await ManagedSession("com.example.managed-only");

		var fake = new FakeInstallSupervisor(_catalog, _sessions);
		var real = CreateRealSupervisor();

		Assert.That(fake.Snapshot().Select(Row), Is.EquivalentTo(real.Snapshot().Select(Row)));
	}

	private static (string, PluginRuntimeState, PluginHealthState, bool) Row(PluginRuntimeSnapshot snapshot)
		=> (snapshot.PluginId, snapshot.State, snapshot.Health, snapshot.Managed);

	private static InstalledPlugin Installed(string pluginId)
	{
		var directory = Path.Combine(Path.GetTempPath(), "md-plugins-fake", pluginId);
		var version = new InstalledPluginVersion
		{
			Version = "1.0.0",
			VersionDirectory = Path.Combine(directory, "versions", "1.0.0"),
			ManifestPath = Path.Combine(directory, "versions", "1.0.0", "manifest.json")
		};

		return new InstalledPlugin
		{
			PluginId = pluginId,
			PluginDirectory = directory,
			Versions = [version],
			ActiveVersion = version
		};
	}

	private Task SelfRegisteredSession(string pluginId, bool connected)
		=> CreateSession(pluginId, PluginSessionOrigin.SelfRegistered, connected);

	private Task ManagedSession(string pluginId)
		=> CreateSession(pluginId, PluginSessionOrigin.Managed, connected: true);

	private async Task CreateSession(string pluginId, PluginSessionOrigin origin, bool connected)
	{
		var sessionId = $"session-{pluginId}";
		await _sessions.Create(new PluginSessionRecord
		{
			SessionId = sessionId,
			PluginId = pluginId,
			DisplayName = pluginId,
			Origin = origin,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, CapabilityNegotiationResult>(),
			DeclaredCapabilities = [],
			CreatedAt = _time.GetUtcNow()
		});

		if (connected)
		{
			_sessions.TryAttach(sessionId, new FakePluginConnection(sessionId), instanceId: null);
		}
	}

	private PluginSupervisor CreateRealSupervisor()
	{
		var services = new ServiceCollection()
			.AddSingleton<Mediator.IMediator>(new RecordingMediator())
			.AddSingleton<IPluginTrustRecordRepository, InMemoryPluginTrustRecordRepository>()
			.AddSingleton<IPluginTrustBaseline, FakePluginTrustBaseline>()
			.BuildServiceProvider();

		var listenerState = new HostListenerState(PublicEndpointSet.HttpOnly(50000), false);
		listenerState.SetLoopbackPort(50001);

		return new PluginSupervisor(new FakePluginManifestReader(),
			_catalog,
			new FakePluginRuntimeStateStore(),
			new FakePluginProcessJournal(),
			new FakePluginProcessLauncher(),
			new FakeDotnetMuxerLocator(),
			new FakePluginHealthProbe(),
			_sessions,
			new PluginLaunchTokenService(_time, _sessions),
			listenerState,
			new FakePluginTrustEvaluator(),
			services.GetRequiredService<IServiceScopeFactory>(),
			_time,
			new PluginSupervisorOptions
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
			},
			Serilog.Core.Logger.None);
	}
}
