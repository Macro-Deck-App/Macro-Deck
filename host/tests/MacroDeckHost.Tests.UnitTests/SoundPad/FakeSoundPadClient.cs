using System.Globalization;
using MacroDeckHost.Integrations.SoundPad;

namespace MacroDeckHost.Tests.UnitTests.SoundPad;

internal sealed class FakeSoundPadClient : ISoundPadClient
{
	public bool Reachable { get; set; } = true;

	public bool HangCalls { get; set; }

	public bool HangConnect { get; set; }

	public bool RejectsMuteQuery { get; set; }

	public SoundPadPlayStatus Status { get; set; } = SoundPadPlayStatus.Stopped;

	public long PositionMs { get; set; }

	public long DurationMs { get; set; }

	public long RecordingPositionMs { get; set; }

	public int Volume { get; set; } = 70;

	public bool Muted { get; set; }

	public List<SoundPadSound> Sounds { get; set; } = [];

	public List<SoundPadCategory> Categories { get; set; } = [];

	public List<string> Commands { get; } = [];

	public int ConnectAttempts { get; private set; }

	public bool Disposed { get; private set; }

	public bool IsConnected { get; private set; }

	public Task ConnectAsync()
	{
		ConnectAttempts++;
		if (HangConnect)
		{
			return new TaskCompletionSource().Task;
		}

		if (!Reachable)
		{
			throw new TimeoutException("The SoundPad pipe is not there.");
		}

		IsConnected = true;
		return Task.CompletedTask;
	}

	public void Disconnect() => IsConnected = false;

	public void Dispose()
	{
		Disposed = true;
		IsConnected = false;
	}

	public Task<string> GetVersionAsync() => Read("4.0.35");

	public Task<SoundPadPlayStatus> GetPlayStatusAsync() => Read(Status);

	public Task<long> GetPlaybackPositionAsync() => Read(PositionMs);

	public Task<long> GetPlaybackDurationAsync() => Read(DurationMs);

	public Task<long> GetRecordingPositionAsync() => Read(RecordingPositionMs);

	public Task<int> GetVolumeAsync() => Read(Volume);

	public Task<bool> IsMutedAsync()
		=> RejectsMuteQuery ? throw new SoundPadCommandException("R-501") : Read(Muted);

	public Task<IReadOnlyList<SoundPadSound>> GetSoundsAsync() => Read<IReadOnlyList<SoundPadSound>>(Sounds.ToList());

	public Task<IReadOnlyList<SoundPadCategory>> GetCategoriesAsync()
		=> Read<IReadOnlyList<SoundPadCategory>>(Categories.ToList());

	public Task PlaySoundAsync(int index) => Run($"play {index}");

	public Task PlayRandomSoundAsync(int? categoryIndex, bool speakers, bool microphone)
		=> Run($"random {categoryIndex?.ToString(CultureInfo.InvariantCulture) ?? "all"} speakers={speakers} microphone={microphone}");

	public Task PlayCurrentSoundAgainAsync() => Run("play again");

	public Task PlayNextSoundAsync() => Run("next");

	public Task PlayPreviousSoundAsync() => Run("previous");

	public Task StopSoundAsync() => Run("stop");

	public Task TogglePauseAsync() => Run("toggle pause");

	public Task SeekAsync(long milliseconds) => Run($"seek {milliseconds}");

	public Task SetVolumeAsync(int volume) => Run($"volume {volume}");

	public Task ToggleMuteAsync() => Run("toggle mute");

	public Task StartRecordingAsync(SoundPadRecordingSource source) => Run($"record {source}");

	public Task StopRecordingAsync() => Run("stop recording");

	private Task<T> Read<T>(T value)
	{
		EnsureUsable();
		return HangCalls ? new TaskCompletionSource<T>().Task : Task.FromResult(value);
	}

	private Task Run(string command)
	{
		EnsureUsable();
		if (HangCalls)
		{
			return new TaskCompletionSource().Task;
		}

		Commands.Add(command);
		return Task.CompletedTask;
	}

	private void EnsureUsable()
	{
		if (!IsConnected)
		{
			throw new InvalidOperationException("Pipe hasn't been connected yet.");
		}
	}
}
