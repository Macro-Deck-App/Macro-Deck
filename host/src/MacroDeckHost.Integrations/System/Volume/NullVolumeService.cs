namespace MacroDeckHost.Integrations.System.Volume;

internal sealed class NullVolumeService : IVolumeService
{
	public bool IsSupported => false;

	public event Action? Changed
	{
		add { }
		remove { }
	}

	public Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<AudioDevice>>([]);

	public Task<float?> GetVolumeAsync(AudioTarget target, CancellationToken cancellationToken = default)
		=> Task.FromResult<float?>(null);

	public Task<bool> SetVolumeAsync(AudioTarget target, float level, CancellationToken cancellationToken = default)
		=> Task.FromResult(false);

	public Task<bool?> GetMuteAsync(AudioTarget target, CancellationToken cancellationToken = default)
		=> Task.FromResult<bool?>(null);

	public Task<bool> SetMuteAsync(AudioTarget target, bool mute, CancellationToken cancellationToken = default)
		=> Task.FromResult(false);
}
