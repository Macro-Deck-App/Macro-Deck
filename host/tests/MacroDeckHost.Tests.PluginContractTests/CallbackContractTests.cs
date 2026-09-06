using System.Text.Json;
using MacroDeck.Plugin.Hosting.Integrations;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using Microsoft.Extensions.DependencyInjection;
using SdkVariableType = MacroDeck.Sdk.Variables.VariableType;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Widgets;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class CallbackContractTests
{
	private const string PluginId = "com.example.callback";

	private CallbackFakeVariableService _variableService = null!;
	private CallbackFakeUserVariableApi _userVariableApi = null!;
	private CallbackFakeDeckNavigator _deckNavigator = null!;
	private CallbackFakeScriptApi _scriptApi = null!;
	private CallbackFakeWidgetApi _widgetApi = null!;
	private CallbackFakeNotificationStore _notificationStore = null!;
	private CallbackFakeActionInteractions _actionInteractions = null!;
	private CallbackFakeCapabilityInvoker _invoker = null!;
	private CallbackFakeEventBus _eventBus = null!;
	private CallbackFakeConfigStore _configStore = null!;
	private CallbackFakeSecretService _secretService = null!;
	private RecordingUiSessionSink _uiSessions = null!;

	private RecordingDeviceRegistry DeviceRegistry { get; set; } = null!;

	private PluginConnectionState _state = null!;
	private HostInvoker _hostInvoker = null!;
	private HostStateCache _stateCache = null!;
	private CallbackHostLink _link = null!;
	private Task<ConnectionOutcome>? _run;

	private RemoteVariableApi _variables = null!;
	private RemoteUserVariableApi _userVariables = null!;
	private RemoteIntegrationConfig _config = null!;
	private RemoteDeckNavigator _deck = null!;
	private RemoteScriptApi _scripts = null!;
	private RemoteWidgetApi _widgets = null!;
	private RemoteEventPublisher _events = null!;
	private RemoteUserNotifier _notifications = null!;
	private RemoteIntegrationContext _context = null!;
	private RemoteDeviceProviderContext _devices = null!;

	[SetUp]
	public async Task SetUp()
	{
		_variableService = new CallbackFakeVariableService();
		_userVariableApi = new CallbackFakeUserVariableApi();
		_deckNavigator = new CallbackFakeDeckNavigator();
		_scriptApi = new CallbackFakeScriptApi();
		_widgetApi = new CallbackFakeWidgetApi();
		_notificationStore = new CallbackFakeNotificationStore();
		_actionInteractions = new CallbackFakeActionInteractions();
		_invoker = new CallbackFakeCapabilityInvoker();
		_eventBus = new CallbackFakeEventBus();
		_configStore = new CallbackFakeConfigStore();
		_secretService = new CallbackFakeSecretService();
		_uiSessions = new RecordingUiSessionSink();
		DeviceRegistry = new RecordingDeviceRegistry();

		var services = new ServiceCollection();
		services.AddSingleton<IIntegrationConfigStore>(_configStore);
		services.AddSingleton<ISecretService>(_secretService);
		services.AddSingleton<IVariableService>(_variableService);
		var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		var router = new PluginCallbackRouter(new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None),
			_invoker,
			scopeFactory,
			_notificationStore,
			_deckNavigator,
			_scriptApi,
			_widgetApi,
			new CallbackFakeWidgetIconInvalidator(),
			_userVariableApi,
			_actionInteractions,
			_uiSessions,
			DeviceRegistry,
			new LayoutRegistry(new RecordingMediator()),
			new FolderViewRegistry(new RecordingMediator()),
			new WidgetTypeRegistry(new RecordingMediator()),
			new ModalInteractionCoordinator(TimeProvider.System),
			new NullUiTransport(),
			new HostCallbackThrottle(TimeProvider.System, capacity: 1000, refillPerSecond: 1000),
			new CallbackFakeHostLockState(),
			Serilog.Core.Logger.None);

		_state = new PluginConnectionState();
		_hostInvoker = new HostInvoker(_state, TimeProvider.System, Serilog.Core.Logger.None);
		_stateCache = new HostStateCache(_state);
		_link = new CallbackHostLink(PluginId, "session-callback", router, _eventBus);

		var dispatcher = PluginTestSession.Dispatcher([], _state, TimeProvider.System);
		var session = PluginTestSession.Create("session-callback", negotiatedVersion: 1, DateTimeOffset.UnixEpoch);
		var connection = new PluginSessionConnection(_link,
			session,
			dispatcher,
			_state,
			TimeProvider.System,
			Serilog.Core.Logger.None,
			_hostInvoker,
			_stateCache);

		_state.ActiveConnection = connection;
		_run = connection.RunAsync(resumeSessionId: null, instanceId: "callback-fixture", CancellationToken.None);

		await WaitForAsync(() => _state.IsReady);

		_variables = new RemoteVariableApi(_hostInvoker);
		_userVariables = new RemoteUserVariableApi(_hostInvoker);
		_config = new RemoteIntegrationConfig(_hostInvoker);
		_deck = new RemoteDeckNavigator(_hostInvoker, _stateCache);
		_scripts = new RemoteScriptApi(_hostInvoker, _stateCache);
		_widgets = new RemoteWidgetApi(_hostInvoker, _state, _stateCache);
		_events = new RemoteEventPublisher(_state, Serilog.Core.Logger.None);
		_notifications = new RemoteUserNotifier(_hostInvoker, Serilog.Core.Logger.None);
		_devices = new RemoteDeviceProviderContext(_hostInvoker);
		_context = new RemoteIntegrationContext(_variables,
			_userVariables,
			_config,
			_deck,
			_scripts,
			_widgets,
			_events,
			_notifications);
	}

	[TearDown]
	public async Task TearDown()
	{
		if (_run is not null)
		{
			await _link.DisposeAsync();
			await _run;
			_run.Dispose();
		}
	}


	[Test]
	public async Task A_device_registered_over_the_wire_reaches_the_hosts_device_registry()
	{
		var registration = await _devices.RegisterDeviceAsync(new DeviceDescriptor("SERIAL-1",
			"Stream Deck XL",
			"Stream Deck XL",
			"Elgato",
			"com.example.contract::xl",
			new DeviceCapabilities { KeyCount = 32, SupportsImages = true }));

		var stored = DeviceRegistry.Devices[(PluginId, "SERIAL-1")];

		Assert.Multiple(() =>
		{
			Assert.That(registration.DeviceId, Is.EqualTo(DeviceRegistry.AssignedIdOf(PluginId, "SERIAL-1")));
			Assert.That(registration.ProviderDeviceId, Is.EqualTo("SERIAL-1"));
			Assert.That(stored.Name, Is.EqualTo("Stream Deck XL"));
			Assert.That(stored.LayoutReference, Is.EqualTo("com.example.contract::xl"));
			Assert.That(stored.Capabilities!.KeyCount, Is.EqualTo(32));
			Assert.That(DeviceRegistry.IsOnline(PluginId, "SERIAL-1"), Is.True);
		});
	}

	[Test]
	public async Task Presence_and_unregistration_over_the_wire_keep_the_devices_identity()
	{
		var first = await _devices.RegisterDeviceAsync(new DeviceDescriptor("SERIAL-1", "Deck"));

		await _devices.SetDevicePresenceAsync("SERIAL-1", DevicePresence.Offline);
		var afterOffline = DeviceRegistry.IsOnline(PluginId, "SERIAL-1");

		await _devices.UnregisterDeviceAsync("SERIAL-1");
		var afterUnregister = DeviceRegistry.IsOnline(PluginId, "SERIAL-1");

		var second = await _devices.RegisterDeviceAsync(new DeviceDescriptor("SERIAL-1", "Deck"));

		Assert.Multiple(() =>
		{
			Assert.That(afterOffline, Is.False);
			Assert.That(afterUnregister, Is.False);
			Assert.That(second.DeviceId, Is.EqualTo(first.DeviceId));
			Assert.That(DeviceRegistry.IsOnline(PluginId, "SERIAL-1"), Is.True);
		});
	}

	[Test]
	public async Task An_updated_device_keeps_its_registration_and_refreshes_its_metadata()
	{
		await _devices.RegisterDeviceAsync(new DeviceDescriptor("SERIAL-1", "Deck"));

		await _devices.UpdateDeviceAsync(new DeviceDescriptor("SERIAL-1", "Deck", Model: "Stream Deck Mini"));

		Assert.That(DeviceRegistry.Devices[(PluginId, "SERIAL-1")].Model, Is.EqualTo("Stream Deck Mini"));
	}

	[Test]
	public async Task A_variable_written_through_IVariableApi_is_readable_from_the_hosts_variable_service_afterwards()
	{
		var handle = await _context.Variables.CreateAsync("counter", VariableType.Numeric, initialValue: 1);
		await _context.Variables.SetValueAsync(handle.Id, 42);

		Assert.That(_variableService.ValueOf(handle.Id), Is.EqualTo("42"));
	}

	[Test]
	public async Task A_deleted_variable_is_gone_from_the_hosts_variable_service()
	{
		var handle = await _context.Variables.CreateAsync("temp", VariableType.Text);
		await _context.Variables.DeleteAsync(handle.Id);

		var stored = await _variableService.GetByOwnerIntegration(PluginId);
		Assert.That(stored, Is.Empty);
	}


	[Test]
	public async Task Applying_a_user_variable_reaches_the_hosts_user_variable_api()
	{
		await _context.UserVariables.ApplyAsync("brightness", null, UserVariableOperation.Set, "5");

		Assert.That(_userVariableApi.Applied,
			Has.Exactly(1)
				.Matches<(string Name, string? OwnerWidgetId, UserVariableOperation Operation, string? Value)>(call =>
					call.Name == "brightness" && call.Operation == UserVariableOperation.Set && call.Value == "5"));
	}


	[Test]
	public async Task Creating_a_user_variable_reaches_the_hosts_user_variable_api_with_every_argument()
	{
		var result = await _context.UserVariables.CreateAsync("track", "widget-1", SdkVariableType.Numeric, "1", 2);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(UserVariableCreateStatus.Created));
			Assert.That(_userVariableApi.Created,
				Has.Exactly(1)
					.Matches<(string Name, string? OwnerWidgetId, SdkVariableType Type, string? InitialValue, int?
						DecimalPlaces)>(call =>
						call.Name == "track" &&
						call.OwnerWidgetId == "widget-1" &&
						call.Type == SdkVariableType.Numeric &&
						call.InitialValue == "1" &&
						call.DecimalPlaces == 2));
		});
	}


	[Test]
	public async Task A_refused_create_surfaces_the_hosts_own_status_across_the_socket()
	{
		_userVariableApi.CreateResultToReturn =
			UserVariableCreateResult.Failed(UserVariableCreateStatus.UnknownWidget, "No widget with id 'x' exists.");

		var result = await _context.UserVariables.CreateAsync("track", "x", SdkVariableType.Text);

		// Collapsing every refusal into one generic failure is the classic remote-proxy bug; the happy-path
		// test above passes with it.
		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(UserVariableCreateStatus.UnknownWidget));
			Assert.That(result.Message, Is.EqualTo("No widget with id 'x' exists."));
		});
	}


	[Test]
	public async Task A_config_value_set_through_IIntegrationConfig_is_readable_back_through_it()
	{
		var entryId = _configStore.Seed(PluginId, "Account", new Dictionary<string, JsonElement>());

		await _context.Config.SetStringAsync(entryId, "username", "alice");
		var readBack = await _context.Config.GetStringAsync(entryId, "username");

		Assert.That(readBack, Is.EqualTo("alice"));
	}

	[Test]
	public async Task GetEntriesAsync_lists_only_this_plugins_config_entries()
	{
		_configStore.Seed(PluginId, "Mine", new Dictionary<string, JsonElement>());
		_configStore.Seed("some.other.plugin", "Theirs", new Dictionary<string, JsonElement>());

		var entries = await _context.Config.GetEntriesAsync();

		Assert.That(entries.Select(e => e.Title), Is.EqualTo(new[] { "Mine" }));
	}


	[Test]
	public async Task ChangeFolderAsync_reaches_the_hosts_deck_navigator()
	{
		await _context.Deck.ChangeFolderAsync("folder-1");

		Assert.That(_deckNavigator.ChangedFolders, Is.EqualTo(new[] { "folder-1" }));
	}

	[Test]
	public async Task RunAsync_reaches_the_hosts_script_api_and_returns_its_result()
	{
		_scriptApi.ResultToReturn = ActionResult.Failed("SOME_CODE", "nope");

		var result = await _context.Scripts.RunAsync("script-1");

		Assert.Multiple(() =>
		{
			Assert.That(_scriptApi.RanScripts, Is.EqualTo(new[] { "script-1" }));
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
		});
	}

	[Test]
	public async Task ApplyAsync_reaches_the_hosts_widget_api_and_returns_its_result()
	{
		_widgetApi.ResultToReturn = false;

		var applied = await _context.Widgets.ApplyAsync(new WidgetAppearanceRequest
		{
			WidgetId = "widget-1", Patch = new WidgetAppearancePatch()
		});

		Assert.Multiple(() =>
		{
			Assert.That(_widgetApi.AppliedWidgetIds, Is.EqualTo(new[] { "widget-1" }));
			Assert.That(applied, Is.False);
		});
	}

	[Test]
	public void GetFolders_and_GetProfiles_are_empty_before_the_first_host_state_push()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_context.Deck.GetFolders(), Is.Empty);
			Assert.That(_context.Deck.GetProfiles(), Is.Empty);
		});
	}

	[Test]
	public void GetScripts_is_empty_before_the_first_host_state_push()
		=> Assert.That(_context.Scripts.GetScripts(), Is.Empty);

	[Test]
	public void GetWidgets_is_empty_and_Exists_is_false_before_the_first_host_state_push()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_context.Widgets.GetWidgets(), Is.Empty);
			Assert.That(_context.Widgets.Exists("anything"), Is.False);
		});
	}

	[Test]
	public async Task A_host_state_push_fills_GetFolders_and_GetProfiles()
	{
		_link.PushHostState(HostApis.Deck,
			new DeckStateDto
			{
				Folders = [new DeckFolder { Id = "f1", Label = "Folder 1" }],
				Profiles = [new DeckProfile { Id = "p1", Label = "Profile 1" }]
			});

		await WaitForAsync(() => _context.Deck.GetFolders().Count > 0);

		Assert.Multiple(() =>
		{
			Assert.That(_context.Deck.GetFolders().Select(f => f.Id), Is.EqualTo(new[] { "f1" }));
			Assert.That(_context.Deck.GetProfiles().Select(p => p.Id), Is.EqualTo(new[] { "p1" }));
		});
	}

	[Test]
	public async Task A_host_state_push_fills_GetScripts()
	{
		_link.PushHostState(HostApis.Scripts, new List<Script> { new() { Id = "s1", Name = "Script 1" } });

		await WaitForAsync(() => _context.Scripts.GetScripts().Count > 0);

		Assert.That(_context.Scripts.GetScripts().Select(s => s.Id), Is.EqualTo(new[] { "s1" }));
	}

	[Test]
	public async Task A_host_state_push_fills_GetWidgets_and_Exists()
	{
		_link.PushHostState(HostApis.Widgets,
			new List<WidgetTargetInfo>
			{
				new() { Id = "w1", Label = "Widget 1", Location = "Profile / Folder", Type = "action-button" }
			});

		await WaitForAsync(() => _context.Widgets.GetWidgets().Count > 0);

		Assert.Multiple(() =>
		{
			Assert.That(_context.Widgets.Exists("w1"), Is.True);
			Assert.That(_context.Widgets.Exists("nope"), Is.False);
		});
	}


	[Test]
	public async Task Notify_lands_in_the_hosts_notification_store()
	{
		_context.Notifications.Notify(new UserNotificationRequest { Title = "Hello" });

		await WaitForAsync(() => _notificationStore.Raised.Count > 0);

		Assert.That(_notificationStore.Raised.Single().Title, Is.EqualTo("Hello"));
	}

	[Test]
	public async Task Dismiss_reaches_the_hosts_notification_store()
	{
		_context.Notifications.Dismiss("my-key");

		await WaitForAsync(() => _notificationStore.DismissedKeys.Count > 0);

		Assert.That(_notificationStore.DismissedKeys.Single(), Does.Contain("my-key"));
	}

	[Test]
	public void Notify_never_throws_into_the_caller_even_when_there_is_no_connection()
	{
		_state.ActiveConnection = null;

		Assert.DoesNotThrow(() => _context.Notifications.Notify(new UserNotificationRequest { Title = "Hello" }));
	}

	[Test]
	public void Dismiss_never_throws_into_the_caller_even_when_there_is_no_connection()
	{
		_state.ActiveConnection = null;

		Assert.DoesNotThrow(() => _context.Notifications.Dismiss("key"));
	}


	[Test]
	public async Task Publish_reaches_the_host_through_event_publish_not_host_invoke_with_the_plugins_id_stamped()
	{
		_context.Events.Publish("something-happened", new Dictionary<string, object?> { ["value"] = 1 });

		await WaitForAsync(() => _eventBus.Published.Count > 0);

		Assert.That(_eventBus.Published.Single().EventId, Does.StartWith(PluginId));
	}

	[Test]
	public void Publish_never_throws_into_the_caller_even_when_there_is_no_connection()
	{
		_state.ActiveConnection = null;

		Assert.DoesNotThrow(() => _context.Events.Publish("something-happened"));
	}


	[Test]
	public async Task RequestItemPicker_reaches_the_host_when_the_correlation_names_a_live_execute()
	{
		_invoker.LiveExecuteCorrelations.Add((PluginId, "live-correlation"));
		var interactions = new RemoteActionInteractions(_hostInvoker, Serilog.Core.Logger.None, "live-correlation");

		interactions.RequestItemPicker(null, "instance-1", MusicPlayerCatalogItemKind.Track);

		await WaitForAsync(() => _actionInteractions.ItemPickerRequests.Count > 0);

		Assert.That(_actionInteractions.ItemPickerRequests.Single().InstanceId, Is.EqualTo("instance-1"));
	}

	[Test]
	public async Task RequestDevicePicker_is_refused_and_never_throws_when_the_correlation_is_not_a_live_execute()
	{
		var interactions = new RemoteActionInteractions(_hostInvoker, Serilog.Core.Logger.None, "forged-correlation");

		Assert.DoesNotThrow(() => interactions.RequestDevicePicker(null, "instance-1", startPlayback: true));

		await Task.Delay(50);
		Assert.That(_actionInteractions.DevicePickerRequests, Is.Empty);
	}

	[Test]
	public void RequestItemPicker_never_throws_into_the_caller_even_when_there_is_no_connection()
	{
		_state.ActiveConnection = null;
		var interactions = new RemoteActionInteractions(_hostInvoker, Serilog.Core.Logger.None, "whatever");

		Assert.DoesNotThrow(() => interactions.RequestItemPicker(null, "instance-1", MusicPlayerCatalogItemKind.Track));
	}

	[Test]
	public async Task A_snapshot_a_patch_and_a_fault_reach_the_hosts_ui_session_sink()
	{
		var tree = "{\"revision\":1,\"surface\":{\"kind\":\"config\",\"sessionMode\":\"exclusive\"," +
			"\"attributes\":{}},\"root\":{\"id\":\"root\",\"type\":\"panel\"}}";
		var patch = "{\"fromRevision\":1,\"toRevision\":2,\"operations\":[]}";

		await _hostInvoker.InvokeAsync(HostApis.Ui,
			HostOperations.Ui.Snapshot,
			new { sessionId = "s1", tree = JsonDocument.Parse(tree).RootElement },
			CancellationToken.None);

		await _hostInvoker.InvokeAsync(HostApis.Ui,
			HostOperations.Ui.Patch,
			new { sessionId = "s1", patch = JsonDocument.Parse(patch).RootElement },
			CancellationToken.None);

		await _hostInvoker.InvokeAsync(HostApis.Ui,
			HostOperations.Ui.Fault,
			new { sessionId = "s1", code = "PROVIDER_FAULTED", message = "gone" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_uiSessions.Snapshots.Single().SessionId, Is.EqualTo("s1"));
			Assert.That(_uiSessions.Snapshots.Single().ProviderId, Is.EqualTo(PluginId));
			Assert.That(_uiSessions.Patches.Single().SessionId, Is.EqualTo("s1"));
			Assert.That(_uiSessions.Faults.Single().Code, Is.EqualTo("PROVIDER_FAULTED"));
		});
	}

	[Test]
	public void A_refused_ui_payload_comes_back_to_the_plugin_as_its_own_host_result_error()
	{
		_uiSessions.Verdict = UiSessionIngestResult.Reject(UiSessionErrorCodes.PayloadTooLarge, "too big");

		var exception = Assert.CatchAsync<HostInvocationException>(async () => await _hostInvoker.InvokeAsync(
			HostApis.Ui,
			HostOperations.Ui.Patch,
			new { sessionId = "s1", patch = JsonDocument.Parse("{\"fromRevision\":1,\"toRevision\":2}").RootElement },
			CancellationToken.None));

		Assert.That(exception!.Code, Is.EqualTo(UiSessionErrorCodes.PayloadTooLarge));
	}

	private static async Task WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
	{
		var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));

		while (DateTime.UtcNow < deadline)
		{
			if (condition())
			{
				return;
			}

			await Task.Delay(5);
		}

		Assert.Fail("The condition was never met.");
	}
}

/// <summary>
/// Records what the <c>ui</c> host api delivered, and can refuse it - the refusal is how a provider
/// learns from its own <c>host.result</c> that a payload was rejected.
/// </summary>
internal sealed class RecordingUiSessionSink : IUiSessionSink
{
	public List<(string ProviderId, string SessionId, byte[] Tree)> Snapshots { get; } = [];

	public List<(string ProviderId, string SessionId, byte[] Patch)> Patches { get; } = [];

	public List<(string ProviderId, string SessionId, string Code, string? Message)> Faults { get; } = [];

	public UiSessionIngestResult Verdict { get; set; } = UiSessionIngestResult.Accept();

	public UiSessionIngestResult PublishSnapshot(string providerId, string sessionId, UiRawJson tree)
	{
		Snapshots.Add((providerId, sessionId, tree.Utf8.ToArray()));
		return Verdict;
	}

	public UiSessionIngestResult PublishPatch(string providerId, string sessionId, UiRawJson patch)
	{
		Patches.Add((providerId, sessionId, patch.Utf8.ToArray()));
		return Verdict;
	}

	public void PublishFault(string providerId, string sessionId, string code, string? message)
		=> Faults.Add((providerId, sessionId, code, message));
}
