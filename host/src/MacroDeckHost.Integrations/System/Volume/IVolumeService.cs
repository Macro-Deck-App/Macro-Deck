namespace MacroDeckHost.Integrations.System.Volume;

public interface IVolumeService
{
	bool IsSupported { get; }

	event Action? Changed;

	Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(CancellationToken cancellationToken = default);

	Task<float?> GetVolumeAsync(AudioTarget target, CancellationToken cancellationToken = default);

	Task<bool> SetVolumeAsync(AudioTarget target, float level, CancellationToken cancellationToken = default);

	Task<bool?> GetMuteAsync(AudioTarget target, CancellationToken cancellationToken = default);

	Task<bool> SetMuteAsync(AudioTarget target, bool mute, CancellationToken cancellationToken = default);
}
