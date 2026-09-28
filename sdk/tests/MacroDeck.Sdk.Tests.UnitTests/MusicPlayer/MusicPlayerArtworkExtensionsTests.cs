using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeck.Sdk.Tests.UnitTests.MusicPlayer;

[TestFixture]
internal sealed class MusicPlayerArtworkExtensionsTests
{
	private static readonly byte[] FirstCover = [1, 2, 3];
	private static readonly byte[] SecondCover = [4, 5, 6, 7];

	[Test]
	public async Task The_artwork_is_registered_under_the_given_name_and_its_handle_returned()
	{
		var player = new ArtworkPlayer { ["track-1"] = new MusicPlayerArtwork(FirstCover, "image/jpeg") };
		var resources = new FakeUiResourceRegistry();

		var cover = await player.GetArtworkAsUiResourceAsync(resources, "now-playing-cover", "track-1");

		var registered = resources.Resources["now-playing-cover"];
		Assert.Multiple(() =>
		{
			Assert.That(cover, Is.EqualTo(registered.Handle));
			Assert.That(registered.Content, Is.EqualTo(FirstCover));
			Assert.That(registered.Handle.MediaType, Is.EqualTo("image/jpeg"));
		});
	}

	[Test]
	public async Task The_next_track_under_the_same_name_replaces_the_cover_instead_of_adding_one()
	{
		var player = new ArtworkPlayer
		{
			["track-1"] = new MusicPlayerArtwork(FirstCover, "image/jpeg"),
			["track-2"] = new MusicPlayerArtwork(SecondCover, "image/png"),
		};
		var resources = new FakeUiResourceRegistry();

		var first = await player.GetArtworkAsUiResourceAsync(resources, "now-playing-cover", "track-1");
		var second = await player.GetArtworkAsUiResourceAsync(resources, "now-playing-cover", "track-2");

		Assert.Multiple(() =>
		{
			Assert.That(resources.Resources, Has.Count.EqualTo(1));
			Assert.That(second!.ResourceId, Is.EqualTo(first!.ResourceId));
			Assert.That(second.ContentHash, Is.Not.EqualTo(first.ContentHash));
			Assert.That(resources.Resources["now-playing-cover"].Content, Is.EqualTo(SecondCover));
		});
	}

	[Test]
	public async Task No_artwork_returns_null_and_leaves_the_previous_cover_in_place()
	{
		var player = new ArtworkPlayer { ["track-1"] = new MusicPlayerArtwork(FirstCover, "image/jpeg") };
		var resources = new FakeUiResourceRegistry();
		await player.GetArtworkAsUiResourceAsync(resources, "now-playing-cover", "track-1");

		var unknown = await player.GetArtworkAsUiResourceAsync(resources, "now-playing-cover", "track-without-cover");
		var noId = await player.GetArtworkAsUiResourceAsync(resources, "now-playing-cover", null);

		Assert.Multiple(() =>
		{
			Assert.That(unknown, Is.Null);
			Assert.That(noId, Is.Null);
			Assert.That(player.RequestedIds, Is.EqualTo((string[])["track-1", "track-without-cover"]));
			Assert.That(resources.Resources["now-playing-cover"].Content, Is.EqualTo(FirstCover));
		});
	}

	[Test]
	public void Artwork_Macro_Deck_does_not_accept_throws_and_registers_nothing()
	{
		var player = new ArtworkPlayer { ["track-1"] = new MusicPlayerArtwork(FirstCover, "image/svg+xml") };
		var resources = new FakeUiResourceRegistry();

		Assert.Multiple(() =>
		{
			Assert.ThrowsAsync<ArgumentException>(
				() => player.GetArtworkAsUiResourceAsync(resources, "now-playing-cover", "track-1"));
			Assert.That(resources.Resources, Is.Empty);
		});
	}

	private sealed class ArtworkPlayer : IMusicPlayer
	{
		private readonly Dictionary<string, MusicPlayerArtwork> _artwork = new(StringComparer.Ordinal);

		public MusicPlayerArtwork this[string artworkId]
		{
			set => _artwork[artworkId] = value;
		}

		public List<string> RequestedIds { get; } = [];

		public Task<MusicPlayerArtwork?> GetArtworkAsync(string artworkId,
			CancellationToken cancellationToken = default)
		{
			RequestedIds.Add(artworkId);
			return Task.FromResult(_artwork.GetValueOrDefault(artworkId));
		}

		public Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
			=> Task.FromResult(MusicPlayerState.Disconnected);

		public Task PlayAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task NextAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task PreviousAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;

		public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;
	}
}
