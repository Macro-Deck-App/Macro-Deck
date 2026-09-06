using MacroDeckHost.Integrations.YtmDesktop.Protocol;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeckHost.Integrations.YtmDesktop;

internal static class YtmDesktopStateMapper
{
	public const string DeviceName = "YouTube Music Desktop App";

	public static YtmDesktopSnapshot Map(YtmPlayerState state, bool shuffleBelief, Func<string, string> registerArtwork)
	{
		var player = state.Player;
		var video = state.Video;
		var repeatMode = player.Queue?.RepeatMode ?? YtmRepeatMode.Unknown;

		return new YtmDesktopSnapshot
		{
			Player = new MusicPlayerState
			{
				IsConnected = true,
				PlaybackState = ToPlaybackState(player.TrackState, video),
				TrackName = video?.Title,
				Artists = string.IsNullOrWhiteSpace(video?.Author) ? [] : [video.Author],
				AlbumName = video?.Album,
				ArtworkId = RegisterLargestThumbnail(video?.Thumbnails, registerArtwork),
				Position = video is null ? null : TimeSpan.FromSeconds(Math.Max(0, player.VideoProgress)),
				Duration = ToDuration(video?.DurationSeconds),

				VolumePercent = Math.Clamp(player.Volume, 0, 100),
				ShuffleEnabled = shuffleBelief,
				RepeatMode = ToRepeatMode(repeatMode),

				DeviceName = DeviceName,
				DeviceType = null
			},
			VideoId = video?.Id,
			Muted = player.Muted,
			LikeStatus = Known(video?.LikeStatus),
			IsLive = video?.IsLive,
			VideoType = Known(video?.VideoType),
			AdPlaying = player.AdPlaying,
			ShuffleEnabled = shuffleBelief,
			RepeatMode = repeatMode is YtmRepeatMode.Unknown ? null : repeatMode,
			Queue = player.Queue?.Items ?? []
		};
	}

	public static int ToCompanionRepeatMode(RepeatMode mode)
		=> mode switch
		{
			RepeatMode.Track => (int)YtmRepeatMode.One,
			RepeatMode.Context => (int)YtmRepeatMode.All,
			_ => (int)YtmRepeatMode.None
		};

	private static PlaybackState ToPlaybackState(YtmTrackState trackState, YtmVideoInfo? video)
	{
		if (video is null)
		{
			return PlaybackState.Stopped;
		}

		return trackState switch
		{
			YtmTrackState.Playing => PlaybackState.Playing,
			YtmTrackState.Paused => PlaybackState.Paused,

			YtmTrackState.Buffering => PlaybackState.Playing,
			_ => PlaybackState.Stopped
		};
	}

	private static RepeatMode ToRepeatMode(YtmRepeatMode mode)
		=> mode switch
		{
			YtmRepeatMode.One => RepeatMode.Track,
			YtmRepeatMode.All => RepeatMode.Context,
			_ => RepeatMode.Off
		};

	private static TimeSpan? ToDuration(int? durationSeconds)
		=> durationSeconds is > 0 ? TimeSpan.FromSeconds(durationSeconds.Value) : null;

	private static string? RegisterLargestThumbnail(
		IReadOnlyList<YtmThumbnail>? thumbnails,
		Func<string, string> registerArtwork)
	{
		if (thumbnails is null || thumbnails.Count == 0)
		{
			return null;
		}

		YtmThumbnail? best = null;
		foreach (var thumbnail in thumbnails)
		{
			if (string.IsNullOrWhiteSpace(thumbnail.Url))
			{
				continue;
			}

			if (best is null || (long)thumbnail.Width * thumbnail.Height > (long)best.Width * best.Height)
			{
				best = thumbnail;
			}
		}

		return best is null ? null : registerArtwork(best.Url);
	}

	private static YtmLikeStatus? Known(YtmLikeStatus? status)
		=> status is null or YtmLikeStatus.Unknown ? null : status;

	private static YtmVideoType? Known(YtmVideoType? type)
		=> type is null or YtmVideoType.Unknown ? null : type;
}
