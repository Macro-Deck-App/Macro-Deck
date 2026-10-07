using System.Globalization;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;

namespace MacroDeckHost.Integrations.SoundPad;

internal sealed class SoundPadMusicPlayer : ICatalogMusicPlayer
{
	private static readonly ILogger _logger =
		IntegrationLog.For<SoundPadMusicPlayer>(SoundPadIntegration.IntegrationId);

	private readonly SoundPadConnection _connection;
	private readonly FailureEpisodeTracker _readFailures = new();
	private readonly Lock _gate = new();

	private SoundPadPlayStatus _lastStatus = SoundPadPlayStatus.Stopped;
	private long _lastDurationMs;
	private long _lastPositionMs;
	private SoundPadSound? _current;
	private SoundPadSound? _startedByMacroDeck;
	private bool _resolveCurrent;

	public SoundPadMusicPlayer(SoundPadConnection connection)
	{
		_connection = connection;
	}

	public MusicPlayerState LastState { get; private set; } = MusicPlayerState.Unavailable();

	public bool IsRecording { get; private set; }

	public bool IsMuted { get; private set; }

	public bool IsConnected => _connection.IsConnected;

	public async Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
	{
		if (!_connection.IsConnected)
		{
			return Unavailable();
		}

		try
		{
			var status = await _connection.RunAsync(c => c.GetPlayStatusAsync(), cancellationToken);
			var positionMs = await _connection.RunAsync(c => c.GetPlaybackPositionAsync(), cancellationToken);
			var durationMs = await _connection.RunAsync(c => c.GetPlaybackDurationAsync(), cancellationToken);
			var volume = await _connection.RunAsync(c => c.GetVolumeAsync(), cancellationToken);
			IsMuted = await OptionalAsync(c => c.IsMutedAsync(), IsMuted, cancellationToken);
			IsRecording = await OptionalAsync(c => c.GetRecordingPositionAsync(), 0L, cancellationToken) > 0;

			var current = await CurrentSoundAsync(status, positionMs, durationMs, cancellationToken);
			NoteReadRecovered();

			var playback = status switch
			{
				SoundPadPlayStatus.Playing or SoundPadPlayStatus.Seeking => PlaybackState.Playing,
				SoundPadPlayStatus.Paused => PlaybackState.Paused,
				_ => PlaybackState.Stopped
			};

			return LastState = new MusicPlayerState
			{
				IsConnected = true,
				PlaybackState = playback,
				TrackName = playback == PlaybackState.Stopped ? null : current?.Title,
				Artists = playback != PlaybackState.Stopped && current?.Artist is { } artist ? [artist] : [],
				Position = playback == PlaybackState.Stopped ? null : TimeSpan.FromMilliseconds(positionMs),
				Duration = playback == PlaybackState.Stopped || durationMs <= 0
					? null
					: TimeSpan.FromMilliseconds(durationMs),
				VolumePercent = Math.Clamp(volume, 0, 100)
			};
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			if (NoteReadFailure())
			{
				_logger.Warning(ex, "Reading the SoundPad playback state failed");
			}

			return Unavailable();
		}
	}

	private async Task<T> OptionalAsync<T>(Func<ISoundPadClient, Task<T>> read, T fallback,
		CancellationToken cancellationToken)
	{
		try
		{
			return await _connection.RunAsync(read, cancellationToken);
		}
		catch (SoundPadCommandException)
		{
			return fallback;
		}
	}

	public Task<MusicPlayerArtwork?> GetArtworkAsync(string artworkId, CancellationToken cancellationToken = default)
		=> Task.FromResult<MusicPlayerArtwork?>(null);

	public async Task PlayAsync(CancellationToken cancellationToken = default)
	{
		switch (await StatusAsync(cancellationToken))
		{
			case SoundPadPlayStatus.Paused:
				await Command(c => c.TogglePauseAsync(), cancellationToken);
				break;
			case SoundPadPlayStatus.Stopped:
				await PlayAgainAsync(cancellationToken);
				break;
		}
	}

	public async Task PauseAsync(CancellationToken cancellationToken = default)
	{
		if (await StatusAsync(cancellationToken) is SoundPadPlayStatus.Playing or SoundPadPlayStatus.Seeking)
		{
			await Command(c => c.TogglePauseAsync(), cancellationToken);
		}
	}

	public async Task TogglePlayPauseAsync(CancellationToken cancellationToken = default)
	{
		if (await StatusAsync(cancellationToken) == SoundPadPlayStatus.Stopped)
		{
			await PlayAgainAsync(cancellationToken);
			return;
		}

		await Command(c => c.TogglePauseAsync(), cancellationToken);
	}

	public Task NextAsync(CancellationToken cancellationToken = default)
		=> CommandChangingSound(c => c.PlayNextSoundAsync(), cancellationToken);

	public Task PreviousAsync(CancellationToken cancellationToken = default)
		=> CommandChangingSound(c => c.PlayPreviousSoundAsync(), cancellationToken);

	public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
		=> Command(c => c.SeekAsync((long)Math.Max(0, position.TotalMilliseconds)), cancellationToken);

	public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
		=> Command(c => c.SetVolumeAsync(Math.Clamp(volumePercent, 0, 100)), cancellationToken);

	public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default)
		=> Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken = default)
		=> Command(c => c.StopSoundAsync(), cancellationToken);

	public Task ToggleMuteAsync(CancellationToken cancellationToken = default)
		=> Command(c => c.ToggleMuteAsync(), cancellationToken);

	public Task StartRecordingAsync(SoundPadRecordingSource source, CancellationToken cancellationToken = default)
		=> Command(c => c.StartRecordingAsync(source), cancellationToken);

	public Task StopRecordingAsync(CancellationToken cancellationToken = default)
		=> Command(c => c.StopRecordingAsync(), cancellationToken);

	public Task PlayRandomSoundAsync(int? categoryIndex, bool speakers, bool microphone,
		CancellationToken cancellationToken = default)
		=> CommandChangingSound(c => c.PlayRandomSoundAsync(categoryIndex, speakers, microphone), cancellationToken);

	public Task<IReadOnlyList<SoundPadCategory>> GetCategoriesAsync(CancellationToken cancellationToken)
		=> _connection.RunAsync(c => c.GetCategoriesAsync(),
			cancellationToken,
			connectOnDemand: true,
			SoundPadConnection.ListTimeout);

	public async Task<IReadOnlyList<MusicPlayerCatalogItem>> GetCatalogAsync(
		string instanceId,
		MusicPlayerCatalogItemKind kind,
		string? filter,
		CancellationToken cancellationToken)
	{
		if (kind != MusicPlayerCatalogItemKind.Track)
		{
			return [];
		}

		var sounds = await SoundsAsync(cancellationToken);
		var term = filter?.Trim();

		return sounds
			.Where(sound => string.IsNullOrEmpty(term) ||
				sound.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
				sound.Artist?.Contains(term, StringComparison.CurrentCultureIgnoreCase) == true)
			.Select(sound => new MusicPlayerCatalogItem(ItemId(sound),
				DisplayTitle(sound),
				MusicPlayerCatalogItemKind.Track,
				sound.Artist,
				Duration: sound.Duration))
			.ToList();
	}

	public async Task PlayItemAsync(MusicPlayerCatalogItem item, CancellationToken cancellationToken = default)
	{
		try
		{
			var sound = await FindAsync(item.Id, cancellationToken);
			if (sound is null)
			{
				_logger.Warning("The SoundPad sound {Sound} is not in the sound list", item.Id);
				return;
			}

			await Command(c => c.PlaySoundAsync(sound.Index), cancellationToken);
			lock (_gate)
			{
				_startedByMacroDeck = sound;
				_resolveCurrent = true;
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Playing the SoundPad sound {Sound} failed", item.Id);
		}
	}

	internal static string ItemId(SoundPadSound sound)
		=> sound.Path ?? sound.Index.ToString(CultureInfo.InvariantCulture);

	private static string DisplayTitle(SoundPadSound sound)
	{
		if (!string.IsNullOrWhiteSpace(sound.Title) || sound.Path is null)
		{
			return sound.Title;
		}

		var name = sound.Path[(sound.Path.LastIndexOfAny(['\\', '/']) + 1)..];
		var extension = name.LastIndexOf('.');
		return extension > 0 ? name[..extension] : name;
	}

	private async Task<SoundPadSound?> FindAsync(string id, CancellationToken cancellationToken)
	{
		var trimmed = id.Trim();
		if (int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
		{
			return index > 0 ? new SoundPadSound(index, string.Empty, null, null, null, null) : null;
		}

		var sounds = await SoundsAsync(cancellationToken);
		return sounds.FirstOrDefault(sound =>
			string.Equals(sound.Path, trimmed, StringComparison.OrdinalIgnoreCase));
	}

	private async Task<SoundPadSound?> CurrentSoundAsync(SoundPadPlayStatus status, long positionMs,
		long durationMs, CancellationToken cancellationToken)
	{
		SoundPadSound? started;
		bool resolve;
		lock (_gate)
		{
			var newPlayback = status != SoundPadPlayStatus.Stopped &&
				(_lastStatus == SoundPadPlayStatus.Stopped ||
					durationMs != _lastDurationMs ||
					positionMs < _lastPositionMs);
			_lastStatus = status;
			_lastDurationMs = durationMs;
			_lastPositionMs = positionMs;

			if (status == SoundPadPlayStatus.Stopped)
			{
				_current = null;
				_startedByMacroDeck = null;
				_resolveCurrent = false;
				return null;
			}

			resolve = _resolveCurrent || newPlayback || _current is null;
			started = _startedByMacroDeck;
			_startedByMacroDeck = null;
			_resolveCurrent = false;
		}

		if (!resolve)
		{
			return _current;
		}

		var current = started is { Title.Length: > 0 } ? started : await MostRecentlyPlayedAsync(started,
			cancellationToken);
		lock (_gate)
		{
			_current = current;
		}

		return current;
	}

	private async Task<SoundPadSound?> MostRecentlyPlayedAsync(SoundPadSound? started,
		CancellationToken cancellationToken)
	{
		var sounds = await SoundsAsync(cancellationToken);
		if (started is not null)
		{
			return sounds.FirstOrDefault(sound => sound.Index == started.Index);
		}

		// SoundPad's remote control has no "current sound" call; the newest last-played time stands in for it.
		return sounds.Where(sound => sound.LastPlayedOn is not null).MaxBy(sound => sound.LastPlayedOn);
	}

	private Task<IReadOnlyList<SoundPadSound>> SoundsAsync(CancellationToken cancellationToken)
		=> _connection.RunAsync(c => c.GetSoundsAsync(),
			cancellationToken,
			connectOnDemand: true,
			SoundPadConnection.ListTimeout);

	private Task<SoundPadPlayStatus> StatusAsync(CancellationToken cancellationToken)
		=> _connection.RunAsync(c => c.GetPlayStatusAsync(), cancellationToken, connectOnDemand: true);

	private async Task PlayAgainAsync(CancellationToken cancellationToken)
		=> await CommandChangingSound(c => c.PlayCurrentSoundAgainAsync(), cancellationToken);

	private async Task CommandChangingSound(Func<ISoundPadClient, Task> command,
		CancellationToken cancellationToken)
	{
		await Command(command, cancellationToken);
		lock (_gate)
		{
			_resolveCurrent = true;
		}
	}

	private Task Command(Func<ISoundPadClient, Task> command, CancellationToken cancellationToken)
		=> _connection.RunAsync(command, cancellationToken, connectOnDemand: true);

	private MusicPlayerState Unavailable()
	{
		IsRecording = false;
		lock (_gate)
		{
			_current = null;
			_startedByMacroDeck = null;
			_resolveCurrent = false;
			_lastStatus = SoundPadPlayStatus.Stopped;
			_lastPositionMs = 0;
		}

		return LastState = MusicPlayerState.Unavailable();
	}

	private bool NoteReadFailure()
		=> _readFailures.RecordFailure("read failed").Kind == FailureEpisodeSignalKind.Onset;

	private void NoteReadRecovered() => _readFailures.RecordSuccess();
}
