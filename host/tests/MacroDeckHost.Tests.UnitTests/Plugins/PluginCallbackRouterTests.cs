using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Callbacks.Ui;
using MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.FolderViews;
using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Capabilities.ScreenSaverProvider;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using Microsoft.Extensions.DependencyInjection;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class PluginCallbackRouterTests
{
	private FakeVariableService _variableService = null!;
	private FakeActionInteractions _actionInteractions = null!;
	private FakeInvoker _invoker = null!;
	private ManualTimeProvider _time = null!;

	private static PluginCallbackRouter Router(
		FakeVariableService variableService,
		FakeActionInteractions actionInteractions,
		FakeInvoker invoker,
		HostCallbackThrottle throttle,
		FakeHostLockState? lockState = null,
		FakeScriptApi? scriptApi = null,
		FakePluginDeviceRegistry? deviceRegistry = null,
		ScreenSaverRegistry? screenSavers = null,
		WidgetTypeRegistry? widgetTypes = null,
		IPluginUiResources? uiResources = null,
		UiResourceCallbackThrottle? uiResourceThrottle = null)
	{
		var services = new ServiceCollection();
		services.AddSingleton<IVariableService>(variableService);
		var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		return new PluginCallbackRouter(new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None),
			invoker,
			scopeFactory,
			new FakeNotificationStore(),
			new FakeDeckNavigator(),
			scriptApi ?? new FakeScriptApi(),
			new FakeWidgetApi(),
			new FakeWidgetIconInvalidator(),
			new FakeUserVariableApi(),
			actionInteractions,
			new NoOpUiSessionSink(),
			deviceRegistry ?? new FakePluginDeviceRegistry(),
			new LayoutRegistry(new RecordingMediator()),
			new FolderViewRegistry(new RecordingMediator()),
			widgetTypes ?? new WidgetTypeRegistry(new RecordingMediator()),
			screenSavers ?? new ScreenSaverRegistry(new RecordingMediator()),
			new ModalInteractionCoordinator(TimeProvider.System),
			new RecordingUiTransport(),
			throttle,
			lockState ?? new FakeHostLockState(),
			Serilog.Core.Logger.None,
			uiResources: uiResources,
			uiResourceThrottle: uiResourceThrottle);
	}

	[SetUp]
	public void SetUp()
	{
		_variableService = new FakeVariableService();
		_actionInteractions = new FakeActionInteractions();
		_invoker = new FakeInvoker();
		_time = new ManualTimeProvider();
	}

	private PluginCallbackRouter Router(
		int throttleCapacity = 100,
		FakeHostLockState? lockState = null,
		FakeScriptApi? scriptApi = null,
		FakePluginDeviceRegistry? deviceRegistry = null,
		ScreenSaverRegistry? screenSavers = null,
		WidgetTypeRegistry? widgetTypes = null)
		=> Router(_variableService,
			_actionInteractions,
			_invoker,
			new HostCallbackThrottle(_time, throttleCapacity, refillPerSecond: 100),
			lockState,
			scriptApi,
			deviceRegistry,
			screenSavers,
			widgetTypes);

	// ---- ui resources ---------------------------------------------------------------------------

	private PluginCallbackRouter UiResourceRouter(RecordingUiResources resources,
		int sharedCapacity = 100,
		int resourceCapacity = 100)
		=> Router(_variableService,
			_actionInteractions,
			_invoker,
			new HostCallbackThrottle(_time, sharedCapacity, refillPerSecond: 0),
			uiResources: resources,
			uiResourceThrottle: new UiResourceCallbackThrottle(_time, resourceCapacity, refillPerSecond: 0));

	private static HostInvokePayload RegisterResource(string name = "photo")
		=> new()
		{
			Api = HostApis.Ui,
			Operation = HostOperations.Ui.RegisterResource,
			Arguments = Arg(new UiRegisterResourceArguments
				{ Name = name, ContentHash = "sha256:abc", MediaType = "image/png" })
		};

	[Test]
	public async Task A_resource_registration_is_made_for_the_calling_plugins_session_and_answers_the_handle()
	{
		var resources = new RecordingUiResources();
		var router = UiResourceRouter(resources);

		var result = await router.RouteAsync("plugin.a", "session-1", "c1", RegisterResource(), CancellationToken.None);

		var answer = result.Data?.Deserialize<UiRegisterResourceResult>(PluginProtocolJson.Options);
		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.Null);
			Assert.That(resources.Calls, Is.EqualTo(new[] { ("plugin.a", "session-1", "photo") }));
			Assert.That(answer?.Resource?.ResourceId, Is.EqualTo("plugin-x.photo"));
			Assert.That(answer?.Resource?.ContentHash, Is.EqualTo("sha256:abc"));
		});
	}

	[Test]
	public async Task A_resource_registration_without_a_session_is_refused_as_retryable()
	{
		var resources = new RecordingUiResources();
		var router = UiResourceRouter(resources);

		var result = await router.RouteAsync("plugin.a", "c1", RegisterResource(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.SessionNotFound));
			Assert.That(result.Error?.Retryable, Is.True);
			Assert.That(resources.Calls, Is.Empty);
		});
	}

	[TestCase(PluginUiResourceOutcome.QuotaExceeded, ProtocolErrorCodes.UiResourceQuotaExceeded)]
	[TestCase(PluginUiResourceOutcome.InvalidName, ProtocolErrorCodes.InvalidPayload)]
	[TestCase(PluginUiResourceOutcome.MediaTypeMismatch, ProtocolErrorCodes.InvalidPayload)]
	[TestCase(PluginUiResourceOutcome.SessionNotCurrent, ProtocolErrorCodes.SessionNotFound)]
	public async Task A_refused_registration_answers_its_protocol_error(PluginUiResourceOutcome outcome, string code)
	{
		var router = UiResourceRouter(new RecordingUiResources { Outcome = outcome });

		var result = await router.RouteAsync("plugin.a", "session-1", "c1", RegisterResource(), CancellationToken.None);

		Assert.That(result.Error?.Code, Is.EqualTo(code));
	}

	[Test]
	public async Task Resource_registrations_do_not_spend_the_plugins_shared_callback_budget()
	{
		var router = UiResourceRouter(new RecordingUiResources(), sharedCapacity: 1);

		for (var index = 0; index < 20; index++)
		{
			var result = await router.RouteAsync("plugin.a",
				"session-1",
				"c" + index,
				RegisterResource($"photo-{index}"),
				CancellationToken.None);
			Assert.That(result.Error, Is.Null);
		}

		Assert.That(router.Admit("plugin.a", new HostInvokePayload
			{
				Api = HostApis.Variables, Operation = HostOperations.Variables.List
			}),
			Is.Null);
	}

	[Test]
	public async Task Resource_registrations_beyond_their_own_budget_are_rate_limited()
	{
		var router = UiResourceRouter(new RecordingUiResources(), resourceCapacity: 2);

		await router.RouteAsync("plugin.a", "session-1", "c1", RegisterResource(), CancellationToken.None);
		await router.RouteAsync("plugin.a", "session-1", "c2", RegisterResource(), CancellationToken.None);
		var third = await router.RouteAsync("plugin.a", "session-1", "c3", RegisterResource(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(third.Error?.Code, Is.EqualTo(ProtocolErrorCodes.RateLimited));
			Assert.That(third.Error?.Retryable, Is.True);
		});
	}

	[Test]
	public async Task A_burst_of_ui_patches_is_still_not_throttled()
	{
		var router = UiResourceRouter(new RecordingUiResources(), sharedCapacity: 1, resourceCapacity: 1);

		for (var index = 0; index < 100; index++)
		{
			var result = await router.RouteAsync("plugin.a",
				"session-1",
				"c" + index,
				new HostInvokePayload
				{
					Api = HostApis.Ui,
					Operation = HostOperations.Ui.Patch,
					Arguments = Arg(new UiPatchArguments
						{ SessionId = "ui-1", Patch = JsonSerializer.SerializeToElement(new { }) })
				},
				CancellationToken.None);
			Assert.That(result.Error?.Code, Is.Not.EqualTo(ProtocolErrorCodes.RateLimited));
		}
	}

	[Test]
	public async Task Removing_a_resource_is_made_for_the_calling_plugins_session()
	{
		var resources = new RecordingUiResources();
		var router = UiResourceRouter(resources);

		var result = await router.RouteAsync("plugin.a",
			"session-1",
			"c1",
			new HostInvokePayload
			{
				Api = HostApis.Ui,
				Operation = HostOperations.Ui.RemoveResource,
				Arguments = Arg(new UiRemoveResourceArguments { Name = "photo" })
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.Null);
			Assert.That(resources.Removed, Is.EqualTo(new[] { ("plugin.a", "session-1", "photo") }));
		});
	}

	private sealed class RecordingUiResources : IPluginUiResources
	{
		public List<(string PluginId, string SessionId, string Name)> Calls { get; } = [];

		public List<(string PluginId, string SessionId, string Name)> Removed { get; } = [];

		public PluginUiResourceOutcome Outcome { get; init; } = PluginUiResourceOutcome.Registered;

		public PluginUiResourceResult Register(string pluginId,
			string sessionId,
			string name,
			string contentHash,
			string mediaType)
		{
			Calls.Add((pluginId, sessionId, name));

			return Outcome == PluginUiResourceOutcome.Registered
				? new PluginUiResourceResult(Outcome, new MacroDeck.Ui.Model.Resources.UiResource
				{
					ResourceId = "plugin-x." + name, ContentHash = contentHash, MediaType = mediaType, ByteLength = 3
				})
				: new PluginUiResourceResult(Outcome);
		}

		public PluginUiResourceResult Remove(string pluginId, string sessionId, string name)
		{
			Removed.Add((pluginId, sessionId, name));
			return new PluginUiResourceResult(PluginUiResourceOutcome.Removed);
		}
	}

	// ---- security: ownership containment -----------------------------------------------------

	[Test]
	public async Task A_plugin_registers_devices_in_its_own_name_only()
	{
		var registry = new FakePluginDeviceRegistry();
		var router = Router(deviceRegistry: registry);

		var result = await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload
			{
				Api = HostApis.Devices,
				Operation = HostOperations.Devices.Register,
				Arguments = Arg(new DevicesRegisterArguments
				{
					Device = new DeviceDescriptorDto { Id = "SERIAL-1", Name = "Deck" }
				})
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.Null);
			Assert.That(registry.AssignedIdOf("plugin.a", "SERIAL-1"), Is.Not.Null);
			Assert.That(registry.AssignedIdOf("plugin.b", "SERIAL-1"), Is.Null);
		});
	}

	[Test]
	public async Task A_plugin_registers_and_withdraws_screensavers_in_its_own_name_only()
	{
		var screenSavers = new ScreenSaverRegistry(new RecordingMediator());
		var router = Router(screenSavers: screenSavers);

		var registered = await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload
			{
				Api = HostApis.ScreenSavers,
				Operation = HostOperations.ScreenSavers.Register,
				Arguments = Arg(new ScreenSaversRegisterArguments
				{
					ScreenSaver = new ScreenSaverDescriptorDto { Id = "photos", Name = LocalizedText.FromLiteral("Photos") }
				})
			},
			CancellationToken.None);
		var id = registered.Data!.Value.Deserialize<ScreenSaversRegisterResult>(PluginProtocolJson.Options)!;

		await router.RouteAsync("plugin.b",
			"c2",
			new HostInvokePayload
			{
				Api = HostApis.ScreenSavers,
				Operation = HostOperations.ScreenSavers.Unregister,
				Arguments = Arg(new ScreenSaversUnregisterArguments { ScreenSaverId = "photos" })
			},
			CancellationToken.None);
		var stillThere = screenSavers.TryResolve(id.ScreenSaverId, out _);

		await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload
			{
				Api = HostApis.ScreenSavers,
				Operation = HostOperations.ScreenSavers.Unregister,
				Arguments = Arg(new ScreenSaversUnregisterArguments { ScreenSaverId = "photos" })
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(registered.Error, Is.Null);
			Assert.That(id.ScreenSaverId, Is.EqualTo("plugin.a::photos"));
			Assert.That(stillThere, Is.True, "another plugin must not withdraw it");
			Assert.That(screenSavers.TryResolve(id.ScreenSaverId, out _), Is.False);
		});
	}

	[TestCase("""{"widgetType":{"id":"panel","name":"Panel"}}""", false)]
	[TestCase("""{"widgetType":{"id":"panel","name":"Panel","supportsFlows":true}}""", true)]
	public async Task A_widget_type_runs_its_flows_only_when_its_registration_says_so(
		string arguments,
		bool expected)
	{
		var widgetTypes = new WidgetTypeRegistry(new RecordingMediator());
		var router = Router(widgetTypes: widgetTypes);

		var registered = await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload
			{
				Api = HostApis.WidgetTypes,
				Operation = HostOperations.WidgetTypes.Register,
				Arguments = JsonDocument.Parse(arguments).RootElement.Clone()
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(registered.Error, Is.Null);
			Assert.That(widgetTypes.TryResolve("plugin.a::panel", out var entry), Is.True);
			Assert.That(entry.Descriptor.SupportsFlows, Is.EqualTo(expected));
		});
	}

	[TestCase("""{"widgetType":{"id":"panel","name":"Panel"}}""", new int[0])]
	[TestCase("""{"widgetType":{"id":"panel","name":"Panel","appearanceProperties":[0,1]}}""", new[] { 0, 1 })]
	public async Task A_widget_type_declares_its_appearance_properties_only_when_its_registration_lists_them(
		string arguments,
		int[] expected)
	{
		var widgetTypes = new WidgetTypeRegistry(new RecordingMediator());
		var router = Router(widgetTypes: widgetTypes);

		var registered = await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload
			{
				Api = HostApis.WidgetTypes,
				Operation = HostOperations.WidgetTypes.Register,
				Arguments = JsonDocument.Parse(arguments).RootElement.Clone()
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(registered.Error, Is.Null);
			Assert.That(widgetTypes.TryResolve("plugin.a::panel", out var entry), Is.True);
			Assert.That(entry.Descriptor.AppearanceProperties?.Select(property => (int)property) ?? [],
				Is.EqualTo(expected));
		});
	}

	[Test]
	public async Task A_device_registered_without_an_id_is_rejected_as_an_invalid_payload()
	{
		var router = Router();

		var result = await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload
			{
				Api = HostApis.Devices,
				Operation = HostOperations.Devices.Register,
				Arguments = Arg(new DevicesRegisterArguments
				{
					Device = new DeviceDescriptorDto { Id = string.Empty, Name = "Deck" }
				})
			},
			CancellationToken.None);

		Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
	}

	[Test]
	public async Task A_plugin_cannot_take_another_plugins_device_offline()
	{
		var registry = new FakePluginDeviceRegistry();
		var router = Router(deviceRegistry: registry);

		await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload
			{
				Api = HostApis.Devices,
				Operation = HostOperations.Devices.Register,
				Arguments = Arg(new DevicesRegisterArguments
				{
					Device = new DeviceDescriptorDto { Id = "SERIAL-1", Name = "Deck" }
				})
			},
			CancellationToken.None);

		await router.RouteAsync("plugin.b",
			"c2",
			new HostInvokePayload
			{
				Api = HostApis.Devices,
				Operation = HostOperations.Devices.Unregister,
				Arguments = Arg(new DevicesUnregisterArguments { DeviceId = "SERIAL-1" })
			},
			CancellationToken.None);

		Assert.That(registry.IsOnline("plugin.a", "SERIAL-1"), Is.True);
	}

	[Test]
	public async Task A_plugin_cannot_read_another_plugins_variable_by_name()
	{
		var router = Router();

		await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload
			{
				Api = HostApis.Variables,
				Operation = HostOperations.Variables.Create,
				Arguments = Arg(new VariablesCreateArguments
					{ Name = "secret", Type = "Text", InitialValue = Arg("shh") })
			},
			CancellationToken.None);

		var fromOtherPlugin = await router.RouteAsync("plugin.b",
			"c2",
			new HostInvokePayload
			{
				Api = HostApis.Variables,
				Operation = HostOperations.Variables.Get,
				Arguments = Arg(new VariablesGetArguments { Name = "secret" })
			},
			CancellationToken.None);

		Assert.That(fromOtherPlugin.Error, Is.Null, "an unknown-to-this-plugin name is a null result, not an error");
		Assert.That(fromOtherPlugin.Data, Is.Null, "the other plugin's variable is invisible, not merely unresolved");
	}

	[Test]
	public async Task A_plugin_cannot_list_another_plugins_variables()
	{
		var router = Router();

		await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload
			{
				Api = HostApis.Variables,
				Operation = HostOperations.Variables.Create,
				Arguments = Arg(new VariablesCreateArguments { Name = "a-only", Type = "Text" })
			},
			CancellationToken.None);

		var listedByB = await router.RouteAsync("plugin.b",
			"c2",
			new HostInvokePayload { Api = HostApis.Variables, Operation = HostOperations.Variables.List },
			CancellationToken.None);

		var names = listedByB.Data!.Value.Deserialize<List<VariableHandle>>(PluginProtocolJson.Options)!;
		Assert.That(names, Is.Empty);
	}

	[Test]
	public async Task A_plugin_cannot_overwrite_another_plugins_variable_by_id()
	{
		var router = Router();

		var created = await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload
			{
				Api = HostApis.Variables,
				Operation = HostOperations.Variables.Create,
				Arguments = Arg(new VariablesCreateArguments { Name = "owned-by-a", Type = "Text" })
			},
			CancellationToken.None);

		var handle = created.Data!.Value.Deserialize<VariableHandle>(PluginProtocolJson.Options)!;

		var attackerWrite = await router.RouteAsync("plugin.b",
			"c2",
			new HostInvokePayload
			{
				Api = HostApis.Variables,
				Operation = HostOperations.Variables.Set,
				Arguments = Arg(new VariablesSetArguments { VariableId = handle.Id, Value = Arg("pwned") })
			},
			CancellationToken.None);

		Assert.That(attackerWrite.Error, Is.Not.Null);
		Assert.That(_variableService.ValueOf(handle.Id), Is.Not.EqualTo("pwned"));
	}


	[Test]
	public async Task An_unknown_api_is_capability_unsupported()
	{
		var router = Router();

		var result = await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload { Api = "not-a-real-api", Operation = "whatever" },
			CancellationToken.None);

		Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
	}

	[Test]
	public async Task An_unknown_operation_on_a_known_api_is_capability_unsupported()
	{
		var router = Router();

		var result = await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload { Api = HostApis.Variables, Operation = "not-a-real-operation" },
			CancellationToken.None);

		Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
	}


	[Test]
	public async Task Exceeding_the_throttle_yields_a_retryable_rate_limited()
	{
		var router = Router(_variableService,
			_actionInteractions,
			_invoker,
			new HostCallbackThrottle(_time, capacity: 1, refillPerSecond: 0));

		var first = await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload { Api = HostApis.Variables, Operation = HostOperations.Variables.List },
			CancellationToken.None);
		var second = await router.RouteAsync("plugin.a",
			"c2",
			new HostInvokePayload { Api = HostApis.Variables, Operation = HostOperations.Variables.List },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(first.Error, Is.Null);
			Assert.That(second.Error!.Code, Is.EqualTo(ProtocolErrorCodes.RateLimited));
			Assert.That(second.Error!.Retryable, Is.True);
		});
	}


	[Test]
	public async Task A_picker_request_outside_a_live_execute_of_that_plugin_is_refused()
	{
		var router = Router();
		_invoker.LiveExecuteCorrelations.Clear();

		var result = await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload
			{
				Api = HostApis.ActionInteractions,
				Operation = HostOperations.ActionInteractions.RequestItemPicker,
				Arguments = Arg(new ActionInteractionsRequestItemPickerArguments
				{
					ExecuteCorrelationId = "forged-correlation",
					InstanceId = "instance-1",
					Kind = nameof(MusicPlayerCatalogItemKind.Track)
				})
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(_actionInteractions.ItemPickerRequests, Is.Empty);
		});
	}

	[Test]
	public async Task A_picker_request_inside_a_live_execute_of_that_plugin_is_honoured()
	{
		var router = Router();
		_invoker.LiveExecuteCorrelations.Add(("plugin.a", "real-correlation"));

		var result = await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload
			{
				Api = HostApis.ActionInteractions,
				Operation = HostOperations.ActionInteractions.RequestItemPicker,
				Arguments = Arg(new ActionInteractionsRequestItemPickerArguments
				{
					ExecuteCorrelationId = "real-correlation",
					InstanceId = "instance-1",
					Kind = nameof(MusicPlayerCatalogItemKind.Track)
				})
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.Null);
			Assert.That(_actionInteractions.ItemPickerRequests, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_picker_request_naming_a_live_execute_of_a_different_plugin_is_refused()
	{
		var router = Router();
		_invoker.LiveExecuteCorrelations.Add(("plugin.other", "someone-elses-correlation"));

		var result = await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload
			{
				Api = HostApis.ActionInteractions,
				Operation = HostOperations.ActionInteractions.RequestItemPicker,
				Arguments = Arg(new ActionInteractionsRequestItemPickerArguments
				{
					ExecuteCorrelationId = "someone-elses-correlation",
					InstanceId = "instance-1",
					Kind = nameof(MusicPlayerCatalogItemKind.Track)
				})
			},
			CancellationToken.None);

		Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
	}

	// ---- scripts -------------------------------------------------------------------------------

	[Test]
	public async Task Scripts_run_carries_the_supplied_inputs_through_to_the_api()
	{
		var scripts = new FakeScriptApi();
		var router = Router(scriptApi: scripts);

		var result = await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload
			{
				Api = HostApis.Scripts,
				Operation = HostOperations.Scripts.Run,
				Arguments = JsonDocument
					.Parse("""
						   {"scriptId":"s1","originClientId":"c9","inputs":{"scene":"Live","volume":42,"muted":true}}
						   """)
					.RootElement.Clone()
			},
			CancellationToken.None);

		var run = scripts.Runs.Single();
		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.Null);
			Assert.That(run.ScriptId, Is.EqualTo("s1"));
			Assert.That(run.OriginClientId, Is.EqualTo("c9"));
			Assert.That(((JsonElement)run.Inputs!["volume"]!).ValueKind, Is.EqualTo(JsonValueKind.Number));
			Assert.That(((JsonElement)run.Inputs["volume"]!).GetDouble(), Is.EqualTo(42d));
			Assert.That(((JsonElement)run.Inputs["scene"]!).GetString(), Is.EqualTo("Live"));
			Assert.That(((JsonElement)run.Inputs["muted"]!).ValueKind, Is.EqualTo(JsonValueKind.True));
		});
	}

	[Test]
	public async Task Scripts_run_forwards_a_plugin_supplied_owner_widget_to_the_script_api()
	{
		var scripts = new FakeScriptApi();
		var router = Router(scriptApi: scripts);

		var result = await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload
			{
				Api = HostApis.Scripts,
				Operation = HostOperations.Scripts.Run,
				Arguments = Arg(new ScriptsRunArguments { ScriptId = "s1", OwnerWidgetId = "w-1" })
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.Null);
			Assert.That(scripts.Runs.Single().OwnerWidgetId, Is.EqualTo("w-1"));
		});
	}

	[Test]
	public async Task Scripts_run_without_an_inputs_field_still_invokes_the_api()
	{
		var scripts = new FakeScriptApi();
		var router = Router(scriptApi: scripts);

		var result = await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload
			{
				Api = HostApis.Scripts,
				Operation = HostOperations.Scripts.Run,
				Arguments = Arg(new ScriptsRunArguments { ScriptId = "s1" })
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.Null);
			Assert.That(scripts.Runs.Single().Inputs, Is.Null);
		});
	}

	// ---- host lock state -----------------------------------------------------------------------

	[Test]
	public async Task Scripts_run_is_refused_while_locked()
	{
		var router = Router(lockState: new FakeHostLockState { IsLocked = true });

		var result = await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload
			{
				Api = HostApis.Scripts,
				Operation = HostOperations.Scripts.Run,
				Arguments = Arg(new ScriptsRunArguments { ScriptId = "s1" })
			},
			CancellationToken.None);

		Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
	}

	[Test]
	public async Task Variables_set_is_refused_while_locked_and_the_variable_is_untouched()
	{
		var router = Router();

		var created = await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload
			{
				Api = HostApis.Variables,
				Operation = HostOperations.Variables.Create,
				Arguments = Arg(new VariablesCreateArguments { Name = "v", Type = "Text", InitialValue = Arg("a") })
			},
			CancellationToken.None);
		var handle = created.Data!.Value.Deserialize<VariableHandle>(PluginProtocolJson.Options)!;

		var lockedRouter = Router(lockState: new FakeHostLockState { IsLocked = true });
		var result = await lockedRouter.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload
			{
				Api = HostApis.Variables,
				Operation = HostOperations.Variables.Set,
				Arguments = Arg(new VariablesSetArguments { VariableId = handle.Id, Value = Arg("b") })
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(_variableService.ValueOf(handle.Id), Is.EqualTo("a"));
		});
	}

	[Test]
	public async Task UserVariables_apply_is_refused_while_locked()
	{
		var router = Router(lockState: new FakeHostLockState { IsLocked = true });

		var result = await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload
			{
				Api = HostApis.UserVariables,
				Operation = HostOperations.UserVariables.Apply,
				Arguments = Arg(new UserVariablesApplyArguments { Name = "v", Operation = "Set", Value = "1" })
			},
			CancellationToken.None);

		Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
	}

	[Test]
	public async Task Deck_change_folder_is_refused_while_locked()
	{
		var router = Router(lockState: new FakeHostLockState { IsLocked = true });

		var result = await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload
			{
				Api = HostApis.Deck,
				Operation = HostOperations.Deck.ChangeFolder,
				Arguments = Arg(new DeckChangeFolderArguments { FolderId = "f1" })
			},
			CancellationToken.None);

		Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
	}

	[Test]
	public async Task Variables_list_a_state_report_still_works_while_locked()
	{
		var router = Router(lockState: new FakeHostLockState { IsLocked = true });

		var result = await router.RouteAsync("plugin.a",
			"c1",
			new HostInvokePayload { Api = HostApis.Variables, Operation = HostOperations.Variables.List },
			CancellationToken.None);

		Assert.That(result.Error, Is.Null);
	}

	private static JsonElement Arg<T>(T value)
		=> JsonSerializer.SerializeToElement(value, PluginProtocolJson.Options);

	private sealed class FakeHostLockState : IHostLockState
	{
		public bool IsSupported { get; init; } = true;

		public bool IsLocked { get; init; }
	}

	private sealed class FakeInvoker : IPluginCapabilityInvoker
	{
		public HashSet<(string PluginId, string CorrelationId)> LiveExecuteCorrelations { get; } = [];

		public Task<JsonElement?> InvokeAsync(string pluginId,
			CapabilityInvokeRequest request,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public bool TryComplete(string pluginId, ProtocolEnvelope result) => false;

		public void AbortAll(string pluginId, ProtocolError reason)
		{
		}

		public bool IsLiveActionExecute(string pluginId, string correlationId)
			=> LiveExecuteCorrelations.Contains((pluginId, correlationId));
	}

	private sealed class FakeActionInteractions : IActionInteractions
	{
		public List<(string? OriginClientId, string InstanceId, MusicPlayerCatalogItemKind Kind)> ItemPickerRequests
		{
			get;
		} = [];

		public void RequestItemPicker(string? originClientId,
			string instanceId,
			MusicPlayerCatalogItemKind kind,
			string? prompt = null)
			=> ItemPickerRequests.Add((originClientId, instanceId, kind));

		public void RequestDevicePicker(string? originClientId,
			string instanceId,
			bool startPlayback,
			string? prompt = null)
		{
		}
	}

	private sealed class FakeNotificationStore : IUserNotificationStore
	{
		public int Capacity => 100;

		public UserNotification? Raise(UserNotificationDraft draft) => null;

		public UserNotification? RaiseIfAbsent(UserNotificationDraft draft) => null;

		public IReadOnlyList<UserNotification> Snapshot() => [];

		public bool UpdateProgress(string dedupeKey, UserNotificationProgress progress) => false;

		public bool DismissByKey(string dedupeKey) => false;

		public bool Dismiss(string id) => false;

		public bool Retire(string dedupeKey) => false;

		public bool DismissAll() => false;

		public event Action? Changed
		{
			add { }
			remove { }
		}
	}

	private sealed class FakeDeckNavigator : IDeckNavigator
	{
		public Task ChangeFolderAsync(string folderId,
			string? originClientId = null,
			CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task ChangeProfileAsync(string profileId,
			string? originClientId = null,
			CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task GoToParentAsync(string? originClientId = null, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task GoBackAsync(string? originClientId = null, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public IReadOnlyList<DeckFolder> GetFolders() => [];

		public IReadOnlyList<DeckProfile> GetProfiles() => [];
	}

	private sealed class FakeScriptApi : IScriptApi
	{
		public List<(string ScriptId, IReadOnlyDictionary<string, object?>? Inputs, string? OriginClientId, string?
			OwnerWidgetId)> Runs { get; } = [];

		public IReadOnlyList<Script> GetScripts() => [];

		public Task<ActionResult> RunAsync(string scriptId,
			IReadOnlyDictionary<string, object?>? inputs = null,
			string? originClientId = null,
			string? ownerWidgetId = null,
			CancellationToken cancellationToken = default)
		{
			Runs.Add((scriptId, inputs, originClientId, ownerWidgetId));
			return ActionResult.SucceededTask;
		}
	}

	private sealed class FakeWidgetApi : IWidgetApi
	{
		public IReadOnlyList<WidgetTargetInfo> GetWidgets() => [];

		public bool Exists(string widgetId) => false;

		public Task<bool> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken = default)
			=> Task.FromResult(false);

		public Task<WidgetStateWriteResult> SetStateAsync(
			string widgetId,
			string stateId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));

		public Task<WidgetStateWriteResult> AdvanceStateAsync(string widgetId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));
	}

	private sealed class FakeWidgetIconInvalidator : IWidgetIconInvalidator
	{
		public List<(string IntegrationId, string ActionId)> Invalidations { get; } = [];

		public void Invalidate(string integrationId, string actionId) => Invalidations.Add((integrationId, actionId));
	}

	private sealed class FakeUserVariableApi : IUserVariableApi
	{
		public Task<UserVariableWriteResult> ApplyAsync(string name,
			string? ownerWidgetId,
			UserVariableOperation operation,
			string? value,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(UserVariableWriteResult.Applied());
	}

	private sealed class FakeVariableService : IVariableService
	{
		private readonly Dictionary<Guid, VariableEntity> _byId = [];

		public string? ValueOf(Guid id) => _byId.TryGetValue(id, out var entity) ? entity.Value : null;

		public Task<IReadOnlyList<VariableEntity>> GetAll()
			=> Task.FromResult<IReadOnlyList<VariableEntity>>([.. _byId.Values]);

		public Task<IReadOnlyList<VariableEntity>> GetByScope(VariableScope scope, string? scopeRefId)
			=> throw new NotSupportedException();

		public Task<VariableEntity?> GetById(Guid id)
			=> Task.FromResult(_byId.GetValueOrDefault(id));

		public Task<VariableEntity?> Resolve(string name, VariableScope contextScope, string? contextScopeRefId)
			=> Task.FromResult(_byId.Values.FirstOrDefault(entity => entity.Name == name));

		public Task<Result<VariableEntity, VariableError>> CreateUserVariable(string name,
			VariableScope scope,
			string? scopeRefId,
			DomainVariableType type,
			object? initialValue,
			int? decimalPlaces)
			=> throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> SetValue(Guid id,
			object? value,
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> UpdateUserVariable(Guid id,
			string? name,
			int? decimalPlaces)
			=> throw new NotSupportedException();

		public Task<Result<VariableError>> DeleteUserVariable(Guid id) => throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> CreateIntegrationVariable(
			string integrationId,
			string name,
			VariableScope scope,
			string? scopeRefId,
			DomainVariableType type,
			object? initialValue,
			int? decimalPlaces,
			string? definitionId = null,
			VariableDeclaration? declaration = null,
			VariableUpdateMode updateMode = VariableUpdateMode.Polled)
		{
			var entity = new VariableEntity
			{
				Id = Guid.CreateVersion7(),
				Name = name,
				Scope = scope,
				ScopeRefId = scopeRefId,
				Type = type,
				Classification = VariableClassification.Integration,
				OwnerIntegrationId = integrationId,
				DefinitionId = definitionId,
				Value = initialValue?.ToString() ?? string.Empty,
				DecimalPlaces = decimalPlaces,
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow,
				Presentation = declaration?.Presentation
			};

			_byId[entity.Id] = entity;
			return Task.FromResult(Result.Ok<VariableEntity, VariableError>(entity));
		}

		public Task<Result<VariableEntity, VariableError>> MaterializeCatalogVariable(string integrationId,
			string resourceId,
			string name,
			Domain.Enums.VariableType type,
			int? decimalPlaces,
			VariableDeclaration? declaration = null) =>
			throw new NotSupportedException();

		public Task<VariableEntity?> GetByDefinition(MacroDeck.Sdk.Identity.QualifiedId definitionId)
			=> throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> ReportIntegrationVariableValue(string integrationId,
			Guid id,
			object? value,
			VariableBounds? bounds = null)
		{
			if (!_byId.TryGetValue(id, out var entity))
			{
				return Task.FromResult(Result.Fail<VariableEntity, VariableError>(VariableError.NotFound));
			}

			if (entity.OwnerIntegrationId != integrationId)
			{
				return Task.FromResult(Result.Fail<VariableEntity, VariableError>(VariableError.NotOwnedByIntegration));
			}

			entity.Value = value?.ToString() ?? string.Empty;
			return Task.FromResult(Result.Ok<VariableEntity, VariableError>(entity));
		}

		public Task<Result<VariableError>> SetIntegrationVariableAvailability(string integrationId,
			Guid id,
			bool available)
			=> throw new NotSupportedException();

		public Task<Result<VariableError>> DeleteIntegrationVariable(string integrationId, Guid id)
		{
			if (!_byId.TryGetValue(id, out var entity))
			{
				return Task.FromResult(Result.Fail<VariableError>(VariableError.NotFound));
			}

			if (entity.OwnerIntegrationId != integrationId)
			{
				return Task.FromResult(Result.Fail<VariableError>(VariableError.NotOwnedByIntegration));
			}

			_byId.Remove(id);
			return Task.FromResult(Result.Ok<VariableError>());
		}

		public Task<IReadOnlyList<VariableEntity>> GetByOwnerIntegration(string integrationId)
			=> Task.FromResult<IReadOnlyList<VariableEntity>>([
				.. _byId.Values.Where(entity => entity.OwnerIntegrationId == integrationId)
			]);

		public Task DeleteByScopeInstance(VariableScope scope, string scopeRefId) => throw new NotSupportedException();

		public Task UpsertWidgetVariable(VariableScope scope,
			string scopeRefId,
			string name,
			DomainVariableType type,
			object? value) => throw new NotSupportedException();

		public Task RemoveWidgetVariable(VariableScope scope, string scopeRefId, string name)
			=> throw new NotSupportedException();
	}
}

internal sealed class NoOpUiSessionSink : IUiSessionSink
{
	public UiSessionIngestResult PublishSnapshot(string providerId, string sessionId, UiRawJson tree)
		=> UiSessionIngestResult.Accept();

	public UiSessionIngestResult PublishPatch(string providerId, string sessionId, UiRawJson patch)
		=> UiSessionIngestResult.Accept();

	public void PublishFault(string providerId, string sessionId, string code, string? message)
	{
	}
}
