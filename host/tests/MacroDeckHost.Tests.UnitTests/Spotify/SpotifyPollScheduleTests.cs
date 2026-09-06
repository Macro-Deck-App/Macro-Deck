using MacroDeck.Sdk.MusicPlayer;
using MacroDeckHost.Integrations.Spotify;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifyPollScheduleTests
{
	[TestCase(PlaybackState.Playing, 3_000)]
	[TestCase(PlaybackState.Paused, 5_000)]
	[TestCase(PlaybackState.Stopped, 10_000)]
	public void Uses_the_required_playback_cadence(PlaybackState playback, int expectedMilliseconds)
	{
		var state = new MusicPlayerState { IsConnected = true, PlaybackState = playback };

		Assert.That(SpotifyPollSchedule.AfterState(state),
			Is.EqualTo(TimeSpan.FromMilliseconds(expectedMilliseconds)));
	}

	[Test]
	public void Disconnected_and_unavailable_states_use_the_idle_cadence()
	{
		Assert.Multiple(() =>
		{
			Assert.That(SpotifyPollSchedule.AfterState(MusicPlayerState.Disconnected),
				Is.EqualTo(TimeSpan.FromSeconds(10)));
			Assert.That(SpotifyPollSchedule.AfterState(MusicPlayerState.Unavailable("offline")),
				Is.EqualTo(TimeSpan.FromSeconds(10)));
		});
	}

	[TestCase(59, 1_500)]
	[TestCase(58, 2_500)]
	public void Polls_half_a_second_after_the_estimated_track_end_when_sooner(
		int positionSeconds,
		int expectedMilliseconds)
	{
		var state = new MusicPlayerState
		{
			IsConnected = true,
			PlaybackState = PlaybackState.Playing,
			Position = TimeSpan.FromSeconds(positionSeconds),
			Duration = TimeSpan.FromSeconds(60)
		};

		Assert.That(SpotifyPollSchedule.AfterState(state),
			Is.EqualTo(TimeSpan.FromMilliseconds(expectedMilliseconds)));
	}

	[TestCase(57.5)]
	[TestCase(60)]
	[TestCase(61)]
	public void Track_end_hint_must_be_positive_and_strictly_sooner_than_the_baseline(double positionSeconds)
	{
		var state = new MusicPlayerState
		{
			IsConnected = true,
			PlaybackState = PlaybackState.Playing,
			Position = TimeSpan.FromSeconds(positionSeconds),
			Duration = TimeSpan.FromSeconds(60)
		};

		Assert.That(SpotifyPollSchedule.AfterState(state), Is.EqualTo(TimeSpan.FromSeconds(3)));
	}
}
