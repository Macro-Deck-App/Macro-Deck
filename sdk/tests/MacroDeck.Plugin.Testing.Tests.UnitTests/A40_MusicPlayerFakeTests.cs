using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

[TestFixture]
public class A40MusicPlayerFakeTests
{
	private static readonly string[] _seededPlayerNames = ["Spotify (alice)"];

	[Test]
	public async Task An_integration_under_the_harness_reads_the_seeded_players_and_album_colours()
	{
		var integration = new RecordingIntegration();
		await using var harness = PluginTestHarness.Create(builder => builder.RegisterIntegration(_ => integration));
		harness.Context.MusicPlayers.Set([
			new MusicPlayerInstanceSnapshot
				{ Id = "spotify::alice", ProviderId = "spotify", DisplayName = "Spotify (alice)" }
		]);
		harness.Context.MusicPlayers.SetAlbumColor("spotify::alice", "#102030");

		await harness.InitializeIntegrationsAsync();
		harness.Context.MusicPlayers.Set([]);

		Assert.Multiple(() =>
		{
			Assert.That(integration.Players, Is.EqualTo(_seededPlayerNames));
			Assert.That(integration.Color, Is.EqualTo("#102030"));
			Assert.That(integration.Changes, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_player_the_test_gave_no_colour_has_none()
	{
		Assert.That(await new FakeMusicPlayerApi().GetAlbumColorAsync("spotify::alice"), Is.Null);
	}

	private sealed class RecordingIntegration : IPluginIntegration
	{
		public IReadOnlyList<string> Players { get; private set; } = [];

		public string? Color { get; private set; }

		public int Changes { get; private set; }

		public IReadOnlyList<IActionDefinition> Actions => [];

		public async Task InitializeAsync(IIntegrationContext context)
		{
			Players = [.. context.MusicPlayers.Instances.Select(instance => instance.DisplayName)];
			Color = await context.MusicPlayers.GetAlbumColorAsync("spotify::alice");
			context.MusicPlayers.InstancesChanged += (_, _) => Changes++;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}
	}
}
