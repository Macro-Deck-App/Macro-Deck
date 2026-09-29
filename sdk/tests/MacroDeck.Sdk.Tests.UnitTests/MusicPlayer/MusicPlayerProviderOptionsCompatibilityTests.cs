using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeck.Sdk.Tests.UnitTests.MusicPlayer;

[TestFixture]
public class MusicPlayerProviderOptionsCompatibilityTests
{
	[Test]
	public void An_instance_built_the_way_older_providers_build_it_declares_no_options()
		=> Assert.That(new MusicPlayerInstance("default", "Jukebox").Options, Is.Empty);

	[Test]
	public void A_provider_written_before_options_existed_answers_an_options_request_with_its_plain_player()
	{
		var player = new StubPlayer();
		IMusicPlayerProvider provider = new PlainProvider(player);

		var resolved = provider.GetPlayerWithOptions(new MusicPlayerOptionsRequest
		{
			InstanceId = "default", Options = new Dictionary<string, object> { ["cycleSeconds"] = 10d }
		});

		Assert.That(resolved, Is.SameAs(player));
	}

	private sealed class PlainProvider(IMusicPlayer player) : IMusicPlayerProvider
	{
		public IReadOnlyList<MusicPlayerInstance> GetInstances() => [new MusicPlayerInstance("default", "Jukebox")];

		public IMusicPlayer? GetPlayer(string instanceId) => instanceId == "default" ? player : null;
	}

	private sealed class StubPlayer : IMusicPlayer
	{
		public Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
			=> Task.FromResult(MusicPlayerState.Disconnected);

		public Task<MusicPlayerArtwork?> GetArtworkAsync(string artworkId, CancellationToken cancellationToken = default)
			=> Task.FromResult<MusicPlayerArtwork?>(null);

		public Task PlayAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task NextAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task PreviousAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}
}
