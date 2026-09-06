using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.Actions;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class ActionsCapabilityHandlerTests
{
	// A real IHostInvoker, backed by a PluginConnectionState with no active connection - matching a
	// capability handler running out of process before (or without) a live host connection. Execute
	// itself never calls it; only building a RemoteActionInteractions needs it resolvable.
	private static ServiceProvider _services = null!;

	[OneTimeSetUp]
	public static void OneTimeSetUp()
		=> _services = new ServiceCollection()
			.AddSingleton<IHostInvoker>(new HostInvoker(new PluginConnectionState(),
				TimeProvider.System,
				Serilog.Core.Logger.None))
			.BuildServiceProvider();

	[OneTimeTearDown]
	public static void OneTimeTearDown() => _services.Dispose();

	private static CapabilityInvocation Invocation(
		string localId,
		string operation = "execute",
		object? arguments = null)
		=> new()
		{
			Kind = CapabilityKinds.Actions,
			LocalId = localId,
			Operation = operation,
			Arguments = arguments is null
				? null
				: JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options),
			CorrelationId = "correlation",
			Services = _services
		};

	private static ActionsCapabilityHandler Handler(params IPluginIntegration[] integrations) => new(integrations);

	[Test]
	public void Every_registered_action_is_declared()
	{
		var handler = Handler(new TestIntegration(new TestAction("play"), new TestAction("pause")),
			new TestIntegration(new TestAction("skip")));

		var declared = handler.DeclareCapabilities();

		Assert.Multiple(() =>
		{
			Assert.That(declared.Select(capability => capability.LocalId),
				Is.EquivalentTo(new[] { "play", "pause", "skip" }));
			Assert.That(declared, Has.All.Property(nameof(DeclaredCapability.Kind)).EqualTo(CapabilityKinds.Actions));
		});
	}

	[Test]
	public void An_action_restricted_to_other_platforms_is_not_declared()
	{
		var otherPlatforms = MacroDeckPlatform.All & ~MacroDeckIntegrationAttribute.Current;
		var handler = Handler(new TestIntegration(new TestAction("hibernate") { Platforms = otherPlatforms },
			new TestAction("play")));

		var declared = handler.DeclareCapabilities();

		Assert.That(declared.Select(capability => capability.LocalId), Is.EquivalentTo(new[] { "play" }));
	}

	[Test]
	public async Task Invoking_an_action_restricted_to_other_platforms_is_unavailable()
	{
		var otherPlatforms = MacroDeckPlatform.All & ~MacroDeckIntegrationAttribute.Current;
		var handler = Handler(new TestIntegration(new TestAction("hibernate") { Platforms = otherPlatforms }));

		var result = await handler.InvokeAsync(Invocation("hibernate"), CancellationToken.None);

		Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
	}

	[Test]
	public async Task A_successful_action_succeeds()
	{
		var handler = Handler(new TestIntegration(new TestAction("play")));

		var result = await handler.InvokeAsync(Invocation("play"), CancellationToken.None);

		Assert.That(result.IsFailure, Is.False);
	}

	[TestCase(ActionResultStatus.Succeeded)]
	[TestCase(ActionResultStatus.Accepted)]
	public async Task An_expected_provider_state_round_trips_on_successful_results(ActionResultStatus status)
	{
		var action = new TestAction("play",
			_ => Task.FromResult(status == ActionResultStatus.Accepted
				? ActionResult.Accepted("Waiting for confirmation.", "playing")
				: ActionResult.Success("playing")));
		var result = await Handler(new TestIntegration(action)).InvokeAsync(Invocation("play"), CancellationToken.None);
		var payload = result.Data!.Value.Deserialize<ActionExecuteResult>(PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(payload!.ExpectedStateId, Is.EqualTo("playing"));
			Assert.That(payload.Accepted, Is.EqualTo(status == ActionResultStatus.Accepted));
		});
	}

	[Test]
	public async Task A_failed_action_carries_its_own_code_and_message()
	{
		var handler = Handler(new TestIntegration(new TestAction("play",
			_ => Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected, "Spotify is not connected.")))));

		var result = await handler.InvokeAsync(Invocation("play"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error!.Code, Is.EqualTo(ActionErrorCodes.NotConnected));
			Assert.That(result.Error!.Message, Is.EqualTo("Spotify is not connected."));
		});
	}

	[Test]
	public async Task An_unknown_action_is_unavailable_rather_than_unsupported()
	{
		// The kind is implemented, this particular capability is not - which is the distinction the
		// two error codes exist to draw.
		var handler = Handler(new TestIntegration(new TestAction("play")));

		var result = await handler.InvokeAsync(Invocation("nope"), CancellationToken.None);

		Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
	}

	[Test]
	public async Task An_unknown_operation_is_unsupported()
	{
		var handler = Handler(new TestIntegration(new TestAction("play")));

		var result = await handler.InvokeAsync(Invocation("play", operation: "rewind"), CancellationToken.None);

		Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
	}

	[Test]
	public async Task Parameters_are_bound_to_the_shape_the_action_declared()
	{
		var action = new TestAction("play")
		{
			Parameters =
			[
				ActionParameter.Number("volume"),
				ActionParameter.Toggle("shuffle"),
				ActionParameter.Text("device")
			]
		};

		var handler = Handler(new TestIntegration(action));

		// Sent as strings, as a flow that stored them from a text field would: the declared type is
		// what decides the CLR shape, because that is what every in-process executor already assumes.
		await handler.InvokeAsync(Invocation("play",
				arguments: new
				{
					parameters = new { volume = "50", shuffle = "true", device = "kitchen" }
				}),
			CancellationToken.None);

		var parameters = action.LastContext!.Parameters;

		Assert.Multiple(() =>
		{
			Assert.That(parameters["volume"], Is.EqualTo(50d));
			Assert.That(parameters["shuffle"], Is.EqualTo(true));
			Assert.That(parameters["device"], Is.EqualTo("kitchen"));
		});
	}

	[Test]
	public async Task An_unbindable_value_reaches_the_action_unchanged_rather_than_being_dropped()
	{
		var action = new TestAction("play") { Parameters = [ActionParameter.Number("volume")] };
		var handler = Handler(new TestIntegration(action));

		await handler.InvokeAsync(Invocation("play", arguments: new { parameters = new { volume = "loud" } }),
			CancellationToken.None);

		// The action's own validation reports this with the detail it has; guessing here would only
		// turn a clear error into a confusing one.
		Assert.That(action.LastContext!.Parameters["volume"], Is.EqualTo("loud"));
	}

	[Test]
	public async Task The_invocation_token_reaches_the_executor()
	{
		using var cancellation = new CancellationTokenSource();
		var action = new TestAction("play");
		var handler = Handler(new TestIntegration(action));

		await handler.InvokeAsync(Invocation("play"), cancellation.Token);

		Assert.That(action.LastContext!.CancellationToken, Is.EqualTo(cancellation.Token));
	}

	[Test]
	public async Task Client_interaction_is_a_real_proxy_scoped_to_this_invocation()
	{
		var action = new TestAction("play");
		var handler = Handler(new TestIntegration(action));

		await handler.InvokeAsync(Invocation("play"), CancellationToken.None);

		// #413 step 6: a picker request is now a real host.invoke, proxied through RemoteActionInteractions
		// rather than left null - see ActionsCapabilityHandler.ExecuteAsync.
		Assert.That(action.LastContext!.Interactions, Is.Not.Null);
	}

	[Test]
	public async Task Origin_and_owner_widget_ids_are_bound_from_the_wire()
	{
		var action = new TestAction("play");
		var handler = Handler(new TestIntegration(action));

		await handler.InvokeAsync(
			Invocation("play", arguments: new { originClientId = "client-1", ownerWidgetId = "widget-1" }),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(action.LastContext!.OriginClientId, Is.EqualTo("client-1"));
			Assert.That(action.LastContext!.OwnerWidgetId, Is.EqualTo("widget-1"));
		});
	}

	[Test]
	public async Task Describe_lists_every_action_with_its_parameters()
	{
		var action = new TestAction("play") { Parameters = [ActionParameter.Number("volume")] };
		var handler = Handler(new TestIntegration(action));

		var result = await handler.InvokeAsync(Invocation("play", operation: "describe"), CancellationToken.None);

		Assert.That(result.IsFailure, Is.False);
		var catalog = result.Data!.Value
			.Deserialize<ActionCatalogPayload>(PluginProtocolJson.Options);
		Assert.Multiple(() =>
		{
			Assert.That(catalog!.Actions, Has.Count.EqualTo(1));
			Assert.That(catalog.Actions[0].LocalId, Is.EqualTo("play"));
			Assert.That(catalog.Actions[0].Parameters, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task An_action_without_dynamic_options_is_unavailable_for_the_options_operation()
	{
		var handler = Handler(new TestIntegration(new TestAction("play")));

		var result = await handler.InvokeAsync(Invocation("play", operation: "options"), CancellationToken.None);

		Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
	}
}
