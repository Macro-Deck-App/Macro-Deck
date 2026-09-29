using System.Globalization;
using MacroDeck.Plugin.Hosting.Capabilities.MusicPlayer;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeckHost.Tests.PluginContractTests.Harness;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class MusicPlayerOptionsContractTests : CapabilityContractFixture
{
	[Test]
	public async Task A_plugin_instance_s_options_reach_the_host_and_its_values_reach_the_plugin_for_state_and_artwork()
	{
		var plugin = new CyclingIntegration();
		var provider = (IMusicPlayerProvider)await ConnectAsync(
			[new MusicPlayerCapabilityHandler([plugin], TestMetadata.Default, new FakeAssetUploader())],
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.MusicPlayer,
					LocalId = ProviderCapabilityId.LocalId,
					VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
				}
			],
			[CapabilityKinds.MusicPlayer]);

		var declared = provider.GetInstances().Single().Options.Single();
		var withOptions = provider.GetPlayerWithOptions(new MusicPlayerOptionsRequest
		{
			InstanceId = CyclingIntegration.InstanceId,
			Options = new Dictionary<string, object> { ["cycleSeconds"] = 30d }
		})!;
		var plain = provider.GetPlayer(CyclingIntegration.InstanceId)!;

		var cycling = await withOptions.GetStateAsync(CancellationToken.None);
		var cover = await withOptions.GetArtworkAsync("cover", CancellationToken.None);
		var current = await plain.GetStateAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(declared.Name, Is.EqualTo("cycleSeconds"));
			Assert.That(declared.Type, Is.EqualTo(ActionParameterType.Number));
			Assert.That(declared.Min, Is.EqualTo(5));
			Assert.That(cycling.TrackName, Is.EqualTo("cycling every 30"));
			Assert.That(cover!.Data, Is.EqualTo(new byte[] { 30 }));
			Assert.That(current.TrackName, Is.EqualTo("current app"));
			Assert.That(plugin.Requests.Select(request => request.Options["cycleSeconds"]), Is.All.EqualTo(30d));
		});
	}

	private sealed class CyclingIntegration : IPluginIntegration, IMusicPlayerProvider
	{
		public const string InstanceId = "any-app";

		public List<MusicPlayerOptionsRequest> Requests { get; } = [];

		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public IReadOnlyList<MusicPlayerInstance> GetInstances() =>
		[
			new MusicPlayerInstance(InstanceId, "Any app")
			{
				Options = [ActionParameter.Number("cycleSeconds", "Cycle every", min: 5, max: 60, defaultValue: 10)]
			}
		];

		public IMusicPlayer? GetPlayer(string instanceId)
			=> instanceId == InstanceId ? Player("current app", 0) : null;

		public IMusicPlayer? GetPlayerWithOptions(MusicPlayerOptionsRequest request)
		{
			Requests.Add(request);
			var seconds = Convert.ToDouble(request.Options["cycleSeconds"], CultureInfo.InvariantCulture);

			return Player($"cycling every {seconds}", (byte)seconds);
		}

		private static TestMusicPlayer Player(string track, byte cover)
			=> new()
			{
				StateToReturn = new MusicPlayerState { IsConnected = true, TrackName = track },
				ArtworkToReturn = new MusicPlayerArtwork([cover], "image/png")
			};
	}
}
