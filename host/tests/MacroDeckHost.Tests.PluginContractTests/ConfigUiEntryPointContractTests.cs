using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Hosting.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin;
using MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin.Actions;
using MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin.ConfigFlow;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.ConfigFlow;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Tests.PluginContractTests.Harness;

namespace MacroDeckHost.Tests.PluginContractTests;

/// <summary>
/// The two config-UI entry points (issue #543), driven end to end against the real
/// <see cref="WellBehavedIntegration"/> fixture over the simulated wire - the same harness style as
/// <see cref="UiSessionContractTests"/>, extended with the config flow and actions capabilities the
/// fixture also serves. <see cref="MacroDeckHost.Application.Ui.Sessions.ConfigUiSessionOpener"/> itself
/// is unit-tested elsewhere; what these scenarios are for is that the two entry points' surface
/// attributes reach the plugin process exactly as documented on
/// <see cref="MacroDeck.Ui.Model.Surfaces.UiConfigSurfaceAttributes"/>, and that a tree never becomes a
/// second way to persist a value.
/// </summary>
[TestFixture]
internal sealed class ConfigUiEntryPointContractTests : UiContractFixture
{
	private static DeclaredCapability ConfigFlowProvider()
		=> new()
		{
			Kind = CapabilityKinds.ConfigFlow,
			LocalId = ProviderCapabilityId.LocalId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};

	private static DeclaredCapability UiProvider()
		=> new()
		{
			Kind = CapabilityKinds.Ui, LocalId = ProviderCapabilityId.LocalId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};

	private static DeclaredCapability Action(string localId)
		=> new()
		{
			Kind = CapabilityKinds.Actions, LocalId = localId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};

	/// <summary>Wires the fixture with config-flow, actions and ui all served out of the same
	/// <see cref="PluginConfigFlowSessions"/> instance, exactly as <c>PluginHostBuilder</c> wires a real
	/// plugin process - the sharing is what lets <c>UiCapabilityHandler</c> resolve the flow session a
	/// <c>config-flow/flow.start</c> minted.</summary>
	private async Task<WellBehavedIntegration> ConnectFixtureAsync()
	{
		var fixture = new WellBehavedIntegration(new NoOpCatalogNotifier(), Serilog.Core.Logger.None);
		var configFlowSessions = new PluginConfigFlowSessions(TimeProvider.System);

		await ConnectAsync([
				new MacroDeck.Plugin.Hosting.Capabilities.Actions.ActionsCapabilityHandler([fixture]),
				new ConfigFlowCapabilityHandler([fixture], configFlowSessions),
				new UiCapabilityHandler([fixture],
					CreatePluginHostInvoker(),
					Serilog.Core.Logger.None,
					configFlowSessions,
					new ModalResultStore())
			],
			[
				Action("refresh-weather"), Action("set-alert-threshold"), Action("set-condition"),
				ConfigFlowProvider(), UiProvider()
			],
			[CapabilityKinds.Actions, CapabilityKinds.ConfigFlow, CapabilityKinds.Ui]);

		return fixture;
	}

	private async Task<(RemoteConfigFlow Flow, string StepId)> StartConfigFlowAsync()
	{
		var provider = (IConfigFlowProvider)IntegrationRegistry.Integrations
			.Single(integration => string.Equals(integration.Id, PluginId, StringComparison.Ordinal));
		var flow = (RemoteConfigFlow)provider.CreateConfigFlow();
		var start = await flow.StartAsync(new TestConfigFlowContext(), CancellationToken.None);
		return (flow, start.NextStep!.StepId);
	}

	private IReadOnlyList<JsonElement> SessionOpenInvocations()
		=>
		[
			.. Link.SentByHost
				.Where(envelope =>
					string.Equals(envelope.Type, MessageTypes.CapabilityInvoke, StringComparison.Ordinal))
				.Select(envelope =>
					envelope.Payload!.Value.Deserialize<CapabilityInvokePayload>(PluginProtocolJson.Options)!)
				.Where(payload => string.Equals(payload.Kind, CapabilityKinds.Ui, StringComparison.Ordinal) &&
					string.Equals(payload.Operation, CapabilityOperations.Ui.SessionOpen, StringComparison.Ordinal))
				.Select(payload => payload.Arguments!.Value)
		];

	private int ConfigFlowSubmitInvocationCount()
		=> Link.SentByHost.Count(envelope =>
			string.Equals(envelope.Type, MessageTypes.CapabilityInvoke, StringComparison.Ordinal) &&
			envelope.Payload!.Value.Deserialize<CapabilityInvokePayload>(PluginProtocolJson.Options)! is
				{ Kind: CapabilityKinds.ConfigFlow } payload &&
			string.Equals(payload.Operation, CapabilityOperations.ConfigFlow.FlowSubmit, StringComparison.Ordinal));

	[Test]
	public async Task
		A1_Integration_config_flow_surface_carries_exactly_the_documented_attributes_and_the_tree_arrives_verbatim()
	{
		await ConnectFixtureAsync();
		var (flow, _) = await StartConfigFlowAsync();

		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Config,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiConfigSurfaceAttributes.EntryPoint]
					= JsonSerializer.SerializeToElement(UiConfigEntryPoints.IntegrationConfig),
				[UiConfigSurfaceAttributes.IntegrationId] = JsonSerializer.SerializeToElement(PluginId),
				[UiConfigSurfaceAttributes.ConfigFlowSessionId] = JsonSerializer.SerializeToElement(flow.SessionId)
			}
		};

		var ticket = await Broker.OpenAsync(PluginId, surface, OwnerPrincipal, CancellationToken.None);
		Assert.That(ticket.Accepted, Is.True, ticket.Message);

		var opened = SessionOpenInvocations().Single();
		var attributes = opened.GetProperty("surfaceAttributes");

		Assert.Multiple(() =>
		{
			Assert.That(opened.GetProperty("surfaceKind").GetString(), Is.EqualTo(UiSurfaceKinds.Config));
			Assert.That(attributes.GetProperty(UiConfigSurfaceAttributes.EntryPoint).GetString(),
				Is.EqualTo(UiConfigEntryPoints.IntegrationConfig));
			Assert.That(attributes.GetProperty(UiConfigSurfaceAttributes.IntegrationId).GetString(),
				Is.EqualTo(PluginId));
			Assert.That(attributes.GetProperty(UiConfigSurfaceAttributes.ConfigFlowSessionId).GetString(),
				Is.EqualTo(flow.SessionId),
				"The plugin cannot correlate the tree to the flow whose SubmitAsync would terminate it.");
			Assert.That(attributes.TryGetProperty(UiConfigSurfaceAttributes.ActionId, out _), Is.False);
			Assert.That(attributes.TryGetProperty(UiConfigSurfaceAttributes.Parameters, out _), Is.False);

			var propertyNames = attributes.EnumerateObject().Select(property => property.Name).ToArray();
			Assert.That(propertyNames,
				Is.EquivalentTo(new[]
				{
					UiConfigSurfaceAttributes.EntryPoint, UiConfigSurfaceAttributes.IntegrationId,
					UiConfigSurfaceAttributes.ConfigFlowSessionId
				}));
		});

		Attach(ticket.SessionId, "c1");
		await WaitForUiAsync(() => UiTransport.MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 1,
			"The client never received the config flow's tree.");

		var pushed = UiTransport.MessagesFor<UiSessionTreeUpdatedEvent>("c1").Single();
		var treeText = System.Text.Encoding.UTF8.GetString(pushed.Tree.Utf8.Span);

		Assert.That(treeText,
			Does.Contain("\"id\":\"location\""),
			"The top-level input id must equal the field name the flow submits under.");

		UiWebSocketEgress.AssertCarriesVerbatim(pushed,
			pushed.Tree.Utf8.ToArray(),
			"A config flow tree reaches the client as the exact bytes the provider produced.");
	}

	[Test]
	public async Task S12_Dispatching_a_tree_event_never_completes_the_flow_or_submits_a_value()
	{
		await ConnectFixtureAsync();
		var (flow, stepId) = await StartConfigFlowAsync();

		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Config,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiConfigSurfaceAttributes.EntryPoint]
					= JsonSerializer.SerializeToElement(UiConfigEntryPoints.IntegrationConfig),
				[UiConfigSurfaceAttributes.IntegrationId] = JsonSerializer.SerializeToElement(PluginId),
				[UiConfigSurfaceAttributes.ConfigFlowSessionId] = JsonSerializer.SerializeToElement(flow.SessionId)
			}
		};

		var ticket = await Broker.OpenAsync(PluginId, surface, OwnerPrincipal, CancellationToken.None);
		Assert.That(ticket.Accepted, Is.True, ticket.Message);
		Attach(ticket.SessionId, "c1");
		await WaitForUiAsync(() => UiTransport.MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 1,
			"The client never received the config flow's tree.");

		var response = Broker.SendEvent(new UiSendEventRequest
			{
				SessionId = ticket.SessionId,
				NodeId = "location",
				Name = UiConfigEvents.Change,
				Data = UiRawJson.FromElement(JsonSerializer.SerializeToElement("Copenhagen, Denmark"))
			},
			"c1");

		Assert.That(response.Accepted, Is.True, response.Message);
		await WaitForUiAsync(() => UiTransport.MessagesFor<UiSessionPatchedEvent>("c1").Count >= 1,
			"The write-through binding never produced a patch for the dispatched change event.");

		Assert.That(ConfigFlowSubmitInvocationCount(),
			Is.Zero,
			"Dispatching a tree event must never call config-flow/flow.submit - only SubmitAsync persists.");

		var completed = await flow.SubmitAsync(stepId,
			new Dictionary<string, object?> { [WellBehavedConfigFlow.LocationFieldName] = "Oslo, Norway" },
			new TestConfigFlowContext(),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(completed.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(ConfigFlowSubmitInvocationCount(), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A2_Action_config_surface_carries_stored_values_and_closing_ends_the_session()
	{
		await ConnectFixtureAsync();

		var action = IntegrationRegistry.Integrations
			.Single(integration => string.Equals(integration.Id, PluginId, StringComparison.Ordinal))
			.Actions.Single(candidate => candidate.Id == "set-alert-threshold");

		var stored = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			// Deliberately not the declared default (30): a masking or seeding bug that fell back to the
			// schema instead of the stored value would still pass if the stored value equalled the default.
			["thresholdCelsius"] = JsonSerializer.SerializeToElement(42.0),
			[SetAlertThresholdAction.WebhookSecretParameterName]
				= JsonSerializer.SerializeToElement("s3cr3t-webhook-key")
		};

		var masked = ActionParameterSecretMasking.Mask(action.Parameters, stored);

		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Config,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiConfigSurfaceAttributes.EntryPoint]
					= JsonSerializer.SerializeToElement(UiConfigEntryPoints.ActionConfig),
				[UiConfigSurfaceAttributes.IntegrationId] = JsonSerializer.SerializeToElement(PluginId),
				[UiConfigSurfaceAttributes.ActionId] = JsonSerializer.SerializeToElement(action.Id),
				[UiConfigSurfaceAttributes.Parameters] = JsonSerializer.SerializeToElement(masked)
			}
		};

		var ticket = await Broker.OpenAsync(PluginId, surface, OwnerPrincipal, CancellationToken.None);
		Assert.That(ticket.Accepted, Is.True, ticket.Message);

		var opened = SessionOpenInvocations().Single();
		var attributes = opened.GetProperty("surfaceAttributes");
		var parameters = attributes.GetProperty(UiConfigSurfaceAttributes.Parameters);
		var serializedSurface = attributes.GetRawText();

		Assert.Multiple(() =>
		{
			Assert.That(attributes.GetProperty(UiConfigSurfaceAttributes.EntryPoint).GetString(),
				Is.EqualTo(UiConfigEntryPoints.ActionConfig));
			Assert.That(attributes.GetProperty(UiConfigSurfaceAttributes.ActionId).GetString(), Is.EqualTo(action.Id));
			Assert.That(attributes.TryGetProperty(UiConfigSurfaceAttributes.ConfigFlowSessionId, out _), Is.False);
			Assert.That(parameters.GetProperty("thresholdCelsius").GetDouble(), Is.EqualTo(42.0));
			Assert.That(parameters.GetProperty(SetAlertThresholdAction.WebhookSecretParameterName).GetString(),
				Is.EqualTo(UiConfigSurfaceAttributes.MaskedSecretValue));
			Assert.That(serializedSurface,
				Does.Not.Contain("s3cr3t-webhook-key"),
				"The plaintext secret must never reach the plugin process through the config surface.");
		});

		var closed = Broker.CloseOwned(ticket.SessionId, OwnerPrincipal, "test teardown");
		Assert.That(closed, Is.True);

		var reattach = Attach(ticket.SessionId, "c2");
		Assert.That(reattach.Accepted, Is.False);
		Assert.That(reattach.Code, Is.EqualTo(UiSessionErrorCodes.SessionNotFound));
	}

	[Test]
	public void A2b_A_secret_nested_inside_an_object_parameter_is_masked_and_a_sibling_value_crosses_intact()
	{
		var definitions = new List<ActionParameter>
		{
			ActionParameter.Object("credentials",
				children:
				[
					ActionParameter.Text("username", label: "Username"),
					ActionParameter.Secret("apiKey", label: "API key")
				],
				label: "Credentials")
		};

		var stored = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			["credentials"] = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
			{
				["username"] = "octocat", ["apiKey"] = "sk-live-do-not-leak"
			})
		};

		var masked = ActionParameterSecretMasking.Mask(definitions, stored);
		var serialized = JsonSerializer.Serialize(masked);

		Assert.Multiple(() =>
		{
			Assert.That(masked["credentials"].GetProperty("username").GetString(), Is.EqualTo("octocat"));
			Assert.That(masked["credentials"].GetProperty("apiKey").GetString(),
				Is.EqualTo(UiConfigSurfaceAttributes.MaskedSecretValue));
			Assert.That(serialized, Does.Not.Contain("sk-live-do-not-leak"));
		});
	}

	private sealed class NoOpCatalogNotifier : MacroDeck.Plugin.Hosting.Integrations.HostApis.IPluginCatalogNotifier
	{
		public void CatalogChanged(string kind, string? localId = null, string? reason = null)
		{
		}
	}
}
