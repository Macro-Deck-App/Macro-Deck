using System.Text;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;
using MacroDeckHost.Integrations.MusicPlayer;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.MusicPlayer;

[TestFixture]
internal sealed class MusicPlayerAlbumColorTests
{
	private const string A = "spotify::default";
	private const string B = "sinusbot::b";

	[Test]
	public async Task A_players_colour_is_the_colour_of_its_cover()
	{
		var harness = new Harness();
		harness.Cache.Record(A, State(playing: true, artworkId: "cover-1"));

		Assert.That(await harness.Color.GetAsync(A), Is.EqualTo("#102030"));
	}

	[Test]
	public async Task A_paused_player_keeps_showing_its_cover_colour()
	{
		var harness = new Harness();
		harness.Cache.Record(A, State(playing: true, artworkId: "cover-1"));
		harness.Cache.Record(A, State(playing: false, artworkId: "cover-1"));

		Assert.That(await harness.Color.GetAsync(A), Is.EqualTo("#102030"));
	}

	[Test]
	public async Task A_player_that_never_had_a_cover_has_no_colour()
	{
		var harness = new Harness();
		harness.Cache.Record(A, State(playing: true, artworkId: null));

		Assert.That(await harness.Color.GetAsync(A), Is.Null);
	}

	[Test]
	public async Task Players_that_go_away_or_lose_their_cover_keep_the_last_colour()
	{
		var harness = new Harness();
		harness.Cache.Record(A, State(playing: true, artworkId: "cover-1"));
		await harness.Color.GetAsync(A);

		harness.Cache.Record(A, State(playing: true, artworkId: null));
		Assert.That(await harness.Color.GetAsync(A), Is.EqualTo("#102030"));

		harness.Cache.Record(A, new MusicPlayerStatePayload { IsConnected = false });
		Assert.That(await harness.Color.GetAsync(A), Is.EqualTo("#102030"));
	}

	[Test]
	public async Task Every_player_has_its_own_colour()
	{
		var harness = new Harness { Colors = { ["cover-1"] = "#102030", ["cover-2"] = "#405060" } };
		harness.Cache.Record(A, State(playing: true, artworkId: "cover-1"));
		harness.Cache.Record(B, State(playing: true, artworkId: "cover-2", track: "Other"));

		Assert.Multiple(async () =>
		{
			Assert.That(await harness.Color.GetAsync(A), Is.EqualTo("#102030"));
			Assert.That(await harness.Color.GetAsync(B), Is.EqualTo("#405060"));
		});
	}

	[Test]
	public async Task A_new_cover_gives_a_new_colour()
	{
		var harness = new Harness { Colors = { ["cover-1"] = "#102030", ["cover-2"] = "#405060" } };
		harness.Cache.Record(A, State(playing: true, artworkId: "cover-1"));
		await harness.Color.GetAsync(A);

		harness.Cache.Record(A, State(playing: true, artworkId: "cover-2", track: "Next"));

		Assert.That(await harness.Color.GetAsync(A), Is.EqualTo("#405060"));
	}

	[Test]
	public async Task The_same_cover_is_fetched_only_once()
	{
		var harness = new Harness();
		harness.Cache.Record(A, State(playing: true, artworkId: "cover-1"));

		await harness.Color.GetAsync(A);
		await harness.Color.GetAsync(A);

		Assert.That(harness.Fetches, Is.EqualTo(1));
	}

	[Test]
	public async Task A_cover_that_cannot_be_read_is_retried_rather_than_remembered_as_colourless()
	{
		var harness = new Harness { Colors = { ["cover-1"] = null } };
		harness.Cache.Record(A, State(playing: true, artworkId: "cover-1"));
		Assert.That(await harness.Color.GetAsync(A), Is.Null);

		harness.Colors["cover-1"] = "#102030";

		Assert.That(await harness.Color.GetAsync(A), Is.EqualTo("#102030"));
	}

	[Test]
	public async Task A_new_cover_that_cannot_be_read_leaves_the_previous_colour()
	{
		var harness = new Harness { Colors = { ["cover-2"] = null } };
		harness.Cache.Record(A, State(playing: true, artworkId: "cover-1"));
		await harness.Color.GetAsync(A);

		harness.Cache.Record(A, State(playing: true, artworkId: "cover-2", track: "Next"));

		Assert.That(await harness.Color.GetAsync(A), Is.EqualTo("#102030"));
	}

	[Test]
	public void The_integration_declares_one_colour_variable_per_player()
	{
		var integration = new MusicPlayerIntegration();
		integration.UseAlbumColor(new Harness().Color);

		var variables = integration.Variables;

		Assert.Multiple(() =>
		{
			Assert.That(variables.Select(variable => variable.Name),
				Is.EqualTo(new[] { "music_player_spotify_album_color", "music_player_sinusbot_album_color" }));
			Assert.That(variables.Select(variable => variable.Type), Is.All.EqualTo(VariableType.Color));
		});
	}

	[Test]
	public void Players_with_the_same_name_get_distinct_variables()
	{
		var harness = new Harness();
		harness.Instances.Add(new MusicPlayerInstanceDescriptor("jellyfin::x", "jellyfin", "Jellyfin", "Spotify", false));
		var integration = new MusicPlayerIntegration();
		integration.UseAlbumColor(harness.Color);

		var names = integration.Variables.Select(variable => variable.Name).ToList();

		Assert.That(names, Is.Unique);
		Assert.That(names, Has.Count.EqualTo(3));
	}

	[Test]
	public void Before_the_service_arrives_the_integration_offers_a_template()
	{
		var variables = new MusicPlayerIntegration().DeclaredVariables;

		Assert.That(VariableNameTemplate.IsTemplate(variables.Single().Name), Is.True);
	}

	[Test]
	public async Task Each_variable_reads_its_own_players_colour()
	{
		var harness = new Harness { Colors = { ["cover-2"] = "#405060" } };
		harness.Cache.Record(A, State(playing: true, artworkId: "cover-1"));
		harness.Cache.Record(B, State(playing: false, artworkId: "cover-2"));
		var integration = new MusicPlayerIntegration();
		integration.UseAlbumColor(harness.Color);

		var spotify = await integration.ReadAsync("music-player-spotify-album-color");
		var sinusBot = await integration.ReadAsync("music-player-sinusbot-album-color");
		var unknown = await integration.ReadAsync("music-player-nobody-album-color");

		Assert.Multiple(() =>
		{
			Assert.That(spotify.Value, Is.EqualTo("#102030"));
			Assert.That(sinusBot.Value, Is.EqualTo("#405060"));
			Assert.That(unknown, Is.EqualTo(VariableReading.Unavailable));
		});
	}

	[Test]
	public async Task The_integration_reads_unavailable_before_it_is_given_the_colour_service()
	{
		var reading = await new MusicPlayerIntegration().ReadAsync("music-player-spotify-album-color");

		Assert.That(reading, Is.EqualTo(VariableReading.Unavailable));
	}

	[Test]
	public async Task The_gateway_binder_hands_the_colour_service_to_the_integration()
	{
		var harness = new Harness();
		harness.Cache.Record(A, State(playing: true, artworkId: "cover-1"));
		var integration = new MusicPlayerIntegration();

		IntegrationGatewayBinder.Bind(integration,
			null!,
			null!,
			null!,
			null!,
			albumColor: harness.Color);

		Assert.That((await integration.ReadAsync("music-player-spotify-album-color")).Value, Is.EqualTo("#102030"));
	}

	private static MusicPlayerStatePayload State(bool playing, string? artworkId, string track = "Song")
		=> new()
		{
			IsConnected = true,
			IsPlaying = playing,
			PlaybackState = playing ? "playing" : "paused",
			TrackName = track,
			ArtworkId = artworkId
		};

	private sealed class Harness
	{
		public Harness()
		{
			Colors["cover-1"] = "#102030";
			Instances.Add(new MusicPlayerInstanceDescriptor(A, "spotify", "Spotify", "Spotify", false));
			Instances.Add(new MusicPlayerInstanceDescriptor(B, "sinusbot", "SinusBot", "SinusBot", false));
			Color = new MusicPlayerAlbumColor(new Registry(this), Cache, new Artwork(this), new Extractor(this));
		}

		public List<MusicPlayerInstanceDescriptor> Instances { get; } = [];

		public MusicPlayerStateCache Cache { get; } = new();

		public Dictionary<string, string?> Colors { get; } = new(StringComparer.Ordinal);

		public int Fetches { get; set; }

		public MusicPlayerAlbumColor Color { get; }

		private sealed class Registry(Harness owner) : IMusicPlayerRegistry
		{
			public IReadOnlyList<MusicPlayerInstanceDescriptor> GetInstances() => owner.Instances;

			public MacroDeck.Sdk.MusicPlayer.IMusicPlayer? GetPlayer(string instanceId) => null;

			public MacroDeck.Sdk.MusicPlayer.IMusicPlayer? GetPlayerWithOptions(string instanceId,
				IReadOnlyDictionary<string, object> options) => null;

			public MacroDeck.Sdk.MusicPlayer.IMusicPlayer? DefaultPlayer => null;
		}

		private sealed class Artwork(Harness owner) : IMusicPlayerArtworkService
		{
			public string GetETag(string artworkId, int? size) => artworkId;

			public Task<ArtworkImageResult?> GetImage(string instanceId, string artworkId, int? size,
				CancellationToken cancellationToken)
			{
				owner.Fetches++;
				return Task.FromResult<ArtworkImageResult?>(
					new ArtworkImageResult(Encoding.UTF8.GetBytes(artworkId), "image/png", artworkId));
			}

			public Task<ArtworkImageResult?> GetImage(MusicPlayerVariant variant, string artworkId, int? size,
				CancellationToken cancellationToken)
				=> GetImage(variant.InstanceId, artworkId, size, cancellationToken);
		}

		private sealed class Extractor(Harness owner) : IArtworkPaletteExtractor
		{
			public ArtworkPalette? Extract(byte[] image)
				=> owner.Colors.GetValueOrDefault(Encoding.UTF8.GetString(image)) is { } color
					? new ArtworkPalette(color, color, color)
					: null;
		}
	}
}
