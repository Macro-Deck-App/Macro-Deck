namespace MacroDeckHost.Integrations.SinusBot;

internal interface ISinusBotClient
{
	Task AuthenticateAsync(string username, string password, CancellationToken cancellationToken);

	Task<IReadOnlyList<SinusBotInstance>> GetInstancesAsync(CancellationToken cancellationToken);

	Task<SinusBotInstanceStatus> GetInstanceStatusAsync(string instanceId, CancellationToken cancellationToken);

	Task<IReadOnlyList<SinusBotFile>> GetFilesAsync(string instanceId, CancellationToken cancellationToken);

	string GetThumbnailUrl(string instanceId, string thumbnailId);

	Task PlayAsync(string instanceId, CancellationToken cancellationToken);

	Task PauseAsync(string instanceId, CancellationToken cancellationToken);

	Task StopAsync(string instanceId, CancellationToken cancellationToken);

	Task PlayFileAsync(string instanceId, string fileId, CancellationToken cancellationToken);

	Task NextAsync(string instanceId, CancellationToken cancellationToken);

	Task PreviousAsync(string instanceId, CancellationToken cancellationToken);

	Task SeekAsync(string instanceId, int seconds, CancellationToken cancellationToken);

	Task SetVolumeAsync(string instanceId, int volumePercent, CancellationToken cancellationToken);

	Task IncreaseVolumeAsync(string instanceId, CancellationToken cancellationToken);

	Task DecreaseVolumeAsync(string instanceId, CancellationToken cancellationToken);

	Task SetShuffleAsync(string instanceId, bool enabled, CancellationToken cancellationToken);

	Task SetRepeatAsync(string instanceId, bool enabled, CancellationToken cancellationToken);
}
