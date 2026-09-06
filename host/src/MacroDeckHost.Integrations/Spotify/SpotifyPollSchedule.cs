using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeckHost.Integrations.Spotify;

internal static class SpotifyPollSchedule
{
	internal static readonly TimeSpan PlayingInterval = TimeSpan.FromSeconds(3);
	internal static readonly TimeSpan PausedInterval = TimeSpan.FromSeconds(5);
	internal static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(10);
	internal static readonly TimeSpan TransientFailureInterval = TimeSpan.FromSeconds(3);
	internal static readonly TimeSpan ActionFollowUpInterval = TimeSpan.FromMilliseconds(750);
	internal static readonly TimeSpan TrackEndGrace = TimeSpan.FromMilliseconds(500);

	internal static TimeSpan AfterState(MusicPlayerState state)
	{
		var baseline = state switch
		{
			{ IsConnected: true, PlaybackState: PlaybackState.Playing } => PlayingInterval,
			{ IsConnected: true, PlaybackState: PlaybackState.Paused } => PausedInterval,
			_ => IdleInterval
		};

		if (state is not
			{
				IsConnected: true,
				PlaybackState: PlaybackState.Playing,
				Position: { } position,
				Duration: { } duration
			} ||
			duration <= position)
		{
			return baseline;
		}

		var trackEnd = duration - position + TrackEndGrace;
		return trackEnd > TimeSpan.Zero && trackEnd < baseline ? trackEnd : baseline;
	}
}
