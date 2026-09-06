using MacroDeck.Plugin.Protocol.Capabilities.MusicPlayer;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

public static class MusicPlayerStateMapper
{
	public static MusicPlayerState ToDomain(MusicPlayerStateDto dto)
	{
		if (!Enum.TryParse<PlaybackState>(dto.PlaybackState, ignoreCase: false, out var playbackState))
		{
			throw new InvalidOperationException($"Unknown playback state '{dto.PlaybackState}'.");
		}

		if (!Enum.TryParse<RepeatMode>(dto.RepeatMode, ignoreCase: false, out var repeatMode))
		{
			throw new InvalidOperationException($"Unknown repeat mode '{dto.RepeatMode}'.");
		}

		return new MusicPlayerState
		{
			IsConnected = dto.IsConnected,
			IsUnavailable = dto.IsUnavailable,
			StatusMessage = dto.StatusMessage,
			PlaybackState = playbackState,
			TrackName = dto.TrackName,
			Artists = dto.Artists,
			AlbumName = dto.AlbumName,
			ArtworkId = dto.ArtworkId,
			Position = dto.PositionSeconds is { } position ? TimeSpan.FromSeconds(position) : null,
			Duration = dto.DurationSeconds is { } duration ? TimeSpan.FromSeconds(duration) : null,
			VolumePercent = dto.VolumePercent,
			ShuffleEnabled = dto.ShuffleEnabled,
			RepeatMode = repeatMode,
			DeviceName = dto.DeviceName,
			DeviceType = dto.DeviceType
		};
	}
}
