using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Capabilities.Localization;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Assets;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.FolderViews;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Layouts;
using MacroDeck.Sdk.Widgets;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class RemotePluginIntegrationRegistrarTests
{
	private ManualTimeProvider _time = null!;
	private PluginSessionRegistry _sessionRegistry = null!;
	private RecordingIntegrationRegistry _integrationRegistry = null!;
	private InMemorySnapshotStore _snapshotStore = null!;
	private InMemoryPluginAssetCache _assetCache = null!;
	private PluginAssetReceiver _assetReceiver = null!;
	private ScriptedInvoker _invoker = null!;
	private RecordingNotificationStore _notifications = null!;
	private IServiceScopeFactory _scopeFactory = null!;
	private EmptyInstallationCatalog _installationCatalog = null!;
	private FakePluginManifestReader _manifestReader = null!;
	private LocalizationCatalogRegistry _localizationCatalogs = null!;
	private FakeLocalizationPreferenceService _preferences = null!;
	private RecordingMediator _mediator = null!;

	private FakePluginDeviceRegistry _deviceRegistry = null!;
	private LayoutRegistry _layoutRegistry = null!;
	private FolderViewRegistry _folderViewRegistry = null!;
	private WidgetTypeRegistry _widgetTypeRegistry = null!;
	private ScreenSaverRegistry _screenSaverRegistry = null!;
	private RemotePluginIntegrationRegistrar _registrar = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_sessionRegistry = new PluginSessionRegistry(_time, Serilog.Core.Logger.None);
		_integrationRegistry = new RecordingIntegrationRegistry();
		_snapshotStore = new InMemorySnapshotStore();
		_assetCache = new InMemoryPluginAssetCache();
		_assetReceiver = new PluginAssetReceiver(_assetCache);
		_invoker = new ScriptedInvoker();
		_notifications = new RecordingNotificationStore();
		_installationCatalog = new EmptyInstallationCatalog();
		_manifestReader = new FakePluginManifestReader();
		_localizationCatalogs = new LocalizationCatalogRegistry();
		_preferences = new FakeLocalizationPreferenceService();
		_mediator = new RecordingMediator();
		_deviceRegistry = new FakePluginDeviceRegistry();
		_layoutRegistry = new LayoutRegistry(new RecordingMediator());
		_folderViewRegistry = new FolderViewRegistry(new RecordingMediator());
		_widgetTypeRegistry = new WidgetTypeRegistry(new RecordingMediator());
		_screenSaverRegistry = new ScreenSaverRegistry(new RecordingMediator());

		var services = new ServiceCollection();
		services.AddSingleton<IMediator>(_mediator);
		services.AddSingleton<IAppPreferenceService>(_preferences);
		_scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		_registrar = new RemotePluginIntegrationRegistrar(_sessionRegistry,
			_integrationRegistry,
			_snapshotStore,
			new RemotePluginSnapshotRefresher(_invoker, _snapshotStore),
			_invoker,
			new AlwaysDisconnected(),
			_assetCache,
			_assetReceiver,
			_installationCatalog,
			_manifestReader,
			_notifications,
			_scopeFactory,
			_localizationCatalogs,
			_deviceRegistry,
			_layoutRegistry,
			_folderViewRegistry,
			_widgetTypeRegistry,
			_screenSaverRegistry,
			_time,
			Serilog.Log.Logger);
	}

	[TearDown]
	public void TearDown() => _registrar.Dispose();

	private async Task<string> ConnectSessionAsync(
		string pluginId,
		IReadOnlyList<DeclaredCapability> declared,
		IReadOnlyDictionary<string, CapabilityNegotiationResult> negotiated,
		string? declaredVersion = null,
		string displayName = "Example Plugin")
	{
		var record = new PluginSessionRecord
		{
			SessionId = Guid.CreateVersion7().ToString("D"),
			PluginId = pluginId,
			DisplayName = displayName,
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 1,
			DeclaredVersion = declaredVersion,
			Capabilities = negotiated,
			DeclaredCapabilities = declared,
			State = PluginSessionState.Awaiting,
			CreatedAt = _time.GetUtcNow()
		};

		await _sessionRegistry.Create(record);
		_sessionRegistry.TryAttach(record.SessionId, new FakePluginConnection(), null);
		return record.SessionId;
	}

	private static InstalledPlugin Installed(string pluginId)
	{
		var version = new InstalledPluginVersion
		{
			Version = "1.0.0",
			VersionDirectory = "/plugins/" + pluginId + "/versions/1.0.0",
			ManifestPath = "/plugins/" + pluginId + "/versions/1.0.0/manifest.json"
		};

		return new InstalledPlugin
		{
			PluginId = pluginId,
			PluginDirectory = "/plugins/" + pluginId,
			Versions = [version],
			ActiveVersion = version
		};
	}

	private static DeclaredCapability Action(string localId)
		=> new() { Kind = CapabilityKinds.Actions, LocalId = localId, VersionRange = Version() };

	private static DeclaredCapability Provider(string kind, string localId = "provider")
		=> new() { Kind = kind, LocalId = localId, VersionRange = Version() };

	private static CapabilityVersionRange Version() => new() { Minimum = 1, Maximum = 1 };

	private static Dictionary<string, CapabilityNegotiationResult> Accepted(params string[] kinds)
		=> kinds.ToDictionary(kind => kind,
			kind => CapabilityNegotiationResult.Accept(kind, 1),
			StringComparer.Ordinal);

	private List<string> CatalogChanges()
		=> [.. _mediator.Published.OfType<IntegrationCatalogChangedNotification>().Select(n => n.IntegrationId)];

	[Test]
	public async Task Registering_an_installed_plugin_detached_announces_the_catalogue_change()
	{
		var pluginId = "com.example.plugin";
		_installationCatalog.Plugins.Add(Installed(pluginId));

		await _registrar.RegisterInstalledDetachedAsync(pluginId);

		Assert.Multiple(() =>
		{
			Assert.That(_integrationRegistry.Registered.Select(i => i.Id), Does.Contain(pluginId));
			Assert.That(CatalogChanges(), Is.EqualTo(new[] { pluginId }));
			Assert.That(_mediator.Published.OfType<IntegrationStateChangedNotification>(), Is.Empty);
		});
	}

	[Test]
	public async Task Re_declaring_capabilities_never_announces_a_removal()
	{
		var pluginId = "com.example.plugin";
		_invoker.ActionsDescribeResult = new ActionCatalogPayload { Actions = [] };
		await ConnectSessionAsync(pluginId, [Action("play")], Accepted(CapabilityKinds.Actions));
		await _registrar.RegisterAsync(pluginId);

		await _registrar.UnregisterAsync(pluginId);
		await _registrar.RegisterAsync(pluginId);

		Assert.Multiple(() =>
		{
			Assert.That(CatalogChanges(), Is.Empty);
			Assert.That(_integrationRegistry.Registered.Select(i => i.Id), Does.Contain(pluginId));
		});
	}

	[Test]
	public async Task Forgetting_a_plugin_announces_its_removal()
	{
		var pluginId = "com.example.plugin";
		_installationCatalog.Plugins.Add(Installed(pluginId));
		await _registrar.RegisterInstalledDetachedAsync(pluginId);
		_mediator.Published.Clear();

		await _registrar.ForgetAsync(pluginId);

		Assert.Multiple(() =>
		{
			Assert.That(_integrationRegistry.Registered.Select(i => i.Id), Does.Not.Contain(pluginId));
			Assert.That(CatalogChanges(), Is.EqualTo(new[] { pluginId }));
		});
	}

	[Test]
	public async Task Sweeping_a_vanished_installation_announces_its_removal()
	{
		var vanished = "com.example.vanished";
		_installationCatalog.Plugins.Add(Installed(vanished));
		await _registrar.RegisterInstalledButStoppedAsync();
		_installationCatalog.Plugins.RemoveAll(plugin => plugin.PluginId == vanished);
		_mediator.Published.Clear();

		await _registrar.UnregisterVanishedInstallationsAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_integrationRegistry.Registered.Select(i => i.Id), Does.Not.Contain(vanished));
			Assert.That(CatalogChanges(), Is.EqualTo(new[] { vanished }));
		});
	}

	private static async Task<bool> WithinASecond(Func<bool> condition)
	{
		for (var attempt = 0; attempt < 100 && !condition(); attempt++)
		{
			await Task.Delay(10);
		}

		return condition();
	}

	private async Task<string> RegisterRunningPluginAsync(string pluginId, string version)
	{
		_invoker.ActionsDescribeResult = new ActionCatalogPayload { Actions = [] };
		var sessionId = await ConnectSessionAsync(pluginId,
			[Action("play")],
			Accepted(CapabilityKinds.Actions),
			declaredVersion: version);
		await _registrar.RegisterAsync(pluginId);
		return sessionId;
	}

	[Test]
	public async Task An_installed_plugin_whose_process_stops_stays_listed_as_stopped()
	{
		var pluginId = "com.example.plugin";
		_installationCatalog.Plugins.Add(Installed(pluginId));
		await RegisterRunningPluginAsync(pluginId, "1.0.0");
		var running = _integrationRegistry.Registered.Single(i => i.Id == pluginId);
		_mediator.Published.Clear();

		await _sessionRegistry.TerminateForPlugin(pluginId, 1000, "stopped");

		Assert.That(await WithinASecond(() => CatalogChanges().Count > 0), Is.True);
		Assert.Multiple(() =>
		{
			var listed = _integrationRegistry.Registered.SingleOrDefault(i => i.Id == pluginId);
			Assert.That(listed, Is.Not.Null);
			Assert.That(listed, Is.Not.SameAs(running));
			Assert.That(CatalogChanges(), Is.EqualTo(new[] { pluginId }));
			Assert.That(_mediator.Published.OfType<IntegrationStateChangedNotification>(), Is.Empty);
		});
	}

	[Test]
	public async Task A_developer_build_whose_session_ends_is_removed_and_announced()
	{
		var pluginId = "com.example.dev-build";
		await RegisterRunningPluginAsync(pluginId, "0.1.0");
		_mediator.Published.Clear();

		await _sessionRegistry.TerminateForPlugin(pluginId, 1000, "stopped");

		Assert.That(await WithinASecond(() => CatalogChanges().Count > 0), Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(_integrationRegistry.Registered.Select(i => i.Id), Does.Not.Contain(pluginId));
			Assert.That(CatalogChanges(), Is.EqualTo(new[] { pluginId }));
		});
	}

	[Test]
	public async Task Upgrading_a_running_plugin_keeps_it_listed_and_ends_on_the_new_adapter()
	{
		var pluginId = "com.example.plugin";
		_installationCatalog.Plugins.Add(Installed(pluginId));
		await RegisterRunningPluginAsync(pluginId, "1.0.0");
		var oldAdapter = _integrationRegistry.Registered.Single(i => i.Id == pluginId);

		await _sessionRegistry.TerminateForPlugin(pluginId, 1000, "upgrade");
		Assert.That(await WithinASecond(() => CatalogChanges().Count > 0), Is.True);
		var listedWhileRestarting = _integrationRegistry.Registered.Select(i => i.Id).ToList();

		await RegisterRunningPluginAsync(pluginId, "1.0.1");

		var lastSignal = _mediator.Published.Last(notification =>
			notification is IntegrationStateChangedNotification or IntegrationCatalogChangedNotification);
		Assert.Multiple(() =>
		{
			Assert.That(listedWhileRestarting, Does.Contain(pluginId));
			Assert.That(lastSignal, Is.EqualTo(new IntegrationStateChangedNotification(pluginId)));
			Assert.That(_integrationRegistry.Registered.Single(i => i.Id == pluginId), Is.Not.SameAs(oldAdapter));
		});
	}

	[Test]
	public async Task A_valid_session_registers_an_adapter_and_publishes_state_changed()
	{
		var pluginId = "com.example.plugin";
		_invoker.ActionsDescribeResult = new ActionCatalogPayload { Actions = [] };

		await ConnectSessionAsync(pluginId, [Action("play")], Accepted(CapabilityKinds.Actions));

		var registered = await _registrar.RegisterAsync(pluginId);

		Assert.Multiple(() =>
		{
			Assert.That(registered, Is.True);
			Assert.That(_integrationRegistry.Registered.Select(i => i.Id), Does.Contain(pluginId));
		});
	}

	[Test]
	public async Task Duplicate_declared_action_ids_are_rejected()
	{
		var pluginId = "com.example.plugin";

		await ConnectSessionAsync(pluginId, [Action("play"), Action("play")], Accepted(CapabilityKinds.Actions));

		var registered = await _registrar.RegisterAsync(pluginId);

		Assert.Multiple(() =>
		{
			Assert.That(registered, Is.False);
			Assert.That(_integrationRegistry.Registered, Is.Empty);
			Assert.That(_notifications.Raised, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Two_provider_ids_for_a_provider_shaped_kind_are_rejected()
	{
		var pluginId = "com.example.plugin";

		await ConnectSessionAsync(pluginId,
			[Provider(CapabilityKinds.MusicPlayer, "a"), Provider(CapabilityKinds.MusicPlayer, "b")],
			Accepted(CapabilityKinds.MusicPlayer));

		var registered = await _registrar.RegisterAsync(pluginId);

		Assert.That(registered, Is.False);
	}

	[Test]
	public async Task A_single_provider_id_for_a_provider_shaped_kind_is_accepted()
	{
		var pluginId = "com.example.plugin";

		await ConnectSessionAsync(pluginId,
			[Provider(CapabilityKinds.MusicPlayer)],
			Accepted(CapabilityKinds.MusicPlayer));

		var registered = await _registrar.RegisterAsync(pluginId);

		Assert.That(registered, Is.True);
	}

	[Test]
	public async Task A_describe_failure_with_no_persisted_snapshot_fails_registration()
	{
		var pluginId = "com.example.plugin";
		_invoker.ThrowOnActionsDescribe = true;

		await ConnectSessionAsync(pluginId, [Action("play")], Accepted(CapabilityKinds.Actions));

		var registered = await _registrar.RegisterAsync(pluginId);

		Assert.Multiple(() =>
		{
			Assert.That(registered, Is.False);
			Assert.That(_integrationRegistry.Registered, Is.Empty);
		});
	}

	[Test]
	public async Task A_describe_failure_with_a_persisted_snapshot_falls_back_to_it()
	{
		var pluginId = "com.example.plugin";

		await _snapshotStore.SaveAsync(RemotePluginCapabilitySnapshot.Empty(pluginId) with
		{
			Actions = [new("play", "Play", string.Empty, [], null, false, false)]
		});

		_invoker.ThrowOnActionsDescribe = true;

		await ConnectSessionAsync(pluginId, [Action("play")], Accepted(CapabilityKinds.Actions));

		var registered = await _registrar.RegisterAsync(pluginId);

		Assert.Multiple(() =>
		{
			Assert.That(registered, Is.True);
			var adapter = _integrationRegistry.Registered.Single(i => i.Id == pluginId);
			Assert.That(adapter.Actions.Select(a => a.Id), Does.Contain("play"));
		});
	}

	[Test]
	public async Task Reconnecting_inside_the_resume_window_re_registers_successfully()
	{
		var pluginId = "com.example.plugin";
		_invoker.ActionsDescribeResult = new ActionCatalogPayload { Actions = [] };

		var sessionId = await ConnectSessionAsync(pluginId, [Action("play")], Accepted(CapabilityKinds.Actions));
		var firstRegistered = await _registrar.RegisterAsync(pluginId);

		_sessionRegistry.Detach(sessionId, _time.GetUtcNow());
		_sessionRegistry.TryAttach(sessionId, new FakePluginConnection(), null);

		var secondRegistered = await _registrar.RegisterAsync(pluginId);

		Assert.Multiple(() =>
		{
			Assert.That(firstRegistered, Is.True);
			Assert.That(secondRegistered, Is.True);
			Assert.That(_integrationRegistry.Registered.Count(i => i.Id == pluginId), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task An_installed_but_stopped_plugin_that_then_connects_registers_successfully()
	{
		var pluginId = "com.example.plugin";
		_invoker.ActionsDescribeResult = new ActionCatalogPayload { Actions = [] };

		_installationCatalog.Plugins.Add(Installed(pluginId));

		await _registrar.RegisterInstalledButStoppedAsync();
		Assert.That(_integrationRegistry.Registered.Select(i => i.Id), Does.Contain(pluginId));

		await ConnectSessionAsync(pluginId, [Action("play")], Accepted(CapabilityKinds.Actions));
		var registered = await _registrar.RegisterAsync(pluginId);

		Assert.Multiple(() =>
		{
			Assert.That(registered, Is.True);
			Assert.That(_integrationRegistry.Registered.Count(i => i.Id == pluginId), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Registering_one_installed_plugin_detached_ignores_an_id_that_is_not_installed()
	{
		var pluginId = "com.example.plugin";
		_installationCatalog.Plugins.Add(Installed(pluginId));

		await _registrar.RegisterInstalledDetachedAsync(pluginId);
		await _registrar.RegisterInstalledDetachedAsync("com.example.never-installed");

		Assert.Multiple(() =>
		{
			Assert.That(_integrationRegistry.Registered.Select(i => i.Id), Does.Contain(pluginId));
			Assert.That(_integrationRegistry.Registered.Select(i => i.Id),
				Does.Not.Contain("com.example.never-installed"));
		});
	}

	[Test]
	public async Task A_plugin_directory_with_no_versions_left_gets_no_detached_adapter()
	{
		var uninstalled = "com.example.uninstalled";
		_installationCatalog.Plugins.Add(Installed("com.example.plugin"));
		_installationCatalog.Plugins.Add(new InstalledPlugin
		{
			PluginId = uninstalled, PluginDirectory = "/plugins/" + uninstalled, Versions = []
		});

		await _registrar.RegisterInstalledButStoppedAsync();
		await _registrar.RegisterInstalledDetachedAsync(uninstalled);

		Assert.Multiple(() =>
		{
			Assert.That(_integrationRegistry.Registered.Select(i => i.Id), Does.Not.Contain(uninstalled));
			Assert.That(_integrationRegistry.Registered.Select(i => i.Id), Does.Contain("com.example.plugin"));
		});
	}

	[Test]
	public async Task An_adapter_whose_plugin_directory_is_gone_is_swept_away()
	{
		var vanished = "com.example.vanished";
		_installationCatalog.Plugins.Add(Installed(vanished));
		_installationCatalog.Plugins.Add(Installed("com.example.plugin"));
		await _registrar.RegisterInstalledButStoppedAsync();

		_installationCatalog.Plugins.RemoveAll(plugin => plugin.PluginId == vanished);
		await _registrar.UnregisterVanishedInstallationsAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_integrationRegistry.Registered.Select(i => i.Id), Does.Not.Contain(vanished));
			Assert.That(_integrationRegistry.Registered.Select(i => i.Id), Does.Contain("com.example.plugin"));
		});
	}

	[Test]
	public async Task Sweeping_keeps_the_adapter_of_a_plugin_whose_directory_survived_without_its_versions()
	{
		var halfRemoved = "com.example.half-removed";
		_installationCatalog.Plugins.Add(Installed(halfRemoved));
		await _registrar.RegisterInstalledButStoppedAsync();

		_installationCatalog.Plugins.RemoveAll(plugin => plugin.PluginId == halfRemoved);
		_installationCatalog.Plugins.Add(new InstalledPlugin
		{
			PluginId = halfRemoved, PluginDirectory = "/plugins/" + halfRemoved, Versions = []
		});

		await _registrar.UnregisterVanishedInstallationsAsync();

		Assert.That(_integrationRegistry.Registered.Select(i => i.Id),
			Does.Contain(halfRemoved),
			"a half-removed install is still uninstallable from the page, so its card has to stay reachable");
	}

	[Test]
	public async Task Sweeping_leaves_a_session_that_was_never_installed_alone()
	{
		var sessionOnly = "com.example.session-only";
		_invoker.ActionsDescribeResult = new ActionCatalogPayload { Actions = [] };
		_installationCatalog.Plugins.Add(Installed("com.example.plugin"));
		await ConnectSessionAsync(sessionOnly, [Action("play")], Accepted(CapabilityKinds.Actions));
		await _registrar.RegisterAsync(sessionOnly);

		await _registrar.UnregisterVanishedInstallationsAsync();

		Assert.That(_integrationRegistry.Registered.Select(i => i.Id), Does.Contain(sessionOnly));
	}

	[Test]
	public async Task Sweeping_leaves_a_dropped_but_resumable_session_alone()
	{
		var reconnecting = "com.example.reconnecting";
		_invoker.ActionsDescribeResult = new ActionCatalogPayload { Actions = [] };
		_installationCatalog.Plugins.Add(Installed("com.example.plugin"));
		var sessionId = await ConnectSessionAsync(reconnecting,
			[Action("play")],
			Accepted(CapabilityKinds.Actions));
		await _registrar.RegisterAsync(reconnecting);
		_sessionRegistry.Detach(sessionId, _time.GetUtcNow());

		await _registrar.UnregisterVanishedInstallationsAsync();

		Assert.That(_integrationRegistry.Registered.Select(i => i.Id),
			Does.Contain(reconnecting),
			"a plugin inside its resume window is reconnecting, not gone");
	}

	[Test]
	public async Task Sweeping_leaves_built_in_integrations_alone()
	{
		_installationCatalog.Plugins.Add(Installed("com.example.plugin"));
		await _registrar.RegisterInstalledButStoppedAsync();
		await _integrationRegistry.RegisterAsync(StubIntegration.Create("clock"), IntegrationOrigin.BuiltIn);

		await _registrar.UnregisterVanishedInstallationsAsync();

		Assert.That(_integrationRegistry.Registered.Select(i => i.Id), Does.Contain("clock"));
	}

	[Test]
	public async Task Sweeping_removes_the_card_of_the_only_installed_plugin_when_its_directory_goes()
	{
		var pluginId = "com.example.plugin";
		_installationCatalog.Plugins.Add(Installed(pluginId));
		await _registrar.RegisterInstalledButStoppedAsync();

		_installationCatalog.Plugins.Clear();
		await _registrar.UnregisterVanishedInstallationsAsync();

		Assert.That(_integrationRegistry.Registered.Select(i => i.Id), Does.Not.Contain(pluginId));
	}

	[Test]
	public async Task Registering_detached_leaves_a_connected_plugins_live_adapter_alone()
	{
		var pluginId = "com.example.plugin";
		_invoker.ActionsDescribeResult = new ActionCatalogPayload { Actions = [] };
		_installationCatalog.Plugins.Add(Installed(pluginId));

		await ConnectSessionAsync(pluginId, [Action("play")], Accepted(CapabilityKinds.Actions));
		await _registrar.RegisterAsync(pluginId);

		var live = _integrationRegistry.Registered.Single(i => i.Id == pluginId);

		await _registrar.RegisterInstalledDetachedAsync(pluginId);

		Assert.That(_integrationRegistry.Registered.Single(i => i.Id == pluginId),
			Is.SameAs(live),
			"a connected plugin must keep the live adapter it registered on welcome");
	}

	[Test]
	public async Task A_plugin_with_a_live_session_is_named_without_reading_its_manifest()
	{
		var pluginId = "com.example.plugin";
		_invoker.ActionsDescribeResult = new ActionCatalogPayload { Actions = [] };

		var installedVersion = new InstalledPluginVersion
		{
			Version = "1.0.0", VersionDirectory = "/plugins/" + pluginId + "/versions/1.0.0",
			ManifestPath = "/plugins/" + pluginId + "/versions/1.0.0/manifest.json"
		};
		_installationCatalog.Plugins.Add(new InstalledPlugin
		{
			PluginId = pluginId,
			PluginDirectory = "/plugins/" + pluginId,
			Versions = [installedVersion],
			ActiveVersion = installedVersion
		});
		_manifestReader.ResultsByPath[installedVersion.ManifestPath] = PluginManifestReadResult.Ok(new PluginManifest
		{
			ManifestVersion = 1,
			Id = pluginId,
			Name = "Sample",
			Version = "1.0.0",
			Entrypoints = new Dictionary<string, PluginEntrypoint>(StringComparer.Ordinal)
		});

		await ConnectSessionAsync(pluginId,
			[Action("play")],
			Accepted(CapabilityKinds.Actions),
			displayName: "Weather Widget");
		await _registrar.RegisterAsync(pluginId);

		Assert.Multiple(() =>
		{
			Assert.That(TestLocalization.Resolve(_integrationRegistry.Registered.Single(i => i.Id == pluginId).Name),
				Is.EqualTo("Weather Widget"));
			Assert.That(_manifestReader.Reads, Is.Empty);
		});
	}

	[Test]
	public async Task An_installed_plugin_with_no_session_is_named_by_its_manifest()
	{
		var pluginId = "com.example.plugin";

		var installedVersion = new InstalledPluginVersion
		{
			Version = "1.0.0", VersionDirectory = "/plugins/" + pluginId + "/versions/1.0.0",
			ManifestPath = "/plugins/" + pluginId + "/versions/1.0.0/manifest.json"
		};
		_installationCatalog.Plugins.Add(new InstalledPlugin
		{
			PluginId = pluginId,
			PluginDirectory = "/plugins/" + pluginId,
			Versions = [installedVersion],
			ActiveVersion = installedVersion
		});
		_manifestReader.ResultsByPath[installedVersion.ManifestPath] = PluginManifestReadResult.Ok(new PluginManifest
		{
			ManifestVersion = 1,
			Id = pluginId,
			Name = "Sample",
			Version = "1.0.0",
			Entrypoints = new Dictionary<string, PluginEntrypoint>(StringComparer.Ordinal)
		});

		await _registrar.RegisterInstalledDetachedAsync(pluginId);

		Assert.That(TestLocalization.Resolve(_integrationRegistry.Registered.Single(i => i.Id == pluginId).Name),
			Is.EqualTo("Sample"));
	}

	[Test]
	public async Task A_plugin_with_neither_a_session_nor_a_readable_manifest_falls_back_to_its_id()
	{
		var pluginId = "com.example.plugin";

		var installedVersion = new InstalledPluginVersion
		{
			Version = "1.0.0", VersionDirectory = "/plugins/" + pluginId + "/versions/1.0.0",
			ManifestPath = "/plugins/" + pluginId + "/versions/1.0.0/manifest.json"
		};
		_installationCatalog.Plugins.Add(new InstalledPlugin
		{
			PluginId = pluginId,
			PluginDirectory = "/plugins/" + pluginId,
			Versions = [installedVersion],
			ActiveVersion = installedVersion
		});
		_manifestReader.ResultsByPath[installedVersion.ManifestPath] =
			PluginManifestReadResult.Fail(PluginManifestError.NotFound, "No manifest.");

		await _registrar.RegisterInstalledDetachedAsync(pluginId);

		Assert.That(TestLocalization.Resolve(_integrationRegistry.Registered.Single(i => i.Id == pluginId).Name),
			Is.EqualTo(pluginId));
	}

	[Test]
	public async Task An_installed_manifest_version_wins_over_the_session_declared_version()
	{
		var pluginId = "com.example.plugin";
		_invoker.ActionsDescribeResult = new ActionCatalogPayload { Actions = [] };

		var manifestVersion = new InstalledPluginVersion
		{
			Version = "2.0.0", VersionDirectory = "/plugins/" + pluginId + "/versions/2.0.0",
			ManifestPath = "/plugins/" + pluginId + "/versions/2.0.0/manifest.json"
		};
		_installationCatalog.Plugins.Add(new InstalledPlugin
		{
			PluginId = pluginId,
			PluginDirectory = "/plugins/" + pluginId,
			Versions = [manifestVersion],
			ActiveVersion = manifestVersion
		});

		await ConnectSessionAsync(pluginId,
			[Action("play")],
			Accepted(CapabilityKinds.Actions),
			declaredVersion: "1.0.0");

		await _registrar.RegisterAsync(pluginId);

		var adapter = _integrationRegistry.Registered.Single(i => i.Id == pluginId);
		Assert.That(adapter.Version, Is.EqualTo("2.0.0"));
	}

	[Test]
	public async Task The_session_declared_version_is_used_when_there_is_no_installed_manifest()
	{
		var pluginId = "com.example.plugin";
		_invoker.ActionsDescribeResult = new ActionCatalogPayload { Actions = [] };

		await ConnectSessionAsync(pluginId,
			[Action("play")],
			Accepted(CapabilityKinds.Actions),
			declaredVersion: "1.0.0");

		await _registrar.RegisterAsync(pluginId);

		var adapter = _integrationRegistry.Registered.Single(i => i.Id == pluginId);
		Assert.That(adapter.Version, Is.EqualTo("1.0.0"));
	}

	[Test]
	public async Task The_placeholder_version_is_used_when_neither_manifest_nor_declared_version_exist()
	{
		var pluginId = "com.example.plugin";
		_invoker.ActionsDescribeResult = new ActionCatalogPayload { Actions = [] };

		await ConnectSessionAsync(pluginId, [Action("play")], Accepted(CapabilityKinds.Actions));

		await _registrar.RegisterAsync(pluginId);

		var adapter = _integrationRegistry.Registered.Single(i => i.Id == pluginId);
		Assert.That(adapter.Version, Is.EqualTo(PluginRuntimeSnapshot.UnknownVersion));
	}

	[Test]
	public async Task A_malformed_icon_content_hash_fails_registration_and_is_never_read_from_the_cache()
	{
		var pluginId = "com.example.plugin";
		_invoker.ActionsDescribeResult = new ActionCatalogPayload { Actions = [] };

		_invoker.IconBytes = [1, 2, 3];
		_invoker.IconContentHash = "sha256:../../../../etc/passwd";

		await ConnectSessionAsync(pluginId,
			[Action("play"), Provider(CapabilityKinds.Icons)],
			Accepted(CapabilityKinds.Actions, CapabilityKinds.Icons));

		var registered = await _registrar.RegisterAsync(pluginId);

		Assert.Multiple(() =>
		{
			Assert.That(registered, Is.False);
			Assert.That(_integrationRegistry.Registered, Is.Empty);
			Assert.That(_assetCache.TryRead("sha256:../../../../etc/passwd", out _, out _), Is.False);
		});
	}

	[Test]
	public async Task A_dropped_session_stays_registered_and_reports_uninitialized()
	{
		var pluginId = "com.example.plugin";
		_invoker.ActionsDescribeResult = new ActionCatalogPayload { Actions = [] };

		var sessionId = await ConnectSessionAsync(pluginId, [Action("play")], Accepted(CapabilityKinds.Actions));
		await _registrar.RegisterAsync(pluginId);

		_sessionRegistry.Detach(sessionId, _time.GetUtcNow());

		Assert.Multiple(() =>
		{
			Assert.That(_integrationRegistry.Registered.Select(i => i.Id), Does.Contain(pluginId));
			Assert.That(_integrationRegistry.Registered.Single(i => i.Id == pluginId).IsInitialized, Is.False);
		});
	}

	[Test]
	public async Task A_pruned_session_unregisters_the_adapter()
	{
		var pluginId = "com.example.plugin";
		_invoker.ActionsDescribeResult = new ActionCatalogPayload { Actions = [] };

		var sessionId = await ConnectSessionAsync(pluginId, [Action("play")], Accepted(CapabilityKinds.Actions));
		await _registrar.RegisterAsync(pluginId);

		_sessionRegistry.Detach(sessionId, _time.GetUtcNow());
		_time.Advance(ProtocolTimeouts.SessionResumeWindow + TimeSpan.FromSeconds(1));

		_sessionRegistry.Snapshot();
		_sessionRegistry.TryResume(pluginId, sessionId, _time.GetUtcNow(), out _);

		await Task.Delay(50);

		Assert.That(_integrationRegistry.Registered.Select(i => i.Id), Does.Not.Contain(pluginId));
	}

	[Test]
	public async Task A_session_replaced_by_a_new_one_does_not_unregister_the_new_sessions_adapter()
	{
		var pluginId = "com.example.plugin";
		_invoker.ActionsDescribeResult = new ActionCatalogPayload { Actions = [] };

		await ConnectSessionAsync(pluginId, [Action("play")], Accepted(CapabilityKinds.Actions));
		await _registrar.RegisterAsync(pluginId);

		await ConnectSessionAsync(pluginId, [Action("play")], Accepted(CapabilityKinds.Actions));
		var registered = await _registrar.RegisterAsync(pluginId);

		await Task.Delay(50);

		Assert.Multiple(() =>
		{
			Assert.That(registered, Is.True);
			Assert.That(_integrationRegistry.Registered.Select(i => i.Id), Does.Contain(pluginId));
		});
	}

	[Test]
	public async Task A_stopped_plugin_keeps_its_icon_and_config_flow_after_a_restart()
	{
		var pluginId = "com.example.plugin";
		_invoker.ActionsDescribeResult = new ActionCatalogPayload { Actions = [] };

		var iconBytes = new byte[] { 1, 2, 3 };
		var iconContentHash = MacroDeck.Plugin.Protocol.Assets.AssetContentHash.Compute(iconBytes);
		_invoker.IconBytes = iconBytes;
		_invoker.IconContentHash = iconContentHash;

		// The registrar waits for the icon's asset.commit to actually land before it grants the leaf -
		// writing straight to the cache is the fast path WaitForIconAsync checks up front, so this test
		// never has to race a real upload.
		_assetCache.Write(iconContentHash, "image/png", iconBytes);

		var sessionId = await ConnectSessionAsync(pluginId,
			[Action("play"), Provider(CapabilityKinds.Icons), Provider(CapabilityKinds.ConfigFlow)],
			Accepted(CapabilityKinds.Actions, CapabilityKinds.Icons, CapabilityKinds.ConfigFlow));

		await _registrar.RegisterAsync(pluginId);
		await _registrar.UnregisterAsync(pluginId);
		_sessionRegistry.MakeNonResumable(sessionId);
		await _sessionRegistry.Terminate(sessionId, 1000, "test teardown");

		_installationCatalog.Plugins.Add(Installed(pluginId));

		await _registrar.RegisterInstalledButStoppedAsync();

		var adapter = _integrationRegistry.Registered.Single(i => i.Id == pluginId);

		Assert.Multiple(() =>
		{
			Assert.That(adapter, Is.InstanceOf<IIntegrationIconProvider>());
			Assert.That(adapter, Is.InstanceOf<IConfigFlowProvider>());
		});
	}

	[Test]
	public async Task A_plugin_declaring_localization_registers_its_catalog_for_the_active_culture()
	{
		var pluginId = "com.example.plugin";
		_invoker.ActionsDescribeResult = new ActionCatalogPayload { Actions = [] };
		var scope = LocalizationScope.ForPlugin(pluginId);

		_invoker.LocalizationDescribeResult =
			new LocalizationDescribeResult { Scope = scope, DefaultCulture = "en", Cultures = ["en"] };
		_invoker.LocalizationCatalogsByCulture["en"] = new LocalizationCatalogResult
		{
			Culture = "en", Entries = new Dictionary<string, string>(StringComparer.Ordinal) { ["Connect"] = "Connect" }
		};

		await ConnectSessionAsync(pluginId,
			[Action("play"), Provider(CapabilityKinds.Localization)],
			Accepted(CapabilityKinds.Actions, CapabilityKinds.Localization));

		var registered = await _registrar.RegisterAsync(pluginId);

		var resolver = new LocalizationResolver(_localizationCatalogs);
		var resolved = resolver.Resolve(new LocalizedString(new LocalizationKey(scope, "Connect")), "en");

		Assert.Multiple(() =>
		{
			Assert.That(registered, Is.True);
			Assert.That(resolved, Is.EqualTo("Connect"));
			Assert.That(_mediator.Published.OfType<LocalizationCatalogChangedNotification>().Select(n => n.Scope),
				Does.Contain(scope));
		});
	}

	[Test]
	public async Task A_plugin_without_the_localization_capability_registers_normally()
	{
		var pluginId = "com.example.plugin";
		_invoker.ActionsDescribeResult = new ActionCatalogPayload { Actions = [] };

		await ConnectSessionAsync(pluginId, [Action("play")], Accepted(CapabilityKinds.Actions));
		var registered = await _registrar.RegisterAsync(pluginId);

		Assert.Multiple(() =>
		{
			Assert.That(registered, Is.True);
			Assert.That(_localizationCatalogs.Scopes, Does.Not.Contain(LocalizationScope.ForPlugin(pluginId)));
		});
	}

	[Test]
	public async Task A_plugin_catalog_violating_a_limit_is_rejected_whole()
	{
		var pluginId = "com.example.plugin";
		_invoker.ActionsDescribeResult = new ActionCatalogPayload { Actions = [] };
		var scope = LocalizationScope.ForPlugin(pluginId);

		_invoker.LocalizationDescribeResult =
			new LocalizationDescribeResult { Scope = scope, DefaultCulture = "en", Cultures = ["en"] };
		_invoker.LocalizationCatalogsByCulture["en"] = new LocalizationCatalogResult
		{
			Culture = "en",
			Entries = new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["Good"] = "Fine",
				["Bad"] = new('x', ProtocolLimits.MaxLocalizationValueLength + 1)
			}
		};

		await ConnectSessionAsync(pluginId,
			[Action("play"), Provider(CapabilityKinds.Localization)],
			Accepted(CapabilityKinds.Actions, CapabilityKinds.Localization));

		var registered = await _registrar.RegisterAsync(pluginId);

		var resolver = new LocalizationResolver(_localizationCatalogs);
		var resolvedGood = resolver.Resolve(new LocalizedString(new LocalizationKey(scope, "Good")), "en");

		Assert.Multiple(() =>
		{
			Assert.That(registered, Is.True, "the plugin's other capabilities must still register");
			Assert.That(_integrationRegistry.Registered.Select(i => i.Id), Does.Contain(pluginId));
			Assert.That(resolvedGood,
				Does.Contain("[["),
				"a whole-catalog rejection must leave even a valid sibling key unresolved");
		});
	}

	[Test]
	public async Task Forgetting_a_plugin_removes_its_localization_scope()
	{
		var pluginId = "com.example.plugin";
		var scope = await RegisterLocalizedPluginAsync(pluginId);
		var resolver = new LocalizationResolver(_localizationCatalogs);
		var before = resolver.Resolve(new LocalizedString(new LocalizationKey(scope, "Connect")), "en");
		var notificationsBefore = _mediator.Published.OfType<LocalizationCatalogChangedNotification>().Count();

		await _registrar.ForgetAsync(pluginId);

		var after = resolver.Resolve(new LocalizedString(new LocalizationKey(scope, "Connect")), "en");

		Assert.Multiple(() =>
		{
			Assert.That(before, Is.EqualTo("Connect"));
			Assert.That(after, Does.Contain("[["));
			Assert.That(_mediator.Published.OfType<LocalizationCatalogChangedNotification>().Count(),
				Is.EqualTo(notificationsBefore + 1));
		});
	}

	[Test]
	public async Task Unregistering_a_plugin_keeps_its_localization_scope_for_trees_still_showing_it()
	{
		var pluginId = "com.example.plugin";
		var scope = await RegisterLocalizedPluginAsync(pluginId);
		var notificationsBefore = _mediator.Published.OfType<LocalizationCatalogChangedNotification>().Count();

		await _registrar.UnregisterAsync(pluginId);

		var resolver = new LocalizationResolver(_localizationCatalogs);
		Assert.Multiple(() =>
		{
			Assert.That(resolver.Resolve(new LocalizedString(new LocalizationKey(scope, "Connect")), "en"),
				Is.EqualTo("Connect"));
			Assert.That(_integrationRegistry.Registered.Any(i => i.Id == pluginId), Is.False);
			Assert.That(_mediator.Published.OfType<LocalizationCatalogChangedNotification>().Count(),
				Is.EqualTo(notificationsBefore));
		});
	}

	[Test]
	public async Task A_reconnecting_plugin_replaces_its_retained_localization_catalog()
	{
		var pluginId = "com.example.plugin";
		var scope = await RegisterLocalizedPluginAsync(pluginId);
		await _registrar.UnregisterAsync(pluginId);

		_invoker.LocalizationCatalogsByCulture["en"] = new LocalizationCatalogResult
		{
			Culture = "en", Entries = new Dictionary<string, string>(StringComparer.Ordinal) { ["Connect"] = "Link" }
		};
		await _registrar.RegisterAsync(pluginId);

		var resolver = new LocalizationResolver(_localizationCatalogs);
		Assert.That(resolver.Resolve(new LocalizedString(new LocalizationKey(scope, "Connect")), "en"),
			Is.EqualTo("Link"));
	}

	[Test]
	public async Task A_culture_refresh_skips_a_retained_catalog_of_an_absent_plugin_and_refreshes_the_rest()
	{
		var absentScope = await RegisterLocalizedPluginAsync("com.example.absent");
		var liveScope = await RegisterLocalizedPluginAsync("com.example.live");
		await _registrar.UnregisterAsync("com.example.absent");
		_invoker.AbsentPluginIds.Add("com.example.absent");
		_invoker.LocalizationCatalogsByCulture["en"] = new LocalizationCatalogResult
		{
			Culture = "en", Entries = new Dictionary<string, string>(StringComparer.Ordinal) { ["Connect"] = "Link" }
		};

		await _registrar.RefreshLocalizationCatalogsAsync();

		var resolver = new LocalizationResolver(_localizationCatalogs);
		Assert.Multiple(() =>
		{
			Assert.That(resolver.Resolve(new LocalizedString(new LocalizationKey(liveScope, "Connect")), "en"),
				Is.EqualTo("Link"));
			Assert.That(resolver.Resolve(new LocalizedString(new LocalizationKey(absentScope, "Connect")), "en"),
				Is.EqualTo("Connect"));
		});
	}

	[Test]
	public async Task A_refreshed_snapshot_keeps_the_plugins_catalog_widget_types_folder_views_and_layouts()
	{
		var pluginId = "com.example.plugin";
		var scope = await RegisterLocalizedPluginAsync(pluginId);
		var widgetType = await _widgetTypeRegistry.Register(pluginId,
			new WidgetTypeDescriptor("gauge", LocalizedText.FromLiteral("Gauge")));
		var folderView = await _folderViewRegistry.Register(pluginId,
			new FolderViewDescriptor("dashboard", LocalizedText.FromLiteral("Dashboard")));
		var layout = await _layoutRegistry.Register(pluginId,
			new LayoutDescriptor("stream-deck-xl", "Stream Deck XL", []));
		var before = _integrationRegistry.Registered.Single(i => i.Id == pluginId);

		await _registrar.ApplyRefreshedSnapshotAsync(pluginId, _snapshotStore.GetSnapshot(pluginId));

		var resolver = new LocalizationResolver(_localizationCatalogs);
		Assert.Multiple(() =>
		{
			Assert.That(_integrationRegistry.Registered.Single(i => i.Id == pluginId), Is.Not.SameAs(before));
			Assert.That(resolver.Resolve(new LocalizedString(new LocalizationKey(scope, "Connect")), "en"),
				Is.EqualTo("Connect"));
			Assert.That(_widgetTypeRegistry.IsRegistered(widgetType.WidgetTypeId), Is.True);
			Assert.That(_folderViewRegistry.TryResolve(folderView.FolderViewId, out _), Is.True);
			Assert.That(_layoutRegistry.TryResolve(layout.LayoutId, out _), Is.True);
		});
	}

	[Test]
	public async Task A_committed_icon_keeps_the_plugins_localization_catalog()
	{
		var pluginId = "com.example.plugin";
		var scope = await RegisterLocalizedPluginAsync(pluginId);

		var iconBytes = new byte[] { 1, 2, 3 };
		var contentHash = MacroDeck.Plugin.Protocol.Assets.AssetContentHash.Compute(iconBytes);
		_assetReceiver.Begin(pluginId,
			"icon",
			MacroDeck.Plugin.Protocol.Assets.AssetKinds.Icon,
			"image/png",
			iconBytes.Length,
			contentHash);
		_assetReceiver.Chunk(pluginId, "icon", 0, iconBytes);
		_assetReceiver.Commit(pluginId, "icon");

		var resolver = new LocalizationResolver(_localizationCatalogs);
		Assert.Multiple(() =>
		{
			Assert.That(_integrationRegistry.Registered.Single(i => i.Id == pluginId),
				Is.InstanceOf<IIntegrationIconProvider>());
			Assert.That(resolver.Resolve(new LocalizedString(new LocalizationKey(scope, "Connect")), "en"),
				Is.EqualTo("Connect"));
		});
	}

	private async Task<string> RegisterLocalizedPluginAsync(string pluginId)
	{
		_invoker.ActionsDescribeResult = new ActionCatalogPayload { Actions = [] };
		var scope = LocalizationScope.ForPlugin(pluginId);
		_invoker.LocalizationDescribeResult =
			new LocalizationDescribeResult { Scope = scope, DefaultCulture = "en", Cultures = ["en"] };
		_invoker.LocalizationCatalogsByCulture["en"] = new LocalizationCatalogResult
		{
			Culture = "en", Entries = new Dictionary<string, string>(StringComparer.Ordinal) { ["Connect"] = "Connect" }
		};

		await ConnectSessionAsync(pluginId,
			[Action("play"), Provider(CapabilityKinds.Localization)],
			Accepted(CapabilityKinds.Actions, CapabilityKinds.Localization));
		Assert.That(await _registrar.RegisterAsync(pluginId), Is.True);
		return scope;
	}

	[Test]
	public async Task Uninstalling_a_plugin_withdraws_its_widget_types_folder_views_and_layouts()
	{
		var pluginId = "com.example.plugin";

		var widgetType = await _widgetTypeRegistry.Register(pluginId,
			new WidgetTypeDescriptor("gauge", LocalizedText.FromLiteral("Gauge")));
		var folderView = await _folderViewRegistry.Register(pluginId,
			new FolderViewDescriptor("dashboard", LocalizedText.FromLiteral("Dashboard")));
		var layout = await _layoutRegistry.Register(pluginId,
			new LayoutDescriptor("stream-deck-xl", "Stream Deck XL", []));
		var screenSaver = await _screenSaverRegistry.Register(pluginId,
			new MacroDeck.Sdk.ScreenSavers.ScreenSaverDescriptor("photos", LocalizedText.FromLiteral("Photos")));

		await _registrar.ForgetAsync(pluginId);

		Assert.Multiple(() =>
		{
			Assert.That(_widgetTypeRegistry.IsRegistered(widgetType.WidgetTypeId), Is.False);
			Assert.That(_folderViewRegistry.TryResolve(folderView.FolderViewId, out _), Is.False);
			Assert.That(_layoutRegistry.TryResolve(layout.LayoutId, out _), Is.False);
			Assert.That(_screenSaverRegistry.TryResolve(screenSaver.ScreenSaverId, out _), Is.False);
		});
	}

	private sealed class ScriptedInvoker : IPluginCapabilityInvoker
	{
		public ActionCatalogPayload? ActionsDescribeResult { get; set; }

		public bool ThrowOnActionsDescribe { get; set; }

		public byte[] IconBytes { get; set; } = [];

		public string IconContentHash { get; set; } = string.Empty;

		public LocalizationDescribeResult? LocalizationDescribeResult { get; set; }

		public Dictionary<string, LocalizationCatalogResult> LocalizationCatalogsByCulture { get; } =
			new(StringComparer.Ordinal);

		public HashSet<string> AbsentPluginIds { get; } = new(StringComparer.Ordinal);

		public Task<JsonElement?> InvokeAsync(string pluginId,
			CapabilityInvokeRequest request,
			CancellationToken cancellationToken)
		{
			if (AbsentPluginIds.Contains(pluginId))
			{
				throw RemoteCapabilityException.CreateRetryable(ProtocolErrorCodes.Timeout, "No attached connection.");
			}

			if (request.Kind == CapabilityKinds.Localization &&
				request.Operation == CapabilityOperations.Localization.Describe)
			{
				return Task.FromResult<JsonElement?>(LocalizationDescribeResult is null
					? null
					: JsonSerializer.SerializeToElement(LocalizationDescribeResult, PluginProtocolJson.Options));
			}

			if (request.Kind == CapabilityKinds.Localization &&
				request.Operation == CapabilityOperations.Localization.Catalog)
			{
				var culture = ((LocalizationCatalogArguments)request.Arguments!).Culture;
				return Task.FromResult<JsonElement?>(LocalizationCatalogsByCulture.TryGetValue(culture, out var result)
					? JsonSerializer.SerializeToElement(result, PluginProtocolJson.Options)
					: null);
			}

			if (request.Kind == CapabilityKinds.Actions && request.Operation == CapabilityOperations.Actions.Describe)
			{
				if (ThrowOnActionsDescribe)
				{
					throw RemoteCapabilityException.CreateRetryable(ProtocolErrorCodes.Timeout, "Timed out.");
				}

				return Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(
					ActionsDescribeResult ?? new ActionCatalogPayload { Actions = [] },
					PluginProtocolJson.Options));
			}

			if (request.Operation == CapabilityOperations.Actions.Describe)
			{
				return request.Kind switch
				{
					CapabilityKinds.Variables => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(
						new MacroDeck.Plugin.Protocol.Capabilities.Variables.VariableCatalogPayload
						{
							DeclaredVariables = [], Variables = []
						},
						PluginProtocolJson.Options)),
					CapabilityKinds.Events => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(
						new MacroDeck.Plugin.Protocol.Capabilities.Events.EventCatalogPayload
						{
							ProviderName = string.Empty, Events = []
						},
						PluginProtocolJson.Options)),
					CapabilityKinds.MusicPlayer => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(
						new MacroDeck.Plugin.Protocol.Capabilities.MusicPlayer.MusicPlayerDescribePayload
						{
							ProviderName = string.Empty, Instances = []
						},
						PluginProtocolJson.Options)),
					CapabilityKinds.Weather => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(
						new MacroDeck.Plugin.Protocol.Capabilities.Weather.WeatherDescribePayload
						{
							ProviderName = string.Empty, Instances = []
						},
						PluginProtocolJson.Options)),
					CapabilityKinds.VirtualProfiles => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(
						new MacroDeck.Plugin.Protocol.Capabilities.VirtualProfiles.VirtualProfilesDescribePayload
						{
							ProviderName = string.Empty, Profiles = []
						},
						PluginProtocolJson.Options)),
					CapabilityKinds.Icons when IconBytes.Length > 0 => Task.FromResult<JsonElement?>(
						JsonSerializer.SerializeToElement(
							new MacroDeck.Plugin.Protocol.Capabilities.Icons.IconsDescribePayload
							{
								MimeType = "image/png", ByteLength = IconBytes.Length, ContentHash = IconContentHash
							},
							PluginProtocolJson.Options)),
					_ => Task.FromResult<JsonElement?>(null)
				};
			}

			throw new NotSupportedException($"Not scripted for kind '{request.Kind}'.");
		}

		public bool TryComplete(string pluginId, ProtocolEnvelope result) => false;

		public void AbortAll(string pluginId, ProtocolError reason)
		{
		}

		public bool IsLiveActionExecute(string pluginId, string correlationId) => false;
	}

	private sealed class InMemorySnapshotStore : IRemotePluginSnapshotStore
	{
		private readonly Dictionary<string, RemotePluginCapabilitySnapshot> _byPluginId = new(StringComparer.Ordinal);

		public RemotePluginCapabilitySnapshot GetSnapshot(string pluginId)
			=> _byPluginId.TryGetValue(pluginId, out var snapshot)
				? snapshot
				: RemotePluginCapabilitySnapshot.Empty(pluginId);

		public bool Has(string pluginId) => _byPluginId.ContainsKey(pluginId);

		public Task SaveAsync(RemotePluginCapabilitySnapshot snapshot, CancellationToken cancellationToken = default)
		{
			_byPluginId[snapshot.PluginId] = snapshot;
			return Task.CompletedTask;
		}
	}

	private sealed class RecordingIntegrationRegistry : IIntegrationRegistry
	{
		public event EventHandler<IntegrationAvailabilityChangedEventArgs>? AvailabilityChanged
		{
			add { }
			remove { }
		}

		private readonly Dictionary<string, IIntegration> _integrations = new(StringComparer.Ordinal);

		private readonly Dictionary<string, IntegrationOrigin> _origins = new(StringComparer.Ordinal);

		public IReadOnlyList<IIntegration> Registered => [.. _integrations.Values];

		public IReadOnlyList<IIntegration> Integrations => Registered;

		public IActionDefinition? FindAction(string integrationId, string actionId) => null;

		public IActionDefinition? FindAction(QualifiedId id) => null;

		public IReadOnlyList<ActionDescriptor> GetActions(bool enabledOnly = true) => [];

		public bool IsEnabled(string integrationId) => true;

		public void SetEnabled(string integrationId, bool enabled)
		{
		}

		public IntegrationOrigin GetOrigin(string integrationId)
			=> _origins.TryGetValue(integrationId, out var origin) ? origin : IntegrationOrigin.BuiltIn;

		public Task<IntegrationRegistrationResult> RegisterAsync(IIntegration integration,
			IntegrationOrigin origin = IntegrationOrigin.BuiltIn,
			IntegrationMetadata? metadata = null)
		{
			if (!_integrations.TryAdd(integration.Id, integration))
			{
				if (origin != IntegrationOrigin.Plugin)
				{
					return Task.FromResult(IntegrationRegistrationResult.DuplicateOwner);
				}

				_integrations[integration.Id] = integration;
			}

			_origins[integration.Id] = origin;
			return Task.FromResult(IntegrationRegistrationResult.Success);
		}

		public Task<bool> UnregisterAsync(string integrationId)
		{
			_origins.Remove(integrationId);
			return Task.FromResult(_integrations.Remove(integrationId));
		}
	}

	private sealed class RecordingNotificationStore : IUserNotificationStore
	{
		public List<UserNotificationDraft> Raised { get; } = [];

		public int Capacity => 100;

		public UserNotification? Raise(UserNotificationDraft draft)
		{
			Raised.Add(draft);
			return null;
		}

		public UserNotification? RaiseIfAbsent(UserNotificationDraft draft) => Raise(draft);

		public IReadOnlyList<UserNotification> Snapshot() => [];

		public bool UpdateProgress(string dedupeKey, UserNotificationProgress progress) => false;

		public bool Dismiss(string id) => false;

		public bool DismissByKey(string dedupeKey) => false;

		public bool Retire(string dedupeKey) => false;

		public bool DismissAll() => false;

		public event Action? Changed
		{
			add { }
			remove { }
		}
	}

	private sealed class EmptyInstallationCatalog : IPluginInstallationCatalog
	{
		public List<InstalledPlugin> Plugins { get; } = [];

		public IReadOnlyList<InstalledPlugin> Discover() => Plugins;

		public bool TryResolveActive(string pluginId, out InstalledPluginVersion? version)
		{
			version = Plugins.FirstOrDefault(p => p.PluginId == pluginId)?.ActiveVersion;
			return version is not null;
		}

		public void Invalidate()
		{
		}
	}

	private sealed class NeverFindsManifest : IPluginManifestReader
	{
		public PluginManifestReadResult Read(string manifestPath, string expectedPluginId, string expectedVersion)
			=> PluginManifestReadResult.Fail(PluginManifestError.NotFound, "Not found.");

		public PluginManifestReadResult ReadFromJson(string json,
			string? versionDirectory,
			string expectedPluginId,
			string expectedVersion)
			=> PluginManifestReadResult.Fail(PluginManifestError.NotFound, "Not found.");
	}

	private sealed class AlwaysDisconnected : IRemotePluginConnectionState
	{
		public bool IsConnected(string pluginId) => false;
	}

	private sealed class FakeLocalizationPreferenceService : IAppPreferenceService
	{
		public string Culture { get; set; } = LocalizationDefaults.Culture;

		public Task<LocalizationSettings> GetLocalization() => Task.FromResult(new LocalizationSettings(Culture));

		public Task<string> GetTimeFormat() => Task.FromResult(AppPreferenceService.TimeFormatSystem);

		public Task<string> SetTimeFormat(string? timeFormat) => throw new NotSupportedException();

		public Task<LocalizationSettings> SetLocalization(string? culture)
		{
			if (culture is not null)
			{
				Culture = culture;
			}

			return Task.FromResult(new LocalizationSettings(Culture));
		}

		public Task<AppearanceSettings> GetAppearance() => throw new NotSupportedException();

		public Task<AppearanceSettings> SetAppearance(string? themeMode, string? accentColor, string? fontFamily)
			=> throw new NotSupportedException();

		public Task<Guid> GetInstallationId() => throw new NotSupportedException();

		public Task<LoggingSettings> GetLogging() => throw new NotSupportedException();

		public Task<LoggingSettings> SetLogging(Application.Logging.LogEntryLevel? minimumLevel)
			=> throw new NotSupportedException();

		public Task<NetworkSettings> GetNetwork() => throw new NotSupportedException();

		public Task<NetworkSettings> SetNetwork(int? publicPort,
			bool? tlsEnabled = null,
			string? tlsMode = null,
			int? tlsHttpsPort = null,
			bool? discoveryEnabled = null)
			=> throw new NotSupportedException();

		public Task<AdbSettings> GetAdb() => throw new NotSupportedException();

		public Task<AdbSettings> SetAdb(bool? enabled,
			string? executablePath,
			bool? usbConnectionsEnabled,
			string? defaultDeviceSerial,
			bool? stopServerOnExit,
			bool allowPlugins)
			=> throw new NotSupportedException();

		public Task<DeveloperSettings> GetDeveloper() => throw new NotSupportedException();

		public Task<DeveloperSettings> SetDeveloper(bool? enabled) => throw new NotSupportedException();

		public Task<OnboardingSettings> GetOnboarding() => throw new NotSupportedException();

		public Task<OnboardingSettings> SetOnboarding(bool? pending) => throw new NotSupportedException();

		public Task<LockScreenSettings> GetLockScreen() => throw new NotSupportedException();

		public Task<LockScreenSettings> SetLockScreen(bool? enabled) => throw new NotSupportedException();

		public Task<BackupSettings> GetBackups() => throw new NotSupportedException();

		public Task<BackupSettings> SetBackups(string? scheduleFrequency,
			string? scheduleTimeOfDay,
			string? scheduleDayOfWeek,
			int? scheduleDayOfMonth,
			string? retentionPolicy,
			int? retentionKeepLatest,
			bool? beforeHostUpdate,
			bool? beforePluginUpdate) => throw new NotSupportedException();

		public Task<DateTimeOffset?> GetBackupScheduleLastRun() => throw new NotSupportedException();

		public Task SetBackupScheduleLastRun(DateTimeOffset value) => throw new NotSupportedException();

		public Task<ExtensionSettings> GetExtensions() => throw new NotSupportedException();

		public Task<ExtensionSettings> SetExtensions(bool? storeEnabled,
			bool? checkForUpdates,
			bool? notifyOnUpdates,
			int? refreshIntervalMinutes,
		bool? autoUpdate = null)
			=> throw new NotSupportedException();
	}
}
