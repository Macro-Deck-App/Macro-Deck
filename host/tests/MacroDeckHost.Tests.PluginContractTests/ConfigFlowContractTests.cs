using MacroDeck.Localization;
using MacroDeck.Plugin.Hosting.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Plugin.Testing;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class ConfigFlowContractTests : CapabilityContractFixture
{
	private static DeclaredCapability Provider()
		=> new()
		{
			Kind = CapabilityKinds.ConfigFlow,
			LocalId = ProviderCapabilityId.LocalId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};

	[Test]
	public async Task Declare_registers_an_adapter_that_passes_the_capability_validator()
	{
		var integration = await ConnectAsync([
				new ConfigFlowCapabilityHandler([new TestConfigFlowIntegration(() => new TestConfigFlow())],
					new PluginConfigFlowSessions(TimeProvider.System))
			],
			[Provider()],
			[CapabilityKinds.ConfigFlow]);

		Assert.Multiple(() =>
		{
			Assert.That(IntegrationCapabilityValidator.Validate(integration), Is.Empty);
			Assert.That(integration, Is.InstanceOf<IConfigFlowProvider>());
		});
	}

	[Test]
	public async Task Describe_reports_allows_multiple_configurations()
	{
		var integration = await ConnectAsync([
				new ConfigFlowCapabilityHandler([
						new TestConfigFlowIntegration(() => new TestConfigFlow(), allowsMultipleConfigurations: false)
					],
					new PluginConfigFlowSessions(TimeProvider.System))
			],
			[Provider()],
			[CapabilityKinds.ConfigFlow]);

		Assert.That(((IConfigFlowProvider)integration).AllowsMultipleConfigurations, Is.False);
	}

	[Test]
	public async Task A_multi_step_flow_round_trips_with_state_held_on_the_plugin_side()
	{
		var pluginFlow = new TestConfigFlow
		{
			ResultToReturn = ConfigFlowResult.Step(new ConfigFlowStep { StepId = "one", Fields = [] })
		};
		pluginFlow.SubmitOverride = (stepId, input, _, _) => stepId switch
		{
			"one" => Task.FromResult(ConfigFlowResult.Step(new ConfigFlowStep { StepId = "two", Fields = [] })),
			"two" => Task.FromResult(ConfigFlowResult.Complete("Prod",
				new Dictionary<string, ConfigFlowValue>
				{
					["token"] = ConfigFlowValue.Secret("abc")
				})),
			_ => throw new InvalidOperationException()
		};

		var createCount = 0;
		var integration = await ConnectAsync([
				new ConfigFlowCapabilityHandler([
						new TestConfigFlowIntegration(() =>
						{
							createCount++;
							return pluginFlow;
						})
					],
					new PluginConfigFlowSessions(TimeProvider.System))
			],
			[Provider()],
			[CapabilityKinds.ConfigFlow]);

		var provider = (IConfigFlowProvider)integration;
		var flow = provider.CreateConfigFlow();
		var context = new TestConfigFlowContext();

		var start = await flow.StartAsync(context, CancellationToken.None);
		Assert.That(start.NextStep!.StepId, Is.EqualTo("one"));

		var stepOne = await flow.SubmitAsync("one",
			new Dictionary<string, object?> { ["field"] = "value" },
			context,
			CancellationToken.None);
		Assert.That(stepOne.NextStep!.StepId, Is.EqualTo("two"));
		var stepOneInput = pluginFlow.LastInput;

		var stepTwo = await flow.SubmitAsync("two", new Dictionary<string, object?>(), context, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(createCount, Is.EqualTo(1));
			Assert.That(pluginFlow.Calls, Is.EqualTo(new[] { "start", "submit:one", "submit:two" }));
			Assert.That(stepOneInput!["field"], Is.EqualTo("value"));

			Assert.That(stepTwo.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(stepTwo.EntryTitle, Is.EqualTo("Prod"));
			Assert.That(stepTwo.Values!["token"].IsSecret, Is.True);
			Assert.That(stepTwo.Values!["token"].Value, Is.EqualTo("abc"));
		});
	}

	[Test]
	public async Task Step_result_survives_the_round_trip()
	{
		var step = new ConfigFlowStep { StepId = "step-id", Title = "Title", Fields = [] };
		var flow = await StartFlowWithResultAsync(ConfigFlowResult.Step(step));

		Assert.Multiple(() =>
		{
			Assert.That(flow.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(flow.NextStep!.StepId, Is.EqualTo("step-id"));
			Assert.That(flow.NextStep!.Title.Literal, Is.EqualTo("Title"));
		});
	}

	[Test]
	public async Task Error_result_survives_the_round_trip()
	{
		var step = new ConfigFlowStep { StepId = "step-id", Fields = [] };
		var result = ConfigFlowResult.Error(step,
			"bad input",
			new Dictionary<string, LocalizedText> { ["field"] = "required" });
		var flow = await StartFlowWithResultAsync(result);

		Assert.Multiple(() =>
		{
			Assert.That(flow.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(flow.ErrorMessage.Literal, Is.EqualTo("bad input"));
			Assert.That(flow.FieldErrors!["field"].Literal, Is.EqualTo("required"));
		});
	}

	[Test]
	public async Task Complete_result_survives_the_round_trip()
	{
		var result = ConfigFlowResult.Complete("Entry Title",
			new Dictionary<string, ConfigFlowValue>
			{
				["plain"] = ConfigFlowValue.Plain("value"), ["secret"] = ConfigFlowValue.Secret("shh")
			});
		var flow = await StartFlowWithResultAsync(result);

		Assert.Multiple(() =>
		{
			Assert.That(flow.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(flow.EntryTitle, Is.EqualTo("Entry Title"));
			Assert.That(flow.Values!["plain"].Value, Is.EqualTo("value"));
			Assert.That(flow.Values!["plain"].IsSecret, Is.False);
			Assert.That(flow.Values!["secret"].IsSecret, Is.True);
		});
	}

	[Test]
	public async Task External_result_survives_the_round_trip()
	{
		var result = ConfigFlowResult.External("https://example.com/authorize", "resume-step");
		var flow = await StartFlowWithResultAsync(result);

		Assert.Multiple(() =>
		{
			Assert.That(flow.Kind, Is.EqualTo(ConfigFlowResultKind.External));
			Assert.That(flow.ExternalUrl, Is.EqualTo("https://example.com/authorize"));
			Assert.That(flow.ResumeStepId, Is.EqualTo("resume-step"));
		});
	}

	private async Task<ConfigFlowResult> StartFlowWithResultAsync(ConfigFlowResult result)
	{
		var integration = await ConnectAsync([
				new ConfigFlowCapabilityHandler(
					[new TestConfigFlowIntegration(() => new TestConfigFlow { ResultToReturn = result })],
					new PluginConfigFlowSessions(TimeProvider.System))
			],
			[Provider()],
			[CapabilityKinds.ConfigFlow]);

		var provider = (IConfigFlowProvider)integration;
		var flow = provider.CreateConfigFlow();
		return await flow.StartAsync(new TestConfigFlowContext(), CancellationToken.None);
	}

	[Test]
	public async Task Abandonment_releases_the_plugin_side_instance()
	{
		var integration = await ConnectAsync([
				new ConfigFlowCapabilityHandler([new TestConfigFlowIntegration(() => new TestConfigFlow())],
					new PluginConfigFlowSessions(TimeProvider.System))
			],
			[Provider()],
			[CapabilityKinds.ConfigFlow]);

		var provider = (IConfigFlowProvider)integration;
		var flow = provider.CreateConfigFlow();
		var context = new TestConfigFlowContext();

		await flow.StartAsync(context, CancellationToken.None);
		await ((IAsyncDisposable)flow).DisposeAsync();

		var exception = Assert.CatchAsync<RemoteCapabilityException>(async ()
			=> await flow.SubmitAsync("step", new Dictionary<string, object?>(), context, CancellationToken.None));
		Assert.That(exception!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
	}

	[Test]
	public async Task An_idle_session_is_released_when_the_plugin_side_clock_passes_the_timeout()
	{
		var pluginClock = new ManualTimeProvider();
		var integration = await ConnectAsync([
				new ConfigFlowCapabilityHandler([new TestConfigFlowIntegration(() => new TestConfigFlow())],
					new PluginConfigFlowSessions(pluginClock))
			],
			[Provider()],
			[CapabilityKinds.ConfigFlow]);

		var provider = (IConfigFlowProvider)integration;
		var flow = provider.CreateConfigFlow();
		var context = new TestConfigFlowContext();

		await flow.StartAsync(context, CancellationToken.None);

		pluginClock.Advance(ConfigFlowCapabilityHandler.IdleTimeout + TimeSpan.FromSeconds(1));

		await InvokeRawAsync(CapabilityKinds.ConfigFlow,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.ConfigFlow.Describe);

		var exception = Assert.CatchAsync<RemoteCapabilityException>(async ()
			=> await flow.SubmitAsync("step", new Dictionary<string, object?>(), context, CancellationToken.None));
		Assert.That(exception!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
	}

	[Test]
	public async Task A_timeout_throws_never_silently_degrades()
	{
		var pluginFlow = new TestConfigFlow { StartOverride = NeverReplies };
		var integration = await ConnectAsync([
				new ConfigFlowCapabilityHandler([new TestConfigFlowIntegration(() => pluginFlow)],
					new PluginConfigFlowSessions(TimeProvider.System))
			],
			[Provider()],
			[CapabilityKinds.ConfigFlow]);

		var provider = (IConfigFlowProvider)integration;
		var flow = provider.CreateConfigFlow();

		var startTask = flow.StartAsync(new TestConfigFlowContext(), CancellationToken.None);
		Time.Advance(ProtocolTimeouts.CapabilityInvoke);

		var exception = Assert.CatchAsync<RemoteCapabilityException>(async () => await startTask);
		Assert.That(exception!.Code, Is.EqualTo(ProtocolErrorCodes.Timeout));
	}

	private static async Task<ConfigFlowResult> NeverReplies(IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		await Task.Delay(Timeout.Infinite, cancellationToken);
		return ConfigFlowResult.Complete("unreachable");
	}

	[Test]
	public async Task A_dropped_connection_throws_never_silently_degrades()
	{
		var integration = await ConnectAsync([
				new ConfigFlowCapabilityHandler([new TestConfigFlowIntegration(() => new TestConfigFlow())],
					new PluginConfigFlowSessions(TimeProvider.System))
			],
			[Provider()],
			[CapabilityKinds.ConfigFlow]);

		var provider = (IConfigFlowProvider)integration;
		var flow = provider.CreateConfigFlow();

		Disconnect();

		var exception = Assert.CatchAsync<RemoteCapabilityException>(async ()
			=> await flow.StartAsync(new TestConfigFlowContext(), CancellationToken.None));
		Assert.That(exception!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
	}

	[Test]
	public async Task Caller_cancellation_puts_capability_cancel_on_the_wire()
	{
		var pluginFlow = new TestConfigFlow { StartOverride = NeverReplies };
		await ConnectAsync([
				new ConfigFlowCapabilityHandler([new TestConfigFlowIntegration(() => pluginFlow)],
					new PluginConfigFlowSessions(TimeProvider.System))
			],
			[Provider()],
			[CapabilityKinds.ConfigFlow]);

		using var cts = new CancellationTokenSource();
		var invokeTask = InvokeRawAsync(CapabilityKinds.ConfigFlow,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.ConfigFlow.FlowStart,
			new { sessionId = "session-1", oAuth = new { redirectUri = "http://127.0.0.1/cb", state = "s" } },
			cts.Token);

		await cts.CancelAsync();

		Assert.CatchAsync<OperationCanceledException>(async () => await invokeTask);
		Assert.That(Link.SentByHost.Any(envelope => envelope.Type == MessageTypes.CapabilityCancel), Is.True);
	}

	[Test]
	public async Task A_throwing_handler_yields_a_redacted_internal_error()
	{
		await ConnectAsync([
				new ConfigFlowCapabilityHandler([new TestConfigFlowIntegration(() => new ThrowingTestConfigFlow())],
					new PluginConfigFlowSessions(TimeProvider.System))
			],
			[Provider()],
			[CapabilityKinds.ConfigFlow]);

		var exception = Assert.CatchAsync<RemoteCapabilityException>(async () => await InvokeRawAsync(
			CapabilityKinds.ConfigFlow,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.ConfigFlow.FlowStart,
			new { sessionId = "session-1", oAuth = new { redirectUri = "http://127.0.0.1/cb", state = "s" } }));

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
				new ConfigFlowCapabilityHandler([new TestConfigFlowIntegration(() => new TestConfigFlow())],
					new PluginConfigFlowSessions(TimeProvider.System))
			],
			[Provider()],
			[CapabilityKinds.ConfigFlow]);

		var unknownLocalId = Assert.CatchAsync<RemoteCapabilityException>(async () => await InvokeRawAsync(
			CapabilityKinds.ConfigFlow,
			"nope",
			CapabilityOperations.ConfigFlow.FlowAbandon,
			new { sessionId = "session-1" }));
		var unknownOperation = Assert.CatchAsync<RemoteCapabilityException>(async () => await InvokeRawAsync(
			CapabilityKinds.ConfigFlow,
			ProviderCapabilityId.LocalId,
			"rewind",
			new { sessionId = "session-1" }));

		Assert.Multiple(() =>
		{
			Assert.That(unknownLocalId!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(unknownOperation!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
		});
	}
}
