using MacroDeckHost.Integrations.Spotify;
using SpotifyAPI.Web;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifyMusicPlayerCatalogTests
{
	[Test]
	public void MapTracks_SkipsNullAndIdLessEntries()
	{
		var player = new SpotifyMusicPlayer();
		var valid = new FullTrack
		{
			Id = "track-1",
			Name = "Track One",
			Artists = [new SimpleArtist { Name = "Artist" }],
			Album = new SimpleAlbum { Images = [] },
			DurationMs = 120000
		};
		var withoutId = new FullTrack
		{
			Name = "No Id",
			Artists = [],
			Album = new SimpleAlbum { Images = [] }
		};

		var items = player.MapTracks([null, valid, withoutId]);

		Assert.That(items, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(items[0].Id, Is.EqualTo("track-1"));
			Assert.That(items[0].Title, Is.EqualTo("Track One"));
			Assert.That(items[0].Subtitle, Is.EqualTo("Artist"));
			Assert.That(items[0].Duration, Is.EqualTo(TimeSpan.FromMinutes(2)));
		});
	}

	[Test]
	public void MapPlaylists_SkipsNullAndIdLessEntries()
	{
		var player = new SpotifyMusicPlayer();
		var valid = new FullPlaylist
		{
			Id = "playlist-1",
			Name = "Playlist One",
			Owner = new PublicUser { DisplayName = "Owner" }
		};
		var withoutId = new FullPlaylist { Name = "No Id" };

		var items = player.MapPlaylists([null, valid, withoutId]);

		Assert.That(items, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(items[0].Id, Is.EqualTo("playlist-1"));
			Assert.That(items[0].Title, Is.EqualTo("Playlist One"));
			Assert.That(items[0].Subtitle, Is.EqualTo("Owner"));
		});
	}
}
