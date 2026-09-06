using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.VirtualProfiles;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.VirtualProfiles;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Profiles;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class VirtualProfilesCapabilityHandlerTests
{
	private static ServiceProvider _services = null!;

	[OneTimeSetUp]
	public static void OneTimeSetUp() => _services = new ServiceCollection().BuildServiceProvider();

	[OneTimeTearDown]
	public static void OneTimeTearDown() => _services.Dispose();

	private static CapabilityInvocation Invocation(string localId, string operation, object? arguments = null)
		=> new()
		{
			Kind = CapabilityKinds.VirtualProfiles,
			LocalId = localId,
			Operation = operation,
			Arguments = arguments is null
				? null
				: JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options),
			CorrelationId = "correlation",
			Services = _services
		};

	private static VirtualProfileDescriptor CarThing()
		=> new("car-thing",
			"Car Thing",
			ProfileLayout.Grid(2, 4),
			[new VirtualFolderDescriptor("main", "Main", [new VirtualWidgetDescriptor("play", "ActionButton", 0, 0)])]);

	[Test]
	public void A_provider_declares_exactly_one_provider_local_id()
	{
		var integration = new TestProfileIntegration("Spotify", [CarThing()]);
		var handler = new VirtualProfilesCapabilityHandler([integration], TestMetadata.Default);

		var declared = handler.DeclareCapabilities();

		Assert.Multiple(() =>
		{
			Assert.That(declared, Has.Count.EqualTo(1));
			Assert.That(declared[0].LocalId, Is.EqualTo(ProviderCapabilityId.LocalId));
			Assert.That(declared[0].Kind, Is.EqualTo(CapabilityKinds.VirtualProfiles));
		});
	}

	[Test]
	public void No_provider_declares_nothing()
		=> Assert.That(new VirtualProfilesCapabilityHandler([], TestMetadata.Default).DeclareCapabilities(), Is.Empty);

	[Test]
	public async Task Describe_reports_the_provider_name_and_profiles()
	{
		var integration = new TestProfileIntegration("Spotify", [CarThing()]);
		var handler = new VirtualProfilesCapabilityHandler([integration], TestMetadata.Default);

		var result = await handler.InvokeAsync(
			Invocation(ProviderCapabilityId.LocalId, CapabilityOperations.VirtualProfiles.Describe),
			CancellationToken.None);

		Assert.That(result.IsFailure, Is.False);
		var payload = result.Data!.Value.Deserialize<VirtualProfilesDescribePayload>(PluginProtocolJson.Options);
		Assert.Multiple(() =>
		{
			Assert.That(payload!.ProviderName, Is.EqualTo("Spotify"));
			Assert.That(payload.Profiles.Single().Id, Is.EqualTo("car-thing"));
			Assert.That(payload.Profiles.Single().Folders.Single().Widgets.Single().Id, Is.EqualTo("play"));
		});
	}

	[Test]
	public async Task Widget_interaction_forwards_the_profile_id_exactly_as_received()
	{
		var integration = new TestProfileIntegration("Spotify", [CarThing()]);
		var handler = new VirtualProfilesCapabilityHandler([integration], TestMetadata.Default);

		var result = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				CapabilityOperations.VirtualProfiles.WidgetInteraction,
				new { profileId = string.Empty, folderId = "main", widgetId = "play", triggerType = "press" }),
			CancellationToken.None);

		Assert.That(result.IsFailure, Is.False);
		Assert.Multiple(() =>
		{
			Assert.That(integration.LastInteraction!.Value.ProfileId, Is.EqualTo(string.Empty));
			Assert.That(integration.LastInteraction!.Value.FolderId, Is.EqualTo("main"));
			Assert.That(integration.LastInteraction!.Value.WidgetId, Is.EqualTo("play"));
			Assert.That(integration.LastInteraction!.Value.Interaction.TriggerType, Is.EqualTo("press"));
		});
	}

	[Test]
	public async Task Widget_interaction_against_an_unknown_widget_is_unavailable()
	{
		var integration = new TestProfileIntegration("Spotify", [CarThing()]);
		var handler = new VirtualProfilesCapabilityHandler([integration], TestMetadata.Default);

		var result = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				CapabilityOperations.VirtualProfiles.WidgetInteraction,
				new { profileId = string.Empty, folderId = "main", widgetId = "gone", triggerType = "press" }),
			CancellationToken.None);

		Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
	}

	[Test]
	public async Task An_unknown_local_id_is_unavailable_and_an_unknown_operation_is_unsupported()
	{
		var integration = new TestProfileIntegration("Spotify", [CarThing()]);
		var handler = new VirtualProfilesCapabilityHandler([integration], TestMetadata.Default);

		var unknownLocalId =
			await handler.InvokeAsync(Invocation("nope", CapabilityOperations.VirtualProfiles.Profiles),
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
