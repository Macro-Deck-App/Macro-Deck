using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.VirtualProfiles;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.VirtualProfiles;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Profiles;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class VirtualProfilesContractTests : CapabilityContractFixture
{
	private static DeclaredCapability Provider()
		=> new()
		{
			Kind = CapabilityKinds.VirtualProfiles,
			LocalId = ProviderCapabilityId.LocalId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};

	private static VirtualProfileDescriptor CarThing()
		=> new("car-thing",
			"Car Thing",
			ProfileLayout.Grid(2, 4),
			[new VirtualFolderDescriptor("main", "Main", [new VirtualWidgetDescriptor("play", "ActionButton", 0, 0)])]);

	[Test]
	public async Task Declare_registers_an_adapter_that_passes_the_capability_validator()
	{
		var integration = await ConnectAsync([
				new VirtualProfilesCapabilityHandler([new TestProfileIntegration("Spotify", [CarThing()])],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.VirtualProfiles]);

		Assert.Multiple(() =>
		{
			Assert.That(IntegrationCapabilityValidator.Validate(integration), Is.Empty);
			Assert.That(((IProfileProvider)integration).GetProfiles().Select(p => p.Id), Does.Contain("car-thing"));
		});
	}

	[Test]
	public async Task Every_operation_round_trips_to_the_sdk_type_the_host_contract_requires()
	{
		var profileIntegration = new TestProfileIntegration("Spotify", [CarThing()]);
		var integration = await ConnectAsync(
			[new VirtualProfilesCapabilityHandler([profileIntegration], TestMetadata.Default)],
			[Provider()],
			[CapabilityKinds.VirtualProfiles]);

		var provider = (IProfileProvider)integration;

		Assert.Multiple(() =>
		{
			Assert.That(provider.ProviderName, Is.EqualTo("Spotify"));
			var profile = provider.GetProfiles().Single();
			Assert.That(profile.Id, Is.EqualTo("car-thing"));
			Assert.That(profile.Layout.Rows, Is.EqualTo(2));
			Assert.That(profile.Folders.Single().Widgets.Single().Id, Is.EqualTo("play"));
		});

		await provider.HandleWidgetInteractionAsync(string.Empty, "main", "play", new WidgetInteraction("press"));

		Assert.Multiple(() =>
		{
			Assert.That(profileIntegration.LastInteraction!.Value.ProfileId, Is.EqualTo(string.Empty));
			Assert.That(profileIntegration.LastInteraction!.Value.FolderId, Is.EqualTo("main"));
			Assert.That(profileIntegration.LastInteraction!.Value.WidgetId, Is.EqualTo("play"));
			Assert.That(profileIntegration.LastInteraction!.Value.Interaction.TriggerType, Is.EqualTo("press"));
		});
	}

	[Test]
	public async Task The_profiles_operation_round_trips_the_same_profile_list_describe_carries()
	{
		await ConnectAsync([
				new VirtualProfilesCapabilityHandler([new TestProfileIntegration("Spotify", [CarThing()])],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.VirtualProfiles]);

		var raw = await InvokeRawAsync(CapabilityKinds.VirtualProfiles,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.VirtualProfiles.Profiles);
		var result = raw!.Value.Deserialize<VirtualProfilesResult>(PluginProtocolJson.Options);

		Assert.That(result!.Profiles.Select(p => p.Id), Is.EqualTo(new[] { "car-thing" }));
	}

	[Test]
	public async Task A_timeout_degrades_to_a_silent_no_op_never_an_exception()
	{
		var integration = await ConnectAsync([
				new VirtualProfilesCapabilityHandler([new NeverRepliesProfileIntegration("Spotify", [CarThing()])],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.VirtualProfiles]);

		var provider = (IProfileProvider)integration;
		var interactionTask
			= provider.HandleWidgetInteractionAsync(string.Empty, "main", "play", new WidgetInteraction("press"));

		Time.Advance(ProtocolTimeouts.CapabilityInvoke);

		Assert.DoesNotThrowAsync(async () => await interactionTask);
	}

	[Test]
	public async Task A_dropped_connection_degrades_to_a_silent_no_op_never_an_exception()
	{
		var integration = await ConnectAsync([
				new VirtualProfilesCapabilityHandler([new TestProfileIntegration("Spotify", [CarThing()])],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.VirtualProfiles]);

		Disconnect();

		var provider = (IProfileProvider)integration;
		Assert.DoesNotThrowAsync(async ()
			=> await provider.HandleWidgetInteractionAsync(string.Empty,
				"main",
				"play",
				new WidgetInteraction("press")));
	}

	[Test]
	public async Task Caller_cancellation_puts_capability_cancel_on_the_wire()
	{
		await ConnectAsync([
				new VirtualProfilesCapabilityHandler([new NeverRepliesProfileIntegration("Spotify", [CarThing()])],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.VirtualProfiles]);

		using var cts = new CancellationTokenSource();
		var invokeTask = InvokeRawAsync(CapabilityKinds.VirtualProfiles,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.VirtualProfiles.WidgetInteraction,
			new { profileId = "", folderId = "main", widgetId = "play", triggerType = "press" },
			cts.Token);

		await cts.CancelAsync();

		Assert.CatchAsync<OperationCanceledException>(async () => await invokeTask);
		Assert.That(Link.SentByHost.Any(envelope => envelope.Type == MessageTypes.CapabilityCancel), Is.True);
	}

	[Test]
	public async Task A_throwing_handler_yields_a_redacted_internal_error()
	{
		await ConnectAsync([
				new VirtualProfilesCapabilityHandler([new ThrowingProfileIntegration("Spotify", [CarThing()])],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.VirtualProfiles]);

		var exception = Assert.CatchAsync<RemoteCapabilityException>(async () => await InvokeRawAsync(
			CapabilityKinds.VirtualProfiles,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.VirtualProfiles.WidgetInteraction,
			new { profileId = "", folderId = "main", widgetId = "play", triggerType = "press" }));

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
				new VirtualProfilesCapabilityHandler([new TestProfileIntegration("Spotify", [CarThing()])],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.VirtualProfiles]);

		var unknownLocalId = Assert.CatchAsync<RemoteCapabilityException>(async () => await InvokeRawAsync(
			CapabilityKinds.VirtualProfiles,
			"nope",
			CapabilityOperations.VirtualProfiles.Profiles));
		var unknownOperation = Assert.CatchAsync<RemoteCapabilityException>(async () => await InvokeRawAsync(
			CapabilityKinds.VirtualProfiles,
			ProviderCapabilityId.LocalId,
			"rewind"));

		Assert.Multiple(() =>
		{
			Assert.That(unknownLocalId!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(unknownOperation!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
		});
	}

	private sealed class NeverRepliesProfileIntegration(
		string providerName,
		IReadOnlyList<VirtualProfileDescriptor> profiles)
		: IPluginIntegration, IProfileProvider
	{
		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public string ProviderName { get; } = providerName;

		public IReadOnlyList<VirtualProfileDescriptor> GetProfiles() => profiles;

		public async Task HandleWidgetInteractionAsync(string profileId,
			string folderId,
			string widgetId,
			WidgetInteraction interaction)
			=> await Task.Delay(Timeout.Infinite, CancellationToken.None);
	}

	private sealed class ThrowingProfileIntegration(
		string providerName,
		IReadOnlyList<VirtualProfileDescriptor> profiles)
		: IPluginIntegration, IProfileProvider
	{
		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public string ProviderName { get; } = providerName;

		public IReadOnlyList<VirtualProfileDescriptor> GetProfiles() => profiles;

		public Task HandleWidgetInteractionAsync(string profileId,
			string folderId,
			string widgetId,
			WidgetInteraction interaction)
			=> throw new InvalidOperationException("boom: token=abc123");
	}
}
