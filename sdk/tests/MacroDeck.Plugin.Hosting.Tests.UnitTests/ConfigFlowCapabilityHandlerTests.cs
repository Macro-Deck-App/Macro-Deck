using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Testing;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using Microsoft.Extensions.DependencyInjection;
using MacroDeck.Localization;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class ConfigFlowCapabilityHandlerTests
{
	private static ServiceProvider _services = null!;

	[OneTimeSetUp]
	public static void OneTimeSetUp() => _services = new ServiceCollection().BuildServiceProvider();

	[OneTimeTearDown]
	public static void OneTimeTearDown() => _services.Dispose();

	private static CapabilityInvocation Invocation(string localId, string operation, object? arguments = null)
		=> new()
		{
			Kind = CapabilityKinds.ConfigFlow,
			LocalId = localId,
			Operation = operation,
			Arguments = arguments is null
				? null
				: JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options),
			CorrelationId = "correlation",
			Services = _services
		};

	private static ConfigFlowOAuthContextDto Oauth(string? code = null)
		=> new() { RedirectUri = "http://127.0.0.1:9696/callback", State = "state-1", AuthorizationCode = code };

	[Test]
	public void A_provider_declares_exactly_one_provider_local_id()
	{
		var integration = new TestConfigFlowIntegration(() => new TestConfigFlow());
		var handler = new ConfigFlowCapabilityHandler([integration], new PluginConfigFlowSessions(TimeProvider.System));

		var declared = handler.DeclareCapabilities();

		Assert.Multiple(() =>
		{
			Assert.That(declared, Has.Count.EqualTo(1));
			Assert.That(declared[0].LocalId, Is.EqualTo(ProviderCapabilityId.LocalId));
			Assert.That(declared[0].Kind, Is.EqualTo(CapabilityKinds.ConfigFlow));
		});
	}

	[Test]
	public void No_provider_declares_nothing()
		=> Assert.That(new ConfigFlowCapabilityHandler([], new PluginConfigFlowSessions(TimeProvider.System))
				.DeclareCapabilities(),
			Is.Empty);

	[Test]
	public async Task Describe_reports_allows_multiple_configurations()
	{
		var integration
			= new TestConfigFlowIntegration(() => new TestConfigFlow(), allowsMultipleConfigurations: false);
		var handler = new ConfigFlowCapabilityHandler([integration], new PluginConfigFlowSessions(TimeProvider.System));

		var result = await handler.InvokeAsync(
			Invocation(ProviderCapabilityId.LocalId, CapabilityOperations.ConfigFlow.Describe),
			CancellationToken.None);

		Assert.That(result.IsFailure, Is.False);
		var payload = result.Data!.Value.Deserialize<ConfigFlowDescribePayload>(PluginProtocolJson.Options);
		Assert.That(payload!.AllowsMultipleConfigurations, Is.False);
	}

	[Test]
	public async Task A_multi_step_flow_carries_state_on_the_same_plugin_side_instance()
	{
		var flow = new TestConfigFlow
		{
			ResultToReturn = ConfigFlowResult.Step(new ConfigFlowStep
				{ StepId = "one", Fields = [ActionParameter.Text("name")] })
		};
		flow.SubmitOverride = (stepId, input, _) => stepId switch
		{
			"one" => Task.FromResult(ConfigFlowResult.Step(new ConfigFlowStep
			{
				StepId = "two", Fields = [ActionParameter.Text("email")]
			})),
			"two" => Task.FromResult(ConfigFlowResult.Complete("Done")),
			_ => throw new InvalidOperationException()
		};

		var createCount = 0;
		var integration = new TestConfigFlowIntegration(() =>
		{
			createCount++;
			return flow;
		});
		var handler = new ConfigFlowCapabilityHandler([integration], new PluginConfigFlowSessions(TimeProvider.System));

		var start = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				CapabilityOperations.ConfigFlow.FlowStart,
				new FlowStartArguments
					{ SessionId = "session-1", OAuth = Oauth(), EntryTitle = "Streaming PC" }),
			CancellationToken.None);
		var startDto = start.Data!.Value.Deserialize<ConfigFlowResultDto>(PluginProtocolJson.Options)!;
		Assert.That(startDto.Kind, Is.EqualTo(nameof(ConfigFlowResultKind.Step)));
		Assert.That(startDto.NextStep!.StepId, Is.EqualTo("one"));

		var stepOne = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				CapabilityOperations.ConfigFlow.FlowSubmit,
				new FlowSubmitArguments
				{
					SessionId = "session-1", StepId = "one",
					Input = new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement("Ada") },
					OAuth = Oauth(),
					EntryTitle = "Streaming PC"
				}),
			CancellationToken.None);
		var stepOneDto = stepOne.Data!.Value.Deserialize<ConfigFlowResultDto>(PluginProtocolJson.Options)!;
		Assert.That(stepOneDto.NextStep!.StepId, Is.EqualTo("two"));

		var stepTwo = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				CapabilityOperations.ConfigFlow.FlowSubmit,
				new FlowSubmitArguments
				{
					SessionId = "session-1", StepId = "two",
					Input = new Dictionary<string, JsonElement>
						{ ["email"] = JsonSerializer.SerializeToElement("ada@example.com") },
					OAuth = Oauth(),
					EntryTitle = "Streaming PC"
				}),
			CancellationToken.None);
		var stepTwoDto = stepTwo.Data!.Value.Deserialize<ConfigFlowResultDto>(PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			// One instance served the whole session - the plugin-side session map, not the wire, is what
			// makes IConfigFlow's instance-per-session contract hold.
			Assert.That(createCount, Is.EqualTo(1));
			Assert.That(flow.Calls, Is.EqualTo(new[] { "start", "submit:one", "submit:two" }));
			Assert.That(flow.LastInput!["email"], Is.EqualTo("ada@example.com"));
			Assert.That(((IConfigFlowEntryContext)flow.LastContext!).EntryTitle, Is.EqualTo("Streaming PC"));
			Assert.That(stepTwoDto.Kind, Is.EqualTo(nameof(ConfigFlowResultKind.Complete)));
			Assert.That(stepTwoDto.EntryTitle, Is.EqualTo("Done"));
		});
	}

	[Test]
	public async Task Every_result_variant_round_trips_to_the_dto()
	{
		async Task<ConfigFlowResultDto> RunAsync(ConfigFlowResult resultToReturn)
		{
			var flow = new TestConfigFlow { ResultToReturn = resultToReturn };
			var handler = new ConfigFlowCapabilityHandler([new TestConfigFlowIntegration(() => flow)],
				new PluginConfigFlowSessions(TimeProvider.System));

			var result = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
					CapabilityOperations.ConfigFlow.FlowStart,
					new FlowStartArguments { SessionId = Guid.NewGuid().ToString(), OAuth = Oauth() }),
				CancellationToken.None);

			return result.Data!.Value.Deserialize<ConfigFlowResultDto>(PluginProtocolJson.Options)!;
		}

		var step = await RunAsync(ConfigFlowResult.Step(new ConfigFlowStep { StepId = "s", Fields = [] }));
		var error = await RunAsync(ConfigFlowResult.Error(new ConfigFlowStep { StepId = "s", Fields = [] },
			"bad input",
			new Dictionary<string, LocalizedText> { ["field"] = "required" }));
		var complete = await RunAsync(ConfigFlowResult.Complete("Entry",
			new Dictionary<string, ConfigFlowValue>
				{ ["token"] = ConfigFlowValue.Secret("shh"), ["region"] = ConfigFlowValue.Plain("eu") }));
		var external = await RunAsync(ConfigFlowResult.External("https://example.com/authorize", "resume"));

		Assert.Multiple(() =>
		{
			Assert.That(step.Kind, Is.EqualTo(nameof(ConfigFlowResultKind.Step)));
			Assert.That(step.NextStep!.StepId, Is.EqualTo("s"));

			Assert.That(error.Kind, Is.EqualTo(nameof(ConfigFlowResultKind.Error)));
			Assert.That(error.ErrorMessage?.Literal, Is.EqualTo("bad input"));
			Assert.That(error.FieldErrors!["field"].Literal, Is.EqualTo("required"));

			Assert.That(complete.Kind, Is.EqualTo(nameof(ConfigFlowResultKind.Complete)));
			Assert.That(complete.EntryTitle, Is.EqualTo("Entry"));
			Assert.That(complete.Values!["token"].IsSecret, Is.True);
			Assert.That(complete.Values!["region"].Value, Is.EqualTo("eu"));

			Assert.That(external.Kind, Is.EqualTo(nameof(ConfigFlowResultKind.External)));
			Assert.That(external.ExternalUrl, Is.EqualTo("https://example.com/authorize"));
			Assert.That(external.ResumeStepId, Is.EqualTo("resume"));
		});
	}

	[Test]
	public async Task Submitting_an_unknown_session_is_unavailable()
	{
		var handler = new ConfigFlowCapabilityHandler([new TestConfigFlowIntegration(() => new TestConfigFlow())],
			new PluginConfigFlowSessions(TimeProvider.System));

		var result = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				CapabilityOperations.ConfigFlow.FlowSubmit,
				new FlowSubmitArguments
				{
					SessionId = "gone", StepId = "one", Input = new Dictionary<string, JsonElement>(), OAuth = Oauth()
				}),
			CancellationToken.None);

		Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
	}

	[Test]
	public async Task Abandoning_a_session_disposes_and_releases_the_instance()
	{
		var flow = new DisposableTestConfigFlow();
		var handler = new ConfigFlowCapabilityHandler([new TestConfigFlowIntegration(() => flow)],
			new PluginConfigFlowSessions(TimeProvider.System));

		await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				CapabilityOperations.ConfigFlow.FlowStart,
				new FlowStartArguments { SessionId = "session-1", OAuth = Oauth() }),
			CancellationToken.None);

		var abandon = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				CapabilityOperations.ConfigFlow.FlowAbandon,
				new FlowAbandonArguments { SessionId = "session-1" }),
			CancellationToken.None);

		var afterAbandon = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				CapabilityOperations.ConfigFlow.FlowSubmit,
				new FlowSubmitArguments
				{
					SessionId = "session-1", StepId = "one", Input = new Dictionary<string, JsonElement>(),
					OAuth = Oauth()
				}),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(abandon.IsFailure, Is.False);
			Assert.That(flow.Disposed, Is.True);
			Assert.That(afterAbandon.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
		});
	}

	[Test]
	public async Task Abandoning_an_unknown_session_is_not_an_error()
	{
		var handler = new ConfigFlowCapabilityHandler([new TestConfigFlowIntegration(() => new TestConfigFlow())],
			new PluginConfigFlowSessions(TimeProvider.System));

		var result = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				CapabilityOperations.ConfigFlow.FlowAbandon,
				new FlowAbandonArguments { SessionId = "never-started" }),
			CancellationToken.None);

		Assert.That(result.IsFailure, Is.False);
	}

	[Test]
	public async Task An_idle_session_is_released_when_the_clock_passes_the_timeout()
	{
		var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
		var flow = new DisposableTestConfigFlow();
		var handler = new ConfigFlowCapabilityHandler([new TestConfigFlowIntegration(() => flow)],
			new PluginConfigFlowSessions(time));

		await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				CapabilityOperations.ConfigFlow.FlowStart,
				new FlowStartArguments { SessionId = "session-1", OAuth = Oauth() }),
			CancellationToken.None);

		time.Advance(ConfigFlowCapabilityHandler.IdleTimeout + TimeSpan.FromSeconds(1));

		// The sweep runs lazily at the top of the next invocation - a describe is enough to trigger it,
		// without needing to touch the session under test.
		await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId, CapabilityOperations.ConfigFlow.Describe),
			CancellationToken.None);

		var afterTimeout = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				CapabilityOperations.ConfigFlow.FlowSubmit,
				new FlowSubmitArguments
				{
					SessionId = "session-1", StepId = "one", Input = new Dictionary<string, JsonElement>(),
					OAuth = Oauth()
				}),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(flow.Disposed, Is.True);
			Assert.That(afterTimeout.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
		});
	}

	[Test]
	public async Task An_unknown_local_id_is_unavailable_and_an_unknown_operation_is_unsupported()
	{
		var handler = new ConfigFlowCapabilityHandler([new TestConfigFlowIntegration(() => new TestConfigFlow())],
			new PluginConfigFlowSessions(TimeProvider.System));

		var unknownLocalId = await handler.InvokeAsync(Invocation("nope",
				CapabilityOperations.ConfigFlow.FlowAbandon,
				new FlowAbandonArguments { SessionId = "x" }),
			CancellationToken.None);
		var unknownOperation =
			await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId, "rewind"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(unknownLocalId.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(unknownOperation.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
		});
	}
}
