using System.Net.WebSockets;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;

namespace MacroDeckHost.Integrations.WebNowPlaying;

internal sealed class WebNowPlayingMusicPlayer(WebNowPlayingPlayers players) : IMusicPlayer
{
	private static readonly ILogger _logger =
		IntegrationLog.For<WebNowPlayingMusicPlayer>(WebNowPlayingIntegration.IntegrationId);

	private int _eventId;

	public Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
	{
		if (!players.HasConnections)
		{
			return Task.FromResult(MusicPlayerState.Unavailable());
		}

		return Task.FromResult(players.Active() is { } active
			? ToState(active)
			: new MusicPlayerState { IsConnected = true, PlaybackState = PlaybackState.Stopped });
	}

	public Task<MusicPlayerArtwork?> GetArtworkAsync(string artworkId, CancellationToken cancellationToken = default)
		=> Task.FromResult(players.Artwork(artworkId) is { } cover ? new MusicPlayerArtwork(cover, "image/png") : null);

	public Task PlayAsync(CancellationToken cancellationToken = default)
		=> SendAsync(player => player.CanSetState,
			WebNowPlayingEvent.SetState,
			_ => WebNowPlayingProtocol.StatePlaying,
			cancellationToken);

	public Task PauseAsync(CancellationToken cancellationToken = default)
		=> SendAsync(player => player.CanSetState,
			WebNowPlayingEvent.SetState,
			_ => WebNowPlayingProtocol.StatePaused,
			cancellationToken);

	public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default)
		=> SendAsync(player => player.CanSetState,
			WebNowPlayingEvent.SetState,
			player => player.State == WebNowPlayingProtocol.StatePlaying
				? WebNowPlayingProtocol.StatePaused
				: WebNowPlayingProtocol.StatePlaying,
			cancellationToken);

	public Task NextAsync(CancellationToken cancellationToken = default)
		=> SendAsync(player => player.CanSkipNext, WebNowPlayingEvent.SkipNext, _ => 0, cancellationToken);

	public Task PreviousAsync(CancellationToken cancellationToken = default)
		=> SendAsync(player => player.CanSkipPrevious, WebNowPlayingEvent.SkipPrevious, _ => 0, cancellationToken);

	public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
		=> SendAsync(player => player.CanSetPosition,
			WebNowPlayingEvent.SetPosition,
			player =>
			{
				var seconds = Math.Max(0, (long)position.TotalSeconds);
				return player.Duration > 0 ? Math.Min(seconds, player.Duration) : seconds;
			},
			cancellationToken);

	public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
		=> SendAsync(player => player.CanSetVolume,
			WebNowPlayingEvent.SetVolume,
			_ => Math.Clamp(volumePercent, 0, 100),
			cancellationToken);

	public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default)
		=> SendAsync(player => player.CanSetShuffle,
			WebNowPlayingEvent.SetShuffle,
			_ => enabled ? 1 : 0,
			cancellationToken);

	public Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default)
	{
		var repeat = mode switch
		{
			RepeatMode.Track => WebNowPlayingProtocol.RepeatOne,
			RepeatMode.Context => WebNowPlayingProtocol.RepeatAll,
			_ => WebNowPlayingProtocol.RepeatNone
		};

		return SendAsync(player => player.CanSetRepeat && (player.AvailableRepeat & repeat) != 0,
			WebNowPlayingEvent.SetRepeat,
			_ => repeat,
			cancellationToken);
	}

	internal static MusicPlayerState ToState(WebNowPlayingActivePlayer active)
	{
		var player = active.Player;

		return new MusicPlayerState
		{
			IsConnected = true,
			PlaybackState = ToPlaybackState(player.State),
			TrackName = NullIfEmpty(player.Title),
			Artists = player.Artist.Length == 0 ? [] : [player.Artist],
			AlbumName = NullIfEmpty(player.Album),
			ArtworkId = active.ArtworkId,
			Position = TimeSpan.FromSeconds(player.Position),
			Duration = player.Duration > 0 ? TimeSpan.FromSeconds(player.Duration) : null,
			VolumePercent = player.Volume,
			ShuffleEnabled = player.Shuffle,
			RepeatMode = ToRepeatMode(player.Repeat),
			DeviceName = NullIfEmpty(player.Name)
		};
	}

	internal static PlaybackState ToPlaybackState(int state)
		=> state switch
		{
			WebNowPlayingProtocol.StatePlaying => PlaybackState.Playing,
			WebNowPlayingProtocol.StatePaused => PlaybackState.Paused,
			_ => PlaybackState.Stopped
		};

	internal static RepeatMode ToRepeatMode(int repeat)
		=> repeat switch
		{
			WebNowPlayingProtocol.RepeatOne => RepeatMode.Track,
			WebNowPlayingProtocol.RepeatAll => RepeatMode.Context,
			_ => RepeatMode.Off
		};

	internal static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

	private async Task SendAsync(
		Func<WebNowPlayingPlayer, bool> supported,
		WebNowPlayingEvent @event,
		Func<WebNowPlayingPlayer, long> data,
		CancellationToken cancellationToken)
	{
		if (players.Active() is not { } active || !supported(active.Player))
		{
			_logger.Debug("No active WebNowPlaying player accepts {Event}", @event);
			return;
		}

		var command = WebNowPlayingProtocol.Command(active.Player.PortId,
			Interlocked.Increment(ref _eventId),
			@event,
			data(active.Player));

		try
		{
			await active.Connection.SendAsync(command, cancellationToken);
		}
		catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException)
		{
			_logger.Debug("WebNowPlaying {Event} was not delivered: {Message}", @event, ex.Message);
		}
	}
}
