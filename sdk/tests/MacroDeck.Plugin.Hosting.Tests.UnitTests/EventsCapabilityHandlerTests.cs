using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.Events;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Capabilities.Events;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class EventsCapabilityHandlerTests
{
	private static ServiceProvider _services = null!;

	[OneTimeSetUp]
	public static void OneTimeSetUp() => _services = new ServiceCollection().BuildServiceProvider();

	[OneTimeTearDown]
	public static void OneTimeTearDown() => _services.Dispose();

	private static CapabilityInvocation Invocation(string localId, string operation, object? arguments = null)
		=> new()
		{
			Kind = CapabilityKinds.Events,
			LocalId = localId,
			Operation = operation,
			Arguments = arguments is null
				? null
				: JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options),
			CorrelationId = "correlation",
			Services = _services
		};

	private static EventDefinition SceneChanged()
		=> new() { Id = "scene-changed", Name = "Scene changed" };

	[Test]
	public void A_provider_declares_exactly_one_provider_local_id()
	{
		var integration = new TestEventIntegration("OBS Studio", SceneChanged());
		var handler = new EventsCapabilityHandler([integration], TestMetadata.Default);

		var declared = handler.DeclareCapabilities();

		Assert.Multiple(() =>
		{
			Assert.That(declared, Has.Count.EqualTo(1));
			Assert.That(declared[0].LocalId, Is.EqualTo(ProviderCapabilityId.LocalId));
			Assert.That(declared[0].Kind, Is.EqualTo(CapabilityKinds.Events));
		});
	}

	[Test]
	public void No_provider_declares_nothing()
	{
		var handler = new EventsCapabilityHandler([], TestMetadata.Default);

		Assert.That(handler.DeclareCapabilities(), Is.Empty);
	}

	[Test]
	public async Task Describe_reports_the_provider_name_and_merged_event_catalogue()
	{
		var integration = new TestEventIntegration("OBS Studio", SceneChanged());
		var handler = new EventsCapabilityHandler([integration], TestMetadata.Default);

		var result = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId, "describe"),
			CancellationToken.None);

		Assert.That(result.IsFailure, Is.False);
		var payload = result.Data!.Value.Deserialize<EventCatalogPayload>(PluginProtocolJson.Options);
		Assert.Multiple(() =>
		{
			Assert.That(payload!.ProviderName, Is.EqualTo("OBS Studio"));
			Assert.That(payload.Events, Has.Count.EqualTo(1));
			Assert.That(payload.Events[0].LocalId, Is.EqualTo("scene-changed"));
			Assert.That(payload.HasDynamicEventOptions, Is.False);
		});
	}

	[Test]
	public async Task Describe_reports_dynamic_event_options_when_the_integration_offers_them()
	{
		var integration = new TestDynamicEventIntegration("OBS Studio", SceneChanged());
		var handler = new EventsCapabilityHandler([integration], TestMetadata.Default);

		var result = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId, "describe"),
			CancellationToken.None);

		var payload = result.Data!.Value.Deserialize<EventCatalogPayload>(PluginProtocolJson.Options);
		Assert.That(payload!.HasDynamicEventOptions, Is.True);
	}

	[Test]
	public async Task Options_routes_to_the_dynamic_provider_for_the_named_event()
	{
		var integration = new TestDynamicEventIntegration("OBS Studio",
			new EventDefinition
			{
				Id = "scene-changed", Name = "Scene changed", ConfigurationParameters = [ActionParameter.Text("scene")]
			})
		{
			ResultToReturn = new DynamicOptionsResult
			{
				Options = [new ActionParameterOption { Value = "main", Label = "Main" }], AllowsCustomValue = true
			}
		};
		var handler = new EventsCapabilityHandler([integration], TestMetadata.Default);

		var result = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				"options",
				new { eventId = "scene-changed", parameterName = "scene" }),
			CancellationToken.None);

		Assert.That(result.IsFailure, Is.False);
		var payload = result.Data!.Value.Deserialize<DynamicOptionsResultDto>(PluginProtocolJson.Options);
		Assert.Multiple(() =>
		{
			Assert.That(payload!.Options.Select(o => o.Value), Is.EqualTo(new[] { "main" }));
			Assert.That(payload.AllowsCustomValue, Is.True);
			Assert.That(integration.LastContext!.EventId, Is.EqualTo("scene-changed"));
			Assert.That(integration.LastContext!.ParameterName, Is.EqualTo("scene"));
		});
	}

	[Test]
	public async Task Options_for_an_event_without_dynamic_options_is_unavailable()
	{
		var integration = new TestEventIntegration("OBS Studio", SceneChanged());
		var handler = new EventsCapabilityHandler([integration], TestMetadata.Default);

		var result = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				"options",
				new { eventId = "scene-changed", parameterName = "scene" }),
			CancellationToken.None);

		Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
	}

	[Test]
	public async Task An_unknown_local_id_is_unavailable_and_an_unknown_operation_is_unsupported()
	{
		var integration = new TestEventIntegration("OBS Studio", SceneChanged());
		var handler = new EventsCapabilityHandler([integration], TestMetadata.Default);

		// describe ignores the local id (see EventsCapabilityHandler's remarks), so "unknown local id" is
		// only meaningful for an operation the provider-local id actually gates, like options.
		var unknownLocalId = await handler.InvokeAsync(
			Invocation("nope", "options", new { eventId = "scene-changed", parameterName = "scene" }),
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
