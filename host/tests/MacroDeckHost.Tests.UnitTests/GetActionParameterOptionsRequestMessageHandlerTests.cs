using System.Text.Json;
using MacroDeckHost.Application.Actions.Options;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Triggers.Providers;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeck.Sdk.Actions;
using Serilog;
using ActionParameter = MacroDeck.Sdk.Actions.ActionParameter;
using MacroDeck.Localization;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests;

public class GetActionParameterOptionsRequestMessageHandlerTests
{
	private static readonly ILogger _logger = Log.Logger;
	private static readonly string[] _actionButtonOnly = ["ActionButton"];
	private static readonly string[] _bothWidgets = ["button-1", "slider-1"];
	private static readonly string[] _deviceNames = ["MacBook Pro", "Studio PC"];
	private static readonly string[] _serialsWithoutSentinel = ["serial-1"];
	private static readonly string[] _serialsWithSentinel = ["", "serial-1"];

	private static readonly string[] _deviceIds =
		["11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"];

	private sealed class DynamicActionDefinition : IDynamicOptionsActionDefinition
	{
		public string Id => "dynamic";
		public LocalizedText Name => "Dynamic";
		public LocalizedText Description => "";

		public IReadOnlyList<ActionParameter> Parameters { get; } =
		[
			ActionParameter.DynamicChoice("device"),
			ActionParameter.Autocomplete("process", optionsSourceId: "test.source")
		];

		public DynamicOptionsContext? LastContext { get; private set; }

		public IActionExecutor CreateExecutor() => throw new NotSupportedException();

		public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
			DynamicOptionsContext context,
			CancellationToken cancellationToken)
		{
			LastContext = context;
			return Task.FromResult(new DynamicOptionsResult
			{
				Options = [new ActionParameterOption { Value = "speaker", Label = "Speaker" }],
				AllowsCustomValue = true,
				CacheSeconds = 30
			});
		}
	}

	private sealed class ErroringActionDefinition : IDynamicOptionsActionDefinition
	{
		public string Id => "erroring";
		public LocalizedText Name => "Erroring";
		public LocalizedText Description => "";

		public IReadOnlyList<ActionParameter> Parameters { get; } = [ActionParameter.DynamicChoice("device")];

		public IActionExecutor CreateExecutor() => throw new NotSupportedException();

		public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
			DynamicOptionsContext context,
			CancellationToken cancellationToken)
			=> Task.FromResult(new DynamicOptionsResult { Options = [], Error = "Choose an instance first" });
	}

	private sealed class ErroringEventProvider : IHostEventProvider, IDynamicEventOptionsProvider
	{
		public string ProviderId => "erroring";

		public LocalizedText ProviderName => "Erroring";

		public IReadOnlyList<EventDefinition> EventDefinitions { get; } =
		[
			new()
			{
				Id = "erroring",
				Name = "Erroring",
				ConfigurationParameters = [ActionParameter.Autocomplete("target")],
				PayloadParameters = []
			}
		];

		public Task<DynamicOptionsResult> GetEventOptionsAsync(
			EventOptionsContext context,
			CancellationToken cancellationToken)
			=> Task.FromResult(new DynamicOptionsResult { Options = [], Error = "Choose an instance first" });
	}

	private sealed class TestOptionsSource : IHostOptionsSource
	{
		public string Id => "test.source";

		public Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken)
			=> Task.FromResult(new DynamicOptionsResult
			{
				Options = [new ActionParameterOption { Value = $"host:{filter}" }]
			});
	}

	private sealed class WidgetLikeOptionsSource : IHostOptionsSource
	{
		public string Id => "macrodeck.widgets";

		public Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken)
			=> Task.FromResult(new DynamicOptionsResult
			{
				Options =
				[
					new ActionParameterOption
					{
						Value = "button-1",
						Label = "Button",
						Metadata = new Dictionary<string, string> { ["type"] = "ActionButton" }
					},
					new ActionParameterOption
					{
						Value = "slider-1",
						Label = "Slider",
						Metadata = new Dictionary<string, string> { ["type"] = "Slider" }
					}
				]
			});
	}

	/// <summary>Mirrors ADB's device list, which offers an empty "Default device" as its "any" pick.</summary>
	private sealed class SentinelOptionsSource : IHostOptionsSource
	{
		public string Id => AdbOptionsSourceIds.Devices;

		public Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken)
			=> Task.FromResult(new DynamicOptionsResult
			{
				Options =
				[
					new ActionParameterOption { Value = string.Empty, Label = "Default device" },
					new ActionParameterOption { Value = "serial-1", Label = "Pixel" }
				]
			});
	}

	private sealed class DeviceLikeOptionsSource : IHostOptionsSource
	{
		public string Id => DeckOptionsSourceIds.Devices;

		public Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken)
			=> Task.FromResult(new DynamicOptionsResult
			{
				Options =
				[
					new ActionParameterOption { Value = "11111111-1111-1111-1111-111111111111", Label = "MacBook Pro" },
					new ActionParameterOption { Value = "22222222-2222-2222-2222-222222222222", Label = "Studio PC" }
				]
			});
	}

	/// <summary>
	/// Declares one name in both parameter lists with different metadata, which is what makes the
	/// request's parameter kind load-bearing rather than a hint.
	/// </summary>
	private sealed class ProbeEventProvider : IHostEventProvider, IDynamicEventOptionsProvider
	{
		public string ProviderId => "probe";

		public LocalizedText ProviderName => "Probe";

		public IReadOnlyList<EventDefinition> EventDefinitions { get; } =
		[
			new()
			{
				Id = "dual",
				Name = "Dual",
				ConfigurationParameters = [ActionParameter.Autocomplete("target")],
				PayloadParameters =
				[
					ActionParameter.DynamicChoice("target", optionsSourceId: DeckOptionsSourceIds.Devices)
				]
			},
			new()
			{
				Id = "sentinel",
				Name = "Sentinel",
				ConfigurationParameters =
				[
					ActionParameter.DynamicChoice("serial", optionsSourceId: AdbOptionsSourceIds.Devices)
				],
				PayloadParameters =
				[
					ActionParameter.DynamicChoice("serial", optionsSourceId: AdbOptionsSourceIds.Devices)
				]
			},
			new()
			{
				Id = "plugin-like",
				Name = "Plugin like",
				ConfigurationParameters = [ActionParameter.Text("server")],
				PayloadParameters = [ActionParameter.DynamicChoice("entityId")]
			}
		];

		public EventOptionsContext? LastContext { get; private set; }

		public Task<DynamicOptionsResult> GetEventOptionsAsync(
			EventOptionsContext context,
			CancellationToken cancellationToken)
		{
			LastContext = context;
			var value = context.ParameterName == "entityId"
				? context.CurrentParameters.TryGetValue("server", out var server) && server as string == "alpha"
					? "light.a"
					: "light.b"
				: "from-configuration";

			return Task.FromResult(new DynamicOptionsResult
			{
				Options = [new ActionParameterOption { Value = value, Label = "Kitchen" }]
			});
		}
	}

	private static GetActionParameterOptionsRequestMessageHandler CreateEventHandler(
		out ProbeEventProvider provider)
	{
		provider = new ProbeEventProvider();
		var registry = new FakeIntegrationRegistry();
		return new GetActionParameterOptionsRequestMessageHandler(registry,
			new EventRegistry(registry, [new CoreEventProvider(), provider], _logger),
			[new DeviceLikeOptionsSource(), new SentinelOptionsSource()],
			_logger);
	}

	private static GetActionParameterOptionsRequestMessageHandler CreateHandler(
		out DynamicActionDefinition action)
	{
		action = new DynamicActionDefinition();
		var registry = new FakeIntegrationRegistry();
		registry.Add(new FakeIntegration { Id = "integration", Actions = [action] });
		return new GetActionParameterOptionsRequestMessageHandler(registry,
			new EventRegistry(registry, [], _logger),
			[new TestOptionsSource(), new WidgetLikeOptionsSource()],
			_logger);
	}

	[Test]
	public async Task An_options_source_resolves_without_an_action_behind_it()
	{
		// A config-flow step's fields have no action to address the parameter through.
		var handler = CreateHandler(out _);

		var response = await handler.Handle(new GetActionParameterOptionsRequest
			{
				OptionsSourceId = "macrodeck.widgets",
				ParameterName = "target"
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Error, Is.Null);
			Assert.That(response.Options.Select(o => o.Value), Is.EquivalentTo(_bothWidgets));
		});
	}

	[Test]
	public async Task A_widget_target_restricted_to_a_type_is_never_offered_the_others()
	{
		// Filtering only in the picker would still hand a plugin the ids of widget types it declared it
		// cannot accept.
		var handler = CreateHandler(out _);

		var response = await handler.Handle(new GetActionParameterOptionsRequest
			{
				OptionsSourceId = "macrodeck.widgets",
				WidgetTypes = _actionButtonOnly,
				ParameterName = "target"
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Error, Is.Null);
			Assert.That(response.Options.Single().Value, Is.EqualTo("button-1"));
		});
	}

	[Test]
	public async Task An_unknown_options_source_is_reported_rather_than_answered_empty()
	{
		var handler = CreateHandler(out _);

		var response = await handler.Handle(new GetActionParameterOptionsRequest
			{
				OptionsSourceId = "macrodeck.nope",
				ParameterName = "target"
			},
			CancellationToken.None);

		Assert.That(response.Error, Is.Not.Null);
	}

	[Test]
	public async Task Resolves_options_from_extension()
	{
		var handler = CreateHandler(out var action);

		var response = await handler.Handle(new GetActionParameterOptionsRequest
			{
				IntegrationId = "integration",
				ActionId = "dynamic",
				ParameterName = "device",
				Filter = "spe"
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Error, Is.Null);
			Assert.That(response.Options.Single().Value, Is.EqualTo("speaker"));
			Assert.That(response.AllowsCustomValue, Is.True);
			Assert.That(response.CacheSeconds, Is.EqualTo(30));
			Assert.That(action.LastContext?.Filter, Is.EqualTo("spe"));
		});
	}

	[Test]
	public async Task Routes_options_source_id_to_host_source()
	{
		var handler = CreateHandler(out _);

		var response = await handler.Handle(new GetActionParameterOptionsRequest
			{
				IntegrationId = "integration",
				ActionId = "dynamic",
				ParameterName = "process",
				Filter = "fire"
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Error, Is.Null);
			Assert.That(response.Options.Single().Value, Is.EqualTo("host:fire"));
		});
	}

	[Test]
	public async Task Unknown_action_returns_error()
	{
		var handler = CreateHandler(out _);

		var response = await handler.Handle(new GetActionParameterOptionsRequest
			{
				IntegrationId = "integration",
				ActionId = "missing",
				ParameterName = "device"
			},
			CancellationToken.None);

		Assert.That(response.Error?.Code, Is.EqualTo("ACTION_NOT_FOUND"));
	}

	[Test]
	public async Task Unknown_parameter_returns_error()
	{
		var handler = CreateHandler(out _);

		var response = await handler.Handle(new GetActionParameterOptionsRequest
			{
				IntegrationId = "integration",
				ActionId = "dynamic",
				ParameterName = "nope"
			},
			CancellationToken.None);

		Assert.That(response.Error?.Code, Is.EqualTo("PARAMETER_NOT_FOUND"));
	}

	[Test]
	public async Task Action_without_provider_returns_error()
	{
		var registry = new FakeIntegrationRegistry();
		registry.Add(new FakeIntegration
		{
			Id = "integration",
			Actions =
			[
				new CapturingActionDefinition
				{
					Id = "static",
					Parameters = [ActionParameter.DynamicChoice("device")]
				}
			]
		});
		var handler = new GetActionParameterOptionsRequestMessageHandler(registry,
			new EventRegistry(registry, [], _logger),
			[],
			_logger);

		var response = await handler.Handle(new GetActionParameterOptionsRequest
			{
				IntegrationId = "integration",
				ActionId = "static",
				ParameterName = "device"
			},
			CancellationToken.None);

		Assert.That(response.Error?.Code, Is.EqualTo("NO_OPTIONS_PROVIDER"));
	}

	[Test]
	public async Task An_event_payload_parameter_resolves_from_its_host_options_source()
	{
		// Authoring "event.deviceId is ..." needs the device list, and deviceId exists only in the
		// occurrence payload - folder-changed's configuration filters on the folder.
		var handler = CreateEventHandler(out _);

		var response = await handler.Handle(new GetActionParameterOptionsRequest
			{
				EventId = $"{CoreEventProvider.ProviderIdValue}::{EventIds.FolderChanged}",
				EventParameterKind = EventParameterKinds.Payload,
				ParameterName = "deviceId"
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Error, Is.Null);
			Assert.That(response.Options.Select(o => o.Label.Literal), Is.EqualTo(_deviceNames));
		});
	}

	[Test]
	public async Task The_parameter_kind_picks_the_list_rather_than_ordering_a_search()
	{
		// The same name can be declared in both lists with different metadata, so resolving one and
		// falling back to the other would silently answer with the wrong parameter's options.
		var handler = CreateEventHandler(out var provider);

		var payload = await handler.Handle(new GetActionParameterOptionsRequest
			{
				EventId = "probe::dual",
				EventParameterKind = EventParameterKinds.Payload,
				ParameterName = "target"
			},
			CancellationToken.None);
		var configured = await handler.Handle(new GetActionParameterOptionsRequest
			{
				EventId = "probe::dual",
				EventParameterKind = EventParameterKinds.Configuration,
				ParameterName = "target"
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(payload.Options.Select(o => o.Value), Is.EqualTo(_deviceIds));
			Assert.That(configured.Options.Single().Value, Is.EqualTo("from-configuration"));
		});
	}

	[Test]
	public async Task A_payload_parameter_without_an_options_source_reaches_the_event_provider()
	{
		// The plugin-facing path: the provider is asked by unqualified event id, and the values it is
		// given to resolve against are the trigger's configuration.
		var handler = CreateEventHandler(out var provider);

		var response = await handler.Handle(new GetActionParameterOptionsRequest
			{
				EventId = "probe::plugin-like",
				EventParameterKind = EventParameterKinds.Payload,
				ParameterName = "entityId",
				CurrentParameters = new Dictionary<string, JsonElement>
				{
					["server"] = JsonSerializer.SerializeToElement("alpha")
				}
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Error, Is.Null);
			Assert.That(response.Options.Single().Value, Is.EqualTo("light.a"));
			Assert.That(provider.LastContext?.EventId, Is.EqualTo("plugin-like"));
			Assert.That(provider.LastContext?.ParameterName, Is.EqualTo("entityId"));
		});
	}

	[Test]
	public async Task A_payload_picker_is_not_offered_the_filter_s_any_entry()
	{
		// An options source shared with a filter offers an empty value meaning "any". An occurrence
		// always carries a real one, so picking it would author a condition that can never hold.
		var handler = CreateEventHandler(out _);

		var payload = await handler.Handle(new GetActionParameterOptionsRequest
			{
				EventId = "probe::sentinel",
				EventParameterKind = EventParameterKinds.Payload,
				ParameterName = "serial"
			},
			CancellationToken.None);
		var configured = await handler.Handle(new GetActionParameterOptionsRequest
			{
				EventId = "probe::sentinel",
				EventParameterKind = EventParameterKinds.Configuration,
				ParameterName = "serial"
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(payload.Options.Select(o => o.Value), Is.EqualTo(_serialsWithoutSentinel));
			Assert.That(configured.Options.Select(o => o.Value), Is.EqualTo(_serialsWithSentinel));
		});
	}

	[Test]
	public async Task An_unknown_payload_parameter_is_reported_as_a_parameter_error()
	{
		var handler = CreateEventHandler(out _);

		var response = await handler.Handle(new GetActionParameterOptionsRequest
			{
				EventId = $"{CoreEventProvider.ProviderIdValue}::{EventIds.FolderChanged}",
				EventParameterKind = EventParameterKinds.Payload,
				ParameterName = "nope"
			},
			CancellationToken.None);

		Assert.That(response.Error?.Code, Is.EqualTo("PARAMETER_NOT_FOUND"));
	}

	[Test]
	public async Task A_non_empty_result_error_becomes_a_transport_error_on_the_action_path()
	{
		var registry = new FakeIntegrationRegistry();
		registry.Add(new FakeIntegration { Id = "integration", Actions = [new ErroringActionDefinition()] });
		var handler = new GetActionParameterOptionsRequestMessageHandler(registry,
			new EventRegistry(registry, [], _logger),
			[],
			_logger);

		var response = await handler.Handle(new GetActionParameterOptionsRequest
			{
				IntegrationId = "integration",
				ActionId = "erroring",
				ParameterName = "device"
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Error?.Code, Is.EqualTo("OPTIONS_UNAVAILABLE"));
			Assert.That(TestLocalization.Resolve(response.Error!.Message), Is.EqualTo("Choose an instance first"));
			Assert.That(response.Options, Is.Empty);
		});
	}

	[Test]
	public async Task A_non_empty_result_error_becomes_a_transport_error_on_the_event_path()
	{
		var registry = new FakeIntegrationRegistry();
		var provider = new ErroringEventProvider();
		var handler = new GetActionParameterOptionsRequestMessageHandler(registry,
			new EventRegistry(registry, [provider], _logger),
			[],
			_logger);

		var response = await handler.Handle(new GetActionParameterOptionsRequest
			{
				EventId = "erroring::erroring",
				EventParameterKind = EventParameterKinds.Configuration,
				ParameterName = "target"
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Error?.Code, Is.EqualTo("OPTIONS_UNAVAILABLE"));
			Assert.That(TestLocalization.Resolve(response.Error!.Message), Is.EqualTo("Choose an instance first"));
			Assert.That(response.Options, Is.Empty);
		});
	}

	[Test]
	public async Task An_event_request_without_a_kind_still_means_a_configuration_parameter()
	{
		// Every caller that predates the payload form omits the field.
		var handler = CreateEventHandler(out _);

		var response = await handler.Handle(new GetActionParameterOptionsRequest
			{
				EventId = "probe::dual",
				ParameterName = "target"
			},
			CancellationToken.None);

		Assert.That(response.Options.Single().Value, Is.EqualTo("from-configuration"));
	}
}
