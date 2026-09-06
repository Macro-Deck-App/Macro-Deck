using MacroDeckHost.Integrations.SinusBot;

namespace MacroDeckHost.Tests.UnitTests.SinusBot;

internal sealed class FakeSinusBotClient : ISinusBotClient
{
	public SinusBotInstanceStatus? Status { get; set; }

	public Exception? StatusException { get; set; }

	public Exception? FilesException { get; set; }

	public IReadOnlyList<SinusBotFile> Files { get; set; } = [];

	public Exception? CommandException { get; set; }

	public Exception? AuthException { get; set; }

	public IReadOnlyList<SinusBotInstance> Instances { get; set; } = [];

	public List<(string Method, string InstanceId, object? Arg)> Calls { get; } = [];

	public Task AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
		=> AuthException is not null ? Task.FromException(AuthException) : Task.CompletedTask;

	public Task<IReadOnlyList<SinusBotInstance>> GetInstancesAsync(CancellationToken cancellationToken)
		=> Task.FromResult(Instances);

	public Task<SinusBotInstanceStatus> GetInstanceStatusAsync(string instanceId, CancellationToken cancellationToken)
	{
		if (StatusException is not null)
		{
			return Task.FromException<SinusBotInstanceStatus>(StatusException);
		}

		return Task.FromResult(Status ?? new SinusBotInstanceStatus());
	}

	public Task<IReadOnlyList<SinusBotFile>> GetFilesAsync(string instanceId, CancellationToken cancellationToken)
	{
		if (FilesException is not null)
		{
			return Task.FromException<IReadOnlyList<SinusBotFile>>(FilesException);
		}

		return Task.FromResult(Files);
	}

	public string GetThumbnailUrl(string instanceId, string thumbnailId)
	{
		Record("GetThumbnailUrl", instanceId, null);
		return Guid.NewGuid().ToString();
	}

	private Task Record(string method, string instanceId, object? arg)
	{
		if (CommandException is not null)
		{
			return Task.FromException(CommandException);
		}

		Calls.Add((method, instanceId, arg));
		return Task.CompletedTask;
	}

	public Task PlayAsync(string instanceId, CancellationToken cancellationToken)
		=> Record("Play", instanceId, null);

	public Task PauseAsync(string instanceId, CancellationToken cancellationToken)
		=> Record("Pause", instanceId, null);

	public Task StopAsync(string instanceId, CancellationToken cancellationToken)
		=> Record("Stop", instanceId, null);

	public Task PlayFileAsync(string instanceId, string fileId, CancellationToken cancellationToken)
		=> Record("PlayFile", instanceId, fileId);

	public Task NextAsync(string instanceId, CancellationToken cancellationToken)
		=> Record("Next", instanceId, null);

	public Task PreviousAsync(string instanceId, CancellationToken cancellationToken)
		=> Record("Previous", instanceId, null);

	public Task SeekAsync(string instanceId, int seconds, CancellationToken cancellationToken)
		=> Record("Seek", instanceId, seconds);

	public Task SetVolumeAsync(string instanceId, int volumePercent, CancellationToken cancellationToken)
		=> Record("SetVolume", instanceId, volumePercent);

	public Task IncreaseVolumeAsync(string instanceId, CancellationToken cancellationToken)
		=> Record("IncreaseVolume", instanceId, null);

	public Task DecreaseVolumeAsync(string instanceId, CancellationToken cancellationToken)
		=> Record("DecreaseVolume", instanceId, null);

	public Task SetShuffleAsync(string instanceId, bool enabled, CancellationToken cancellationToken)
		=> Record("SetShuffle", instanceId, enabled);

	public Task SetRepeatAsync(string instanceId, bool enabled, CancellationToken cancellationToken)
		=> Record("SetRepeat", instanceId, enabled);
}
