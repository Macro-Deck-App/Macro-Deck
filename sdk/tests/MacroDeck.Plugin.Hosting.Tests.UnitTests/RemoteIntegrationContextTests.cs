using System.Text.Json;
using MacroDeck.Plugin.Hosting.Integrations;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.FolderViews;
using MacroDeck.Sdk.Layouts;
using MacroDeck.Sdk.Ui;
using MacroDeck.Localization;
using MacroDeck.Plugin.Hosting.Capabilities.Ui;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

/// <summary>
/// <c>RemoteIntegrationContext</c> and its <c>HostApis</c> proxies, unit-level against a hand-rolled
/// <see cref="IHostInvoker" /> rather than the full harness. The regression test this file exists for:
/// issue #413 step 6 deleted <c>NotSupportedIntegrationContext</c>, and every member here must now be a
/// working proxy instead of throwing <see cref="NotSupportedException" />.
/// </summary>
[TestFixture]
public class RemoteIntegrationContextTests
{
	private RecordingHostInvoker _invoker = null!;
	private RemoteIntegrationContext _context = null!;

	[SetUp]
	public void SetUp()
	{
		_invoker = new RecordingHostInvoker();
		var stateCache = new HostStateCache(new PluginConnectionState());

		_context = new RemoteIntegrationContext(new RemoteVariableApi(_invoker),
			new RemoteUserVariableApi(_invoker),
			new RemoteIntegrationConfig(_invoker),
			new RemoteDeckNavigator(_invoker, stateCache),
			new RemoteScriptApi(_invoker, stateCache),
			new RemoteWidgetApi(_invoker, new PluginConnectionState(), stateCache),
			new RemoteEventPublisher(new PluginConnectionState(), Serilog.Core.Logger.None),
			new RemoteUserNotifier(_invoker, Serilog.Core.Logger.None));
	}

	[TearDown]
	public void TearDown() => _invoker.Dispose();

	// ---- regression: no member throws NotSupportedException any more -------------------------------

	[Test]
	public async Task Every_IIntegrationContext_member_is_a_working_proxy_not_NotSupported()
	{
		Assert.DoesNotThrowAsync(async () => await _context.Variables.GetAllAsync());
		Assert.DoesNotThrowAsync(async () =>
			await _context.UserVariables.ApplyAsync("x", null, UserVariableOperation.Set, "1"));
		Assert.DoesNotThrowAsync(async () => await _context.Config.GetEntriesAsync());
		Assert.DoesNotThrowAsync(async () => await _context.Deck.ChangeFolderAsync("f1"));
		Assert.That(() => _context.Deck.GetFolders(), Throws.Nothing);
		Assert.DoesNotThrowAsync(async () => await _context.Scripts.RunAsync("s1"));
		Assert.That(() => _context.Scripts.GetScripts(), Throws.Nothing);
		Assert.DoesNotThrowAsync(async () => await _context.Widgets.ApplyAsync(new WidgetAppearanceRequest
			{ WidgetId = "w1", Patch = new WidgetAppearancePatch() }));
		Assert.That(() => _context.Widgets.GetWidgets(), Throws.Nothing);
		Assert.That(() => _context.Events.Publish("evt"), Throws.Nothing);
		Assert.That(() => _context.Notifications.Notify(new UserNotificationRequest { Title = "t" }), Throws.Nothing);
		Assert.That(() => _context.Notifications.Dismiss("key"), Throws.Nothing);

		// A production instance of MacroDeck.Sdk.Actions.IActionInteractions is handed out per
		// actions/execute invocation (RemoteActionInteractions), not through IIntegrationContext -
		// covered separately below for the same regression.
		var interactions = new RemoteActionInteractions(_invoker, Serilog.Core.Logger.None, "correlation-1");
		Assert.That(() => interactions.RequestItemPicker(null, "i1", MusicPlayerCatalogItemKind.Track), Throws.Nothing);
		Assert.That(() => interactions.RequestDevicePicker(null, "i1", false), Throws.Nothing);

		await Task.CompletedTask;
	}

	// ---- each proxy sends the Api/Operation pair HostOperations declares for it ---------------------

	private static IEnumerable<TestCaseData> ApiOperationCases()
	{
		yield return Case(HostApis.Variables,
			HostOperations.Variables.List,
			(context, invoker) => context.Variables.GetAllAsync());
		yield return Case(HostApis.Variables,
			HostOperations.Variables.Get,
			(context, invoker) => context.Variables.GetByNameAsync("n"));
		yield return Case(HostApis.Variables,
			HostOperations.Variables.Create,
			(context, invoker) =>
			{
				invoker.NextResult = JsonSerializer.SerializeToElement(
					new VariableHandle(Guid.NewGuid(), "n", VariableType.Text, null, null),
					PluginProtocolJson.Options);
				return context.Variables.CreateAsync("n", VariableType.Text);
			});
		yield return Case(HostApis.Variables,
			HostOperations.Variables.Set,
			(context, invoker) => context.Variables.SetValueAsync(Guid.NewGuid(), "v"));
		yield return Case(HostApis.Variables,
			HostOperations.Variables.Delete,
			(context, invoker) => context.Variables.DeleteAsync(Guid.NewGuid()));

		yield return Case(HostApis.UserVariables,
			HostOperations.UserVariables.Apply,
			(context, invoker) => context.UserVariables.ApplyAsync("n", null, UserVariableOperation.Set, "1"));

		yield return Case(HostApis.UserVariables,
			HostOperations.UserVariables.Create,
			(context, invoker) => context.UserVariables.CreateAsync("n", null, VariableType.Text, "1"));

		yield return Case(HostApis.Config,
			HostOperations.Config.Entries,
			(context, invoker) => context.Config.GetEntriesAsync());
		yield return Case(HostApis.Config,
			HostOperations.Config.GetString,
			(context, invoker) => context.Config.GetStringAsync(Guid.NewGuid(), "k"));
		yield return Case(HostApis.Config,
			HostOperations.Config.GetSecret,
			(context, invoker) => context.Config.GetSecretAsync(Guid.NewGuid(), "k"));
		yield return Case(HostApis.Config,
			HostOperations.Config.SetString,
			(context, invoker) => context.Config.SetStringAsync(Guid.NewGuid(), "k", "v"));
		yield return Case(HostApis.Config,
			HostOperations.Config.SetSecret,
			(context, invoker) => context.Config.SetSecretAsync(Guid.NewGuid(), "k", "v"));

		yield return Case(HostApis.Deck,
			HostOperations.Deck.ChangeFolder,
			(context, invoker) => context.Deck.ChangeFolderAsync("f1"));
		yield return Case(HostApis.Deck,
			HostOperations.Deck.ChangeProfile,
			(context, invoker) => context.Deck.ChangeProfileAsync("p1"));
		yield return Case(HostApis.Deck,
			HostOperations.Deck.Parent,
			(context, invoker) => context.Deck.GoToParentAsync());
		yield return Case(HostApis.Deck,
			HostOperations.Deck.Back,
			(context, invoker) => context.Deck.GoBackAsync());

		yield return Case(HostApis.Scripts,
			HostOperations.Scripts.Run,
			(context, invoker) => context.Scripts.RunAsync("s1"));

		yield return Case(HostApis.Widgets,
			HostOperations.Widgets.Apply,
			(context, invoker) => context.Widgets.ApplyAsync(new WidgetAppearanceRequest
				{ WidgetId = "w1", Patch = new WidgetAppearancePatch() }));
		yield return Case(HostApis.Widgets,
			HostOperations.Widgets.InvalidateIcon,
			(context, invoker) => context.Widgets.InvalidateIconAsync("current-track"));

		yield return Case(HostApis.Notifications,
			HostOperations.Notifications.Notify,
			async (context, invoker) =>
			{
				context.Notifications.Notify(new UserNotificationRequest { Title = "t" });
				await invoker.WaitForCallAsync();
			});
		yield return Case(HostApis.Notifications,
			HostOperations.Notifications.Dismiss,
			async (context, invoker) =>
			{
				context.Notifications.Dismiss("key");
				await invoker.WaitForCallAsync();
			});
	}

	[TestCaseSource(nameof(ApiOperationCases))]
	public async Task Each_proxy_sends_the_declared_Api_Operation_pair(string api,
		string operation,
		Func<IIntegrationContext, RecordingHostInvoker, Task> exercise)
	{
		await exercise(_context, _invoker);

		Assert.Multiple(() =>
		{
			Assert.That(_invoker.LastApi, Is.EqualTo(api));
			Assert.That(_invoker.LastOperation, Is.EqualTo(operation));
		});
	}

	[Test]
	public async Task Action_interactions_send_the_declared_Api_Operation_pair()
	{
		var interactions = new RemoteActionInteractions(_invoker, Serilog.Core.Logger.None, "correlation-1");

		interactions.RequestItemPicker(null, "i1", MusicPlayerCatalogItemKind.Track);
		await _invoker.WaitForCallAsync();
		Assert.Multiple(() =>
		{
			Assert.That(_invoker.LastApi, Is.EqualTo(HostApis.ActionInteractions));
			Assert.That(_invoker.LastOperation, Is.EqualTo(HostOperations.ActionInteractions.RequestItemPicker));
		});

		interactions.RequestDevicePicker(null, "i1", startPlayback: false);
		await _invoker.WaitForCallAsync();
		Assert.Multiple(() =>
		{
			Assert.That(_invoker.LastApi, Is.EqualTo(HostApis.ActionInteractions));
			Assert.That(_invoker.LastOperation, Is.EqualTo(HostOperations.ActionInteractions.RequestDevicePicker));
		});
	}

	[Test]
	public async Task Device_registrations_send_the_declared_Api_Operation_pair()
	{
		var devices = new RemoteDeviceProviderContext(_invoker);
		var descriptor = new DeviceDescriptor("SERIAL-1", "Deck");

		var expected = new (Func<Task> Call, string Operation)[]
		{
			(() => devices.RegisterDeviceAsync(descriptor), HostOperations.Devices.Register),
			(() => devices.UpdateDeviceAsync(descriptor), HostOperations.Devices.Update),
			(() => devices.SetDevicePresenceAsync("SERIAL-1", DevicePresence.Offline),
				HostOperations.Devices.Presence),
			(() => devices.UnregisterDeviceAsync("SERIAL-1"), HostOperations.Devices.Unregister)
		};

		foreach (var (call, operation) in expected)
		{
			await call();

			Assert.Multiple(() =>
			{
				Assert.That(_invoker.LastApi, Is.EqualTo(HostApis.Devices));
				Assert.That(_invoker.LastOperation, Is.EqualTo(operation));
			});
		}
	}

	[Test]
	public async Task Layout_registrations_send_the_declared_Api_Operation_pair()
	{
		var layouts = new RemoteLayoutProviderContext(_invoker);
		var descriptor = new LayoutDescriptor("xl", "Stream Deck XL", []);

		var expected = new (Func<Task> Call, string Operation)[]
		{
			(() => layouts.RegisterLayoutAsync(descriptor), HostOperations.Layouts.Register),
			(() => layouts.UnregisterLayoutAsync("xl"), HostOperations.Layouts.Unregister)
		};

		foreach (var (call, operation) in expected)
		{
			await call();

			Assert.Multiple(() =>
			{
				Assert.That(_invoker.LastApi, Is.EqualTo(HostApis.Layouts));
				Assert.That(_invoker.LastOperation, Is.EqualTo(operation));
			});
		}
	}

	[Test]
	public async Task FolderView_registrations_send_the_declared_Api_Operation_pair()
	{
		var folderViews = new RemoteFolderViewProviderContext(_invoker);
		var descriptor = new FolderViewDescriptor("dashboard", LocalizedText.FromLiteral("Dashboard"));

		var expected = new (Func<Task> Call, string Operation)[]
		{
			(() => folderViews.RegisterFolderViewAsync(descriptor), HostOperations.FolderViews.Register),
			(() => folderViews.UnregisterFolderViewAsync("dashboard"), HostOperations.FolderViews.Unregister)
		};

		foreach (var (call, operation) in expected)
		{
			await call();

			Assert.Multiple(() =>
			{
				Assert.That(_invoker.LastApi, Is.EqualTo(HostApis.FolderViews));
				Assert.That(_invoker.LastOperation, Is.EqualTo(operation));
			});
		}
	}

	[Test]
	public async Task WidgetType_registrations_send_the_declared_Api_Operation_pair()
	{
		var widgetTypes = new RemoteWidgetTypeProviderContext(_invoker);
		var descriptor = new WidgetTypeDescriptor("gauge", LocalizedText.FromLiteral("Gauge"));

		var expected = new (Func<Task> Call, string Operation)[]
		{
			(() => widgetTypes.RegisterWidgetTypeAsync(descriptor), HostOperations.WidgetTypes.Register),
			(() => widgetTypes.UnregisterWidgetTypeAsync("gauge"), HostOperations.WidgetTypes.Unregister)
		};

		foreach (var (call, operation) in expected)
		{
			await call();

			Assert.Multiple(() =>
			{
				Assert.That(_invoker.LastApi, Is.EqualTo(HostApis.WidgetTypes));
				Assert.That(_invoker.LastOperation, Is.EqualTo(operation));
			});
		}
	}

	/// <summary>
	/// Opening a modal answers on its own result; the user's answer arrives separately, so this covers
	/// only the outgoing pair - the return trip is <c>UiCapabilityHandlerTests</c>' <c>modal.result</c>.
	/// </summary>
	[Test]
	public async Task Showing_a_modal_sends_the_declared_Api_Operation_pair()
	{
		var ui = new RemoteUiInteractions(_invoker, new ModalResultStore(), Serilog.Core.Logger.None, "correlation-1");

		await ui.ShowModalAsync(originClientId: "client-1", new ModalDefinition { ViewId = "device-picker" });

		Assert.Multiple(() =>
		{
			Assert.That(_invoker.LastApi, Is.EqualTo(HostApis.ActionInteractions));
			Assert.That(_invoker.LastOperation, Is.EqualTo(HostOperations.ActionInteractions.ShowModal));
		});
	}

	/// <summary>Every operation <see cref="HostOperations" /> declares for every <see cref="HostApis" />
	/// member is covered above - this proves the coverage itself is complete, so a future API added to
	/// the vocabulary without a matching case here fails loudly rather than silently going untested.</summary>
	[Test]
	public void Every_declared_HostApis_operation_has_a_covered_case()
	{
		var covered = ApiOperationCases()
			.Select(data => ((string)data.Arguments[0]!, (string)data.Arguments[1]!))
			.ToHashSet();

		// action-interactions is covered by Action_interactions_send_the_declared_Api_Operation_pair
		// instead - its members are not reachable through IIntegrationContext, so it has no place in
		// the context-driven table above.
		covered.Add((HostApis.ActionInteractions, HostOperations.ActionInteractions.RequestItemPicker));
		covered.Add((HostApis.ActionInteractions, HostOperations.ActionInteractions.RequestDevicePicker));
		covered.Add((HostApis.ActionInteractions, HostOperations.ActionInteractions.ShowModal));

		// ui is driven by a live session rather than by IIntegrationContext, so it is covered by
		// UiCapabilityHandlerTests instead - one test per operation, asserting the bytes as well as the
		// pair, which this table's recording invoker cannot see.
		covered.Add((HostApis.Ui, HostOperations.Ui.Snapshot));
		covered.Add((HostApis.Ui, HostOperations.Ui.Patch));
		covered.Add((HostApis.Ui, HostOperations.Ui.Fault));

		// devices is handed to a device provider rather than reached through IIntegrationContext, so it
		// is covered by Device_registrations_send_the_declared_Api_Operation_pair above.
		foreach (var operation in HostOperations.Devices.All)
		{
			covered.Add((HostApis.Devices, operation));
		}

		// variable-values is handed to a provider through IVariableSink rather than reached through
		// IIntegrationContext, so it is covered by RemoteVariableSinkTests instead -
		// Publishing_sends_the_declared_Api_Operation_pair and
		// Invalidating_the_catalog_sends_the_declared_Api_Operation_pair.
		foreach (var operation in HostOperations.VariableValues.All)
		{
			covered.Add((HostApis.VariableValues, operation));
		}

		// layouts is handed to a layout provider rather than reached through IIntegrationContext, so it
		// is covered by Layout_registrations_send_the_declared_Api_Operation_pair above.
		foreach (var operation in HostOperations.Layouts.All)
		{
			covered.Add((HostApis.Layouts, operation));
		}

		// folder-views is handed to a folder view provider rather than reached through
		// IIntegrationContext, so it is covered by
		// FolderView_registrations_send_the_declared_Api_Operation_pair above.
		foreach (var operation in HostOperations.FolderViews.All)
		{
			covered.Add((HostApis.FolderViews, operation));
		}

		// widget-types is handed to a widget type provider rather than reached through
		// IIntegrationContext, so it is covered by
		// WidgetType_registrations_send_the_declared_Api_Operation_pair above.
		foreach (var operation in HostOperations.WidgetTypes.All)
		{
			covered.Add((HostApis.WidgetTypes, operation));
		}

		var declared = HostApis.All.SelectMany(api => HostOperations.For(api).Select(operation => (api, operation)));

		Assert.That(declared, Is.SubsetOf(covered));
	}

	private static TestCaseData Case(string api,
		string operation,
		Func<IIntegrationContext, RecordingHostInvoker, Task> exercise)
		=> new TestCaseData(api, operation, exercise).SetName($"{{m}}({api}.{operation})");

	// ---- HostStateCache: empty/false before a push, pushed values after ----------------------------

	[Test]
	public void HostStateCache_returns_empty_and_false_before_any_push()
	{
		var stateCache = new HostStateCache(new PluginConnectionState());

		Assert.Multiple(() =>
		{
			Assert.That(stateCache.GetList<Script>(HostApis.Scripts), Is.Empty);
			Assert.That(stateCache.GetList<WidgetTargetInfo>(HostApis.Widgets), Is.Empty);
			Assert.That(stateCache.Get<DeckStateDto>(HostApis.Deck), Is.Null);
		});
	}

	[Test]
	public void HostStateCache_returns_the_pushed_values_after_a_push()
	{
		var stateCache = new HostStateCache(new PluginConnectionState());

		stateCache.Apply(StatePush(HostApis.Scripts, new List<Script> { new() { Id = "s1", Name = "Script 1" } }));
		stateCache.Apply(StatePush(HostApis.Widgets,
			new List<WidgetTargetInfo>
			{
				new() { Id = "w1", Label = "Widget 1", Location = "Profile / Folder", Type = "action-button" }
			}));
		stateCache.Apply(StatePush(HostApis.Deck,
			new DeckStateDto
			{
				Folders = [new Sdk.Decks.DeckFolder { Id = "f1", Label = "Folder 1" }],
				Profiles = [new Sdk.Decks.DeckProfile { Id = "p1", Label = "Profile 1" }]
			}));

		Assert.Multiple(() =>
		{
			Assert.That(stateCache.GetList<Script>(HostApis.Scripts).Select(s => s.Id), Is.EqualTo(new[] { "s1" }));
			Assert.That(stateCache.GetList<WidgetTargetInfo>(HostApis.Widgets).Select(w => w.Id),
				Is.EqualTo(new[] { "w1" }));
			Assert.That(stateCache.Get<DeckStateDto>(HostApis.Deck)!.Folders.Select(f => f.Id),
				Is.EqualTo(new[] { "f1" }));
			Assert.That(stateCache.Get<DeckStateDto>(HostApis.Deck)!.Profiles.Select(p => p.Id),
				Is.EqualTo(new[] { "p1" }));
		});
	}

	[Test]
	public void RemoteDeckNavigator_ScriptApi_WidgetApi_read_through_HostStateCache()
	{
		var invoker = new RecordingHostInvoker();
		var stateCache = new HostStateCache(new PluginConnectionState());
		var deck = new RemoteDeckNavigator(invoker, stateCache);
		var scripts = new RemoteScriptApi(invoker, stateCache);
		var widgets = new RemoteWidgetApi(invoker, new PluginConnectionState(), stateCache);

		Assert.Multiple(() =>
		{
			Assert.That(deck.GetFolders(), Is.Empty);
			Assert.That(deck.GetProfiles(), Is.Empty);
			Assert.That(scripts.GetScripts(), Is.Empty);
			Assert.That(widgets.GetWidgets(), Is.Empty);
			Assert.That(widgets.Exists("anything"), Is.False);
		});

		stateCache.Apply(StatePush(HostApis.Deck,
			new DeckStateDto { Folders = [new Sdk.Decks.DeckFolder { Id = "f1", Label = "Folder 1" }] }));
		stateCache.Apply(StatePush(HostApis.Scripts, new List<Script> { new() { Id = "s1", Name = "Script 1" } }));
		stateCache.Apply(StatePush(HostApis.Widgets,
			new List<WidgetTargetInfo>
			{
				new() { Id = "w1", Label = "Widget 1", Location = "Profile / Folder", Type = "action-button" }
			}));

		Assert.Multiple(() =>
		{
			Assert.That(deck.GetFolders().Select(f => f.Id), Is.EqualTo(new[] { "f1" }));
			Assert.That(scripts.GetScripts().Select(s => s.Id), Is.EqualTo(new[] { "s1" }));
			Assert.That(widgets.Exists("w1"), Is.True);
		});
	}

	private static ProtocolEnvelope StatePush(string api, object? data)
		=> new()
		{
			Type = MessageTypes.HostState,
			Id = Guid.NewGuid().ToString(),
			Payload = JsonSerializer.SerializeToElement(new HostStatePayload
				{
					Api = api,
					Data = data is null ? null : JsonSerializer.SerializeToElement(data, PluginProtocolJson.Options)
				},
				PluginProtocolJson.Options)
		};

	[Test]
	public async Task Scripts_run_sends_the_supplied_inputs_in_the_host_invoke_payload()
	{
		var inputs = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["scene"] = "Live",
			["volume"] = 42d
		};

		await _context.Scripts.RunAsync("s1", inputs, "c9");

		var arguments = (ScriptsRunArguments)_invoker.LastArguments!;
		Assert.Multiple(() =>
		{
			Assert.That(arguments.ScriptId, Is.EqualTo("s1"));
			Assert.That(arguments.OriginClientId, Is.EqualTo("c9"));
			Assert.That(arguments.Inputs, Is.EqualTo(inputs));
		});
	}

	[Test]
	public async Task Scripts_run_without_inputs_sends_none()
	{
		await _context.Scripts.RunAsync("s1");

		Assert.That(((ScriptsRunArguments)_invoker.LastArguments!).Inputs, Is.Null);
	}

	/// <summary>Records the last <c>Api</c>/<c>Operation</c> pair an <see cref="IHostInvoker" /> call
	/// site sent, and always answers with an empty success - the unit-level stand-in this file's tests
	/// use instead of a real wire.</summary>
	public sealed class RecordingHostInvoker : IHostInvoker, IDisposable
	{
		private readonly SemaphoreSlim _called = new(0);

		public string? LastApi { get; private set; }

		public string? LastOperation { get; private set; }

		public JsonElement? NextResult { get; set; }

		public object? LastArguments { get; private set; }

		public Task<JsonElement?> InvokeAsync(string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			LastApi = api;
			LastOperation = operation;
			LastArguments = arguments;
			var result = NextResult;
			NextResult = null;
			_called.Release();
			return Task.FromResult(result);
		}

		public bool TryComplete(ProtocolEnvelope result) => false;

		/// <summary>Waits for a fire-and-forget call (Notify/Dismiss/the ActionInteractions members) to
		/// actually reach <see cref="InvokeAsync" /> on its background task, without a sleep.</summary>
		public Task WaitForCallAsync() => _called.WaitAsync(TimeSpan.FromSeconds(5));

		public void Dispose() => _called.Dispose();
	}
}
