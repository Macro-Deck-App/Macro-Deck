namespace MacroDeckHost.Integrations.System.Volume;

internal sealed class NullVolumeService : IVolumeService
{
	public bool IsSupported => false;

	public Task<float?> GetVolumeAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult<float?>(null);

	public Task SetVolumeAsync(float level, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task<bool?> GetMuteAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult<bool?>(null);

	public Task SetMuteAsync(bool mute, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
