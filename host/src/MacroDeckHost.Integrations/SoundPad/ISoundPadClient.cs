namespace MacroDeckHost.Integrations.SoundPad;

internal enum SoundPadPlayStatus
{
	Stopped,
	Playing,
	Paused,
	Seeking
}

internal enum SoundPadRecordingSource
{
	Default,
	Microphone,
	Speakers
}

internal sealed record SoundPadSound(
	int Index,
	string Title,
	string? Artist,
	string? Path,
	TimeSpan? Duration,
	DateTime? LastPlayedOn);

internal sealed record SoundPadCategory(int Index, string Name);

internal sealed class SoundPadCommandException(string message) : Exception(message);

internal interface ISoundPadClient : IDisposable
{
	bool IsConnected { get; }

	Task ConnectAsync();

	void Disconnect();

	Task<string> GetVersionAsync();

	Task<SoundPadPlayStatus> GetPlayStatusAsync();

	Task<long> GetPlaybackPositionAsync();

	Task<long> GetPlaybackDurationAsync();

	Task<long> GetRecordingPositionAsync();

	Task<int> GetVolumeAsync();

	Task<bool> IsMutedAsync();

	Task<IReadOnlyList<SoundPadSound>> GetSoundsAsync();

	Task<IReadOnlyList<SoundPadCategory>> GetCategoriesAsync();

	Task PlaySoundAsync(int index);

	Task PlayRandomSoundAsync(int? categoryIndex, bool speakers, bool microphone);

	Task PlayCurrentSoundAgainAsync();

	Task PlayNextSoundAsync();

	Task PlayPreviousSoundAsync();

	Task StopSoundAsync();

	Task TogglePauseAsync();

	Task SeekAsync(long milliseconds);

	Task SetVolumeAsync(int volume);

	Task ToggleMuteAsync();

	Task StartRecordingAsync(SoundPadRecordingSource source);

	Task StopRecordingAsync();
}
