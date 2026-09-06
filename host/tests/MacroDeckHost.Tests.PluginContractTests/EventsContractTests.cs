using MacroDeck.Plugin.Hosting.Capabilities.Events;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using Serilog;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class EventsContractTests : CapabilityContractFixture
{
	private static DeclaredCapability Provider()
		=> new()
		{
			Kind = CapabilityKinds.Events,
			LocalId = ProviderCapabilityId.LocalId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};

	private static EventDefinition SceneChanged()
		=> new()
		{
			Id = "scene-changed", Name = "Scene changed", ConfigurationParameters = [ActionParameter.Text("scene")]
		};

	[Test]
	public async Task Declare_registers_an_adapter_that_passes_the_capability_validator()
	{
		var integration = await ConnectAsync([
				new EventsCapabilityHandler([new TestEventIntegration("OBS Studio", SceneChanged())],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.Events]);

		Assert.Multiple(() =>
		{
			Assert.That(IntegrationCapabilityValidator.Validate(integration), Is.Empty);
			Assert.That(((IEventProvider)integration).EventDefinitions.Select(e => e.Id),
				Does.Contain("scene-changed"));
		});
	}

	private static EventDefinition EntityChanged()
		=> new()
		{
			Id = "entity-changed",
			Name = "Entity changed",
			PayloadParameters = [ActionParameter.DynamicChoice("entityId")]
		};

	[Test]
	public async Task A_condition_resolves_a_payload_parameter_through_the_events_capability()
	{
		// The whole point of keeping this metadata-driven: a plugin declares a selectable payload value
		// and the condition editor can offer it, with no Macro Deck-side knowledge of the plugin.
		var plugin = new TestDynamicEventIntegration("Home", EntityChanged())
		{
			ResultToReturn = new DynamicOptionsResult
			{
				Options = [new ActionParameterOption { Value = "light.kitchen", Label = "Kitchen" }]
			}
		};

		var integration = await ConnectAsync([new EventsCapabilityHandler([plugin], TestMetadata.Default)],
			[Provider()],
			[CapabilityKinds.Events]);

		var logger = new LoggerConfiguration().CreateLogger();
		var handler = new GetActionParameterOptionsRequestMessageHandler(IntegrationRegistry,
			new EventRegistry(IntegrationRegistry, [], logger),
			[],
			logger);

		var response = await handler.Handle(new GetActionParameterOptionsRequest
			{
				EventId = $"{integration.Id}::entity-changed",
				EventParameterKind = EventParameterKinds.Payload,
				ParameterName = "entityId"
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Error, Is.Null);
			Assert.That(response.Options.Single().Value, Is.EqualTo("light.kitchen"));

			// Proves the request really crossed the wire rather than being answered host-side.
			Assert.That(plugin.LastContext?.ParameterName, Is.EqualTo("entityId"));
			Assert.That(plugin.LastContext?.EventId, Is.EqualTo("entity-changed"));
		});
	}

	[Test]
	public async Task Every_operation_round_trips_to_the_sdk_type_the_host_contract_requires()
	{
		var dynamicIntegration = new TestDynamicEventIntegration("OBS Studio", SceneChanged())
		{
			ResultToReturn = new DynamicOptionsResult
			{
				Options = [new ActionParameterOption { Value = "main", Label = "Main" }], AllowsCustomValue = true
			}
		};

		var integration = await ConnectAsync([new EventsCapabilityHandler([dynamicIntegration], TestMetadata.Default)],
			[Provider()],
			[CapabilityKinds.Events]);

		var provider = (IEventProvider)integration;
		Assert.Multiple(() =>
		{
			Assert.That(((IEventProvider)provider).ProviderName, Is.EqualTo("OBS Studio"));
			Assert.That(provider.EventDefinitions.Single().Id, Is.EqualTo("scene-changed"));
			Assert.That(integration, Is.InstanceOf<IDynamicEventOptionsProvider>());
		});

		var dynamicProvider = (IDynamicEventOptionsProvider)integration;
		var options = await dynamicProvider.GetEventOptionsAsync(
			new EventOptionsContext { EventId = "scene-changed", ParameterName = "scene" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(options.Options.Select(o => o.Value), Is.EqualTo(new[] { "main" }));
			Assert.That(options.AllowsCustomValue, Is.True);
		});
	}

	[Test]
	public async Task A_timeout_degrades_to_an_empty_option_list_never_an_exception()
	{
		var integration = await ConnectAsync([
				new EventsCapabilityHandler([new NeverRepliesDynamicEventIntegration("OBS Studio", SceneChanged())],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.Events]);

		var dynamicProvider = (IDynamicEventOptionsProvider)integration;
		var optionsTask = dynamicProvider.GetEventOptionsAsync(
			new EventOptionsContext { EventId = "scene-changed", ParameterName = "scene" },
			CancellationToken.None);

		Time.Advance(ProtocolTimeouts.CapabilityInvoke);
		var options = await optionsTask;

		Assert.That(options.Options, Is.Empty);
	}

	[Test]
	public async Task A_dropped_connection_degrades_to_an_empty_option_list_never_an_exception()
	{
		var integration = await ConnectAsync([
				new EventsCapabilityHandler([new TestDynamicEventIntegration("OBS Studio", SceneChanged())],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.Events]);

		Disconnect();

		var dynamicProvider = (IDynamicEventOptionsProvider)integration;
		var options = await dynamicProvider.GetEventOptionsAsync(
			new EventOptionsContext { EventId = "scene-changed", ParameterName = "scene" },
			CancellationToken.None);

		Assert.That(options.Options, Is.Empty);
	}

	[Test]
	public async Task Caller_cancellation_puts_capability_cancel_on_the_wire()
	{
		var stillRunning = new TaskCompletionSource<DynamicOptionsResult>();
		var integration = await ConnectAsync([
				new EventsCapabilityHandler(
					[new SlowDynamicEventIntegration("OBS Studio", stillRunning.Task, SceneChanged())],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.Events]);

		using var cts = new CancellationTokenSource();
		var dynamicProvider = (IDynamicEventOptionsProvider)integration;
		var optionsTask = dynamicProvider.GetEventOptionsAsync(
			new EventOptionsContext { EventId = "scene-changed", ParameterName = "scene" },
			cts.Token);

		await cts.CancelAsync();

		Assert.CatchAsync<OperationCanceledException>(async () => await optionsTask);
		Assert.That(Link.SentByHost.Any(envelope => envelope.Type == MessageTypes.CapabilityCancel), Is.True);

		stillRunning.TrySetResult(new DynamicOptionsResult { Options = [] });
	}

	[Test]
	public async Task A_throwing_handler_yields_a_redacted_internal_error()
	{
		await ConnectAsync([
				new EventsCapabilityHandler([new ThrowingDynamicEventIntegration("OBS Studio", SceneChanged())],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.Events]);

		var exception = Assert.CatchAsync<RemoteCapabilityException>(async () => await InvokeRawAsync(
			CapabilityKinds.Events,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Events.Options,
			new { eventId = "scene-changed", parameterName = "scene" }));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.Code, Is.EqualTo(ProtocolErrorCodes.InternalError));
			Assert.That(exception.Message, Does.Not.Contain("boom"));
			Assert.That(exception.Message, Does.Not.Contain("token=abc123"));
		});
	}

	[Test]
	public async Task An_unknown_local_id_is_unavailable_and_an_unknown_operation_is_unsupported()
	{
		await ConnectAsync([
				new EventsCapabilityHandler([new TestEventIntegration("OBS Studio", SceneChanged())],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.Events]);

		var unknownLocalId = Assert.CatchAsync<RemoteCapabilityException>(async () => await InvokeRawAsync(
			CapabilityKinds.Events,
			"nope",
			CapabilityOperations.Events.Options,
			new { eventId = "scene-changed", parameterName = "scene" }));
		var unknownOperation = Assert.CatchAsync<RemoteCapabilityException>(async () =>
			await InvokeRawAsync(CapabilityKinds.Events, ProviderCapabilityId.LocalId, "rewind"));

		Assert.Multiple(() =>
		{
			Assert.That(unknownLocalId!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(unknownOperation!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
		});
	}

	private sealed class SlowDynamicEventIntegration(
		string providerName,
		Task<DynamicOptionsResult> pending,
		params EventDefinition[] events)
		: TestEventIntegration(providerName, events), IDynamicEventOptionsProvider
	{
		public Task<DynamicOptionsResult> GetEventOptionsAsync(EventOptionsContext context,
			CancellationToken cancellationToken)
			=> pending;
	}

	private sealed class ThrowingDynamicEventIntegration(string providerName, params EventDefinition[] events)
		: TestEventIntegration(providerName, events), IDynamicEventOptionsProvider
	{
		public Task<DynamicOptionsResult> GetEventOptionsAsync(EventOptionsContext context,
			CancellationToken cancellationToken)
			=> throw new InvalidOperationException("boom: token=abc123");
	}

	private sealed class NeverRepliesDynamicEventIntegration(string providerName, params EventDefinition[] events)
		: TestEventIntegration(providerName, events), IDynamicEventOptionsProvider
	{
		public async Task<DynamicOptionsResult> GetEventOptionsAsync(EventOptionsContext context,
			CancellationToken cancellationToken)
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
			return new DynamicOptionsResult { Options = [] };
		}
	}
}
