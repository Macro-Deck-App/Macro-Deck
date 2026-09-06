using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;

public class MusicPlayerStatePayload
{
	public string? InstanceId { get; set; }

	public bool IsConnected { get; set; }

	public bool IsUnavailable { get; set; }

	public string? StatusMessage { get; set; }

	public string PlaybackState { get; set; } = "stopped";

	public bool IsPlaying { get; set; }

	public string? TrackName { get; set; }

	public string? ArtistName { get; set; }

	public string? AlbumName { get; set; }

	public string? ArtworkId { get; set; }

	public long? PositionMs { get; set; }

	public long? DurationMs { get; set; }

	public int? Volume { get; set; }

	public bool ShuffleEnabled { get; set; }

	public string RepeatMode { get; set; } = "off";

	public string? DeviceName { get; set; }

	public string? DeviceType { get; set; }

	public static MusicPlayerStatePayload From(MusicPlayerState state, string? instanceId = null)
		=> new()
		{
			InstanceId = instanceId,
			IsConnected = state.IsConnected,
			IsUnavailable = state.IsUnavailable,
			StatusMessage = state.StatusMessage,
			PlaybackState = state.PlaybackState.ToString().ToLowerInvariant(),
			IsPlaying = state.PlaybackState == MacroDeck.Sdk.MusicPlayer.PlaybackState.Playing,
			TrackName = state.TrackName,
			ArtistName = state.Artists.Count > 0 ? string.Join(", ", state.Artists) : null,
			AlbumName = state.AlbumName,
			ArtworkId = state.ArtworkId,
			PositionMs = state.Position is { } p ? (long)p.TotalMilliseconds : null,
			DurationMs = state.Duration is { } d ? (long)d.TotalMilliseconds : null,
			Volume = state.VolumePercent,
			ShuffleEnabled = state.ShuffleEnabled,
			RepeatMode = state.RepeatMode.ToString().ToLowerInvariant(),
			DeviceName = state.DeviceName,
			DeviceType = state.DeviceType
		};

	public static MusicPlayerStatePayload Disconnected(string? instanceId = null)
		=> From(MusicPlayerState.Disconnected, instanceId);
}
