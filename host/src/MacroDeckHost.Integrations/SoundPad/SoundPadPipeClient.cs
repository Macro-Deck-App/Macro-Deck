using System.Globalization;
using System.Runtime.Versioning;
using SoundpadConnector;
using SoundpadConnector.Response;
using SoundpadConnector.XML;

namespace MacroDeckHost.Integrations.SoundPad;

[SupportedOSPlatform("windows")]
internal sealed class SoundPadPipeClient : ISoundPadClient
{
	// AutoReconnect stays off: with it ConnectAsync never returns while SoundPad is closed.
	private readonly Soundpad _soundpad = new() { AutoReconnect = false };

	public bool IsConnected => _soundpad.ConnectionStatus == ConnectionStatus.Connected;

	public Task ConnectAsync() => _soundpad.ConnectAsync();

	public void Disconnect() => _soundpad.Disconnect();

	public void Dispose() => _soundpad.Dispose();

	public async Task<string> GetVersionAsync() => Value(await _soundpad.GetSoundpadVersion());

	public async Task<SoundPadPlayStatus> GetPlayStatusAsync()
		=> Value(await _soundpad.GetPlayStatus()) switch
		{
			PlayStatus.Playing => SoundPadPlayStatus.Playing,
			PlayStatus.Paused => SoundPadPlayStatus.Paused,
			PlayStatus.Seeking => SoundPadPlayStatus.Seeking,
			_ => SoundPadPlayStatus.Stopped
		};

	public async Task<long> GetPlaybackPositionAsync() => Value(await _soundpad.GetPlaybackPosition());

	public async Task<long> GetPlaybackDurationAsync() => Value(await _soundpad.GetPlaybackDuration());

	public async Task<long> GetRecordingPositionAsync() => Value(await _soundpad.GetRecordingPosition());

	public async Task<int> GetVolumeAsync() => (int)Value(await _soundpad.GetVolume());

	public async Task<bool> IsMutedAsync() => Value(await _soundpad.IsMuted());

	public async Task<IReadOnlyList<SoundPadSound>> GetSoundsAsync()
		=> (Value(await _soundpad.GetSoundlist()).Sounds ?? []).Select(Map).ToList();

	public async Task<IReadOnlyList<SoundPadCategory>> GetCategoriesAsync()
		=> (Value(await _soundpad.GetCategories()).Categories ?? [])
			.Select(category => new SoundPadCategory(category.Index, category.Name ?? string.Empty))
			.ToList();

	public async Task PlaySoundAsync(int index) => Ensure(await _soundpad.PlaySound(index));

	public async Task PlayRandomSoundAsync(int? categoryIndex, bool speakers, bool microphone)
		=> Ensure(categoryIndex is { } category
			? await _soundpad.PlayRandomSoundFromCategory(category, speakers, microphone)
			: await _soundpad.PlayRandomSound(speakers, microphone));

	public async Task PlayCurrentSoundAgainAsync() => Ensure(await _soundpad.PlayCurrentSoundAgain());

	public async Task PlayNextSoundAsync() => Ensure(await _soundpad.PlayNextSound());

	public async Task PlayPreviousSoundAsync() => Ensure(await _soundpad.PlayPreviousSound());

	public async Task StopSoundAsync() => Ensure(await _soundpad.StopSound());

	public async Task TogglePauseAsync() => Ensure(await _soundpad.TogglePause());

	public async Task SeekAsync(long milliseconds)
		=> Ensure(await _soundpad.Seek((int)Math.Clamp(milliseconds, 0, int.MaxValue)));

	public async Task SetVolumeAsync(int volume) => Ensure(await _soundpad.SetVolume(Math.Clamp(volume, 0, 100)));

	public async Task ToggleMuteAsync() => Ensure(await _soundpad.ToggleMute());

	public async Task StartRecordingAsync(SoundPadRecordingSource source)
		=> Ensure(source switch
		{
			SoundPadRecordingSource.Microphone => await _soundpad.StartRecordingMicrophone(),
			SoundPadRecordingSource.Speakers => await _soundpad.StartRecordingSpeakers(),
			_ => await _soundpad.StartRecording()
		});

	public async Task StopRecordingAsync() => Ensure(await _soundpad.StopRecording());

	private static SoundPadSound Map(Sound sound)
		=> new(sound.Index,
			sound.Title ?? string.Empty,
			string.IsNullOrWhiteSpace(sound.Artist) ? null : sound.Artist,
			string.IsNullOrWhiteSpace(sound.Url) ? null : sound.Url,
			SoundPadDurations.Parse(sound.Duration),
			sound.LastPlayedOn is { } lastPlayed && lastPlayed != default ? lastPlayed : null);

	private static T Value<T>(ResponseBase<T> response)
		=> response.IsSuccessful
			? response.Value
			: throw new SoundPadCommandException(response.ErrorMessage ?? "SoundPad rejected the request");

	private static void Ensure(NoContentResponse response)
	{
		if (!response.IsSuccessful)
		{
			throw new SoundPadCommandException("SoundPad rejected the request");
		}
	}
}

internal static class SoundPadDurations
{
	private static readonly string[] _formats = [@"m\:ss", @"mm\:ss", @"h\:mm\:ss", @"hh\:mm\:ss"];

	public static TimeSpan? Parse(string? value)
		=> !string.IsNullOrWhiteSpace(value) &&
			TimeSpan.TryParseExact(value.Trim(), _formats, CultureInfo.InvariantCulture, out var duration)
				? duration
				: null;
}
