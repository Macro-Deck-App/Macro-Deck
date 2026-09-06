namespace MacroDeck.Sdk.MusicPlayer;

/// <summary>Provider-independent music player capability used by the host.</summary>
public interface IMusicPlayer
{
	/// <summary>Returns the current playback state, or <see cref="MusicPlayerState.Disconnected"/> when unavailable.</summary>
	Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default);

	/// <summary>Resolves an artwork ID to image bytes, or <c>null</c> when unavailable.</summary>
	Task<MusicPlayerArtwork?> GetArtworkAsync(string artworkId, CancellationToken cancellationToken = default);

	Task PlayAsync(CancellationToken cancellationToken = default);

	Task PauseAsync(CancellationToken cancellationToken = default);

	Task TogglePlayPauseAsync(CancellationToken cancellationToken = default);

	Task NextAsync(CancellationToken cancellationToken = default);

	Task PreviousAsync(CancellationToken cancellationToken = default);

	Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);

	/// <summary>Sets volume in percent from 0 to 100.</summary>
	Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default);

	Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default);

	Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default);
}
