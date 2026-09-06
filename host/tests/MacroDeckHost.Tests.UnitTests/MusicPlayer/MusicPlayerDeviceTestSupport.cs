using MacroDeck.Sdk.MusicPlayer;
using MacroDeckHost.Application.MusicPlayer;

namespace MacroDeckHost.Tests.UnitTests.MusicPlayer;

/// <summary>A registry holding one player under one instance id, and nothing under any other.</summary>
internal sealed class SinglePlayerRegistry : IMusicPlayerRegistry
{
	private readonly IMusicPlayer? _player;
	private readonly string _instanceId;

	public SinglePlayerRegistry(IMusicPlayer? player, string instanceId = "inst-1")
	{
		_player = player;
		_instanceId = instanceId;
	}

	public IReadOnlyList<MusicPlayerInstanceDescriptor> GetInstances()
		=> [new(_instanceId, "test", "Test", "Test", false)];

	public IMusicPlayer? GetPlayer(string instanceId)
		=> string.Equals(instanceId, _instanceId, StringComparison.Ordinal) ? _player : null;

	public IMusicPlayer? DefaultPlayer => _player;
}

internal class FakeMusicPlayer : IMusicPlayer
{
	public Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult(MusicPlayerState.Disconnected);

	public Task<MusicPlayerArtwork?> GetArtworkAsync(string artworkId, CancellationToken cancellationToken = default)
		=> Task.FromResult<MusicPlayerArtwork?>(null);

	public Task PlayAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task NextAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task PreviousAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
		=> Task.CompletedTask;

	public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default)
		=> Task.CompletedTask;
}

/// <summary>A player that can list devices and switch between them.</summary>
internal sealed class FakeDevicePlayer : FakeMusicPlayer, IMusicPlayerDeviceProvider
{
	public IReadOnlyList<MusicPlayerDevice> Devices { get; set; } = [];

	public (string DeviceId, bool StartPlayback)? Transferred { get; private set; }

	/// <summary>Thrown by <see cref="GetDevicesAsync" />, which the contract says reports a failed read by
	/// throwing rather than by answering with an empty list.</summary>
	public Exception? ListFailure { get; set; }

	public Exception? TransferFailure { get; set; }

	public int DeviceCalls { get; private set; }

	public Task<IReadOnlyList<MusicPlayerDevice>> GetDevicesAsync(CancellationToken cancellationToken)
	{
		DeviceCalls++;

		return ListFailure is not null
			? Task.FromException<IReadOnlyList<MusicPlayerDevice>>(ListFailure)
			: Task.FromResult(Devices);
	}

	public Task TransferPlaybackAsync(string deviceId, bool startPlayback, CancellationToken cancellationToken)
	{
		if (TransferFailure is not null)
		{
			return Task.FromException(TransferFailure);
		}

		Transferred = (deviceId, startPlayback);

		return Task.CompletedTask;
	}
}
