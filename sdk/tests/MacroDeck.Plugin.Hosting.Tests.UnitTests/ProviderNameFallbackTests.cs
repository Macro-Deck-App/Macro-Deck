using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.Events;
using MacroDeck.Plugin.Hosting.Capabilities.MusicPlayer;
using MacroDeck.Plugin.Hosting.Capabilities.VirtualProfiles;
using MacroDeck.Plugin.Hosting.Capabilities.Weather;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Events;
using MacroDeck.Plugin.Protocol.Capabilities.MusicPlayer;
using MacroDeck.Plugin.Protocol.Capabilities.VirtualProfiles;
using MacroDeck.Plugin.Protocol.Capabilities.Weather;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Profiles;
using MacroDeck.Sdk.Weather;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

/// <summary>
/// <c>ProviderName</c> is optional as of #560: a plugin that does not state one is described by the name
/// in its <c>manifest.json</c>, so the plugin's name lives in exactly one place. The four provider-shaped
/// kinds each merge it separately, which is why each is covered here rather than one standing in for the
/// rest.
///
/// <para>
/// The counterexample - a provider that *does* state a name still wins - is already covered per kind by
/// the existing <c>Describe_reports_the_provider_name...</c> tests, which assert a stated name while
/// <see cref="TestMetadata.Default" /> carries a different one. Without that pair, a handler that ignored
/// the provider entirely and always reported the manifest name would pass everything here.
/// </para>
/// </summary>
[TestFixture]
public class ProviderNameFallbackTests
{
	private static ServiceProvider _services = null!;

	[OneTimeSetUp]
	public static void OneTimeSetUp() => _services = new ServiceCollection().BuildServiceProvider();

	[OneTimeTearDown]
	public static void OneTimeTearDown() => _services.Dispose();

	[Test]
	public async Task Events_describe_reports_the_manifest_name_when_the_provider_states_none()
	{
		var handler = new EventsCapabilityHandler([new NamelessEventIntegration()], TestMetadata.Default);

		var payload = await Describe<EventCatalogPayload>(handler, CapabilityKinds.Events, "describe");

		Assert.That(payload.ProviderName, Is.EqualTo(TestMetadata.Default.Name));
	}

	[Test]
	public async Task Weather_describe_reports_the_manifest_name_when_the_provider_states_none()
	{
		var handler = new WeatherCapabilityHandler([new NamelessWeatherIntegration()], TestMetadata.Default);

		var payload = await Describe<WeatherDescribePayload>(handler,
			CapabilityKinds.Weather,
			CapabilityOperations.Weather.Describe);

		Assert.That(payload.ProviderName, Is.EqualTo(TestMetadata.Default.Name));
	}

	[Test]
	public async Task Music_player_describe_reports_the_manifest_name_when_the_provider_states_none()
	{
		var handler = new MusicPlayerCapabilityHandler([new NamelessMusicPlayerIntegration()],
			TestMetadata.Default,
			new FakeAssetUploader());

		var payload = await Describe<MusicPlayerDescribePayload>(handler,
			CapabilityKinds.MusicPlayer,
			CapabilityOperations.MusicPlayer.Describe);

		Assert.That(payload.ProviderName, Is.EqualTo(TestMetadata.Default.Name));
	}

	[Test]
	public async Task Virtual_profiles_describe_reports_the_manifest_name_when_the_provider_states_none()
	{
		var handler = new VirtualProfilesCapabilityHandler([new NamelessProfileIntegration()],
			TestMetadata.Default);

		var payload = await Describe<VirtualProfilesDescribePayload>(handler,
			CapabilityKinds.VirtualProfiles,
			CapabilityOperations.VirtualProfiles.Describe);

		Assert.That(payload.ProviderName, Is.EqualTo(TestMetadata.Default.Name));
	}

	private static async Task<TPayload> Describe<TPayload>(
		ICapabilityHandler handler,
		string kind,
		string operation)
	{
		var result = await handler.InvokeAsync(new CapabilityInvocation
			{
				Kind = kind,
				LocalId = ProviderCapabilityId.LocalId,
				Operation = operation,
				CorrelationId = "correlation",
				Services = _services
			},
			CancellationToken.None);

		Assert.That(result.IsFailure, Is.False, $"{kind}/{operation} failed");
		return result.Data!.Value.Deserialize<TPayload>(PluginProtocolJson.Options)!;
	}

	/// <summary>
	/// These deliberately do not declare <c>ProviderName</c> at all - that is the whole condition under
	/// test, and the shared fakes in <c>Support/TestProviders.cs</c> all state one.
	/// </summary>
	private abstract class NamelessIntegration : IPluginIntegration
	{
		public IReadOnlyList<IActionDefinition> Actions => [];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;
	}

	private sealed class NamelessEventIntegration : NamelessIntegration, IEventProvider
	{
		public IReadOnlyList<EventDefinition> EventDefinitions => [];
	}

	private sealed class NamelessWeatherIntegration : NamelessIntegration, IWeatherProvider
	{
		public IReadOnlyList<WeatherStationInstance> GetInstances() => [];

		public IWeatherStation? GetStation(string instanceId) => null;
	}

	private sealed class NamelessMusicPlayerIntegration : NamelessIntegration, IMusicPlayerProvider
	{
		public IReadOnlyList<MusicPlayerInstance> GetInstances() => [];

		public IMusicPlayer? GetPlayer(string instanceId) => null;
	}

	private sealed class NamelessProfileIntegration : NamelessIntegration, IProfileProvider
	{
		public IReadOnlyList<VirtualProfileDescriptor> GetProfiles() => [];
	}
}
