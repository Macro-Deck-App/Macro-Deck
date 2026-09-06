using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeck.Sdk.Tests.UnitTests.MusicPlayer;

[TestFixture]
internal sealed class MusicPlayerCapabilityShapeTests
{
	[Test]
	public void IMusicPlayer_DoesNotDeclarePlayItemAsync_AndAMinimalImplementationIsNotACatalogPlayer()
	{
		IMusicPlayer player = new MinimalPlayer();

		Assert.Multiple(() =>
		{
			Assert.That(typeof(IMusicPlayer).GetMethod("PlayItemAsync"), Is.Null);
			Assert.That(player is ICatalogMusicPlayer, Is.False);
		});
	}

	[Test]
	public void ICatalogMusicPlayer_DeclaresPlayItemAsync_AndExtendsIMusicPlayerAndCatalogProvider()
	{
		var playItemAsync = typeof(ICatalogMusicPlayer).GetMethod("PlayItemAsync");

		Assert.Multiple(() =>
		{
			Assert.That(playItemAsync, Is.Not.Null);
			Assert.That(playItemAsync!.ReturnType, Is.EqualTo(typeof(Task)));
			Assert.That(typeof(IMusicPlayer).IsAssignableFrom(typeof(ICatalogMusicPlayer)), Is.True);
			Assert.That(typeof(IMusicPlayerCatalogProvider).IsAssignableFrom(typeof(ICatalogMusicPlayer)), Is.True);
			Assert.That(typeof(ICatalogMusicPlayer).IsPublic, Is.True);
			Assert.That(typeof(ICatalogMusicPlayer).Namespace, Is.EqualTo("MacroDeck.Sdk.MusicPlayer"));
		});
	}

	private sealed class MinimalPlayer : IMusicPlayer
	{
		public Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
			=> Task.FromResult(MusicPlayerState.Disconnected);

		public Task<MusicPlayerArtwork?> GetArtworkAsync(string artworkId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult<MusicPlayerArtwork?>(null);

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
