namespace MacroDeckHost.Integrations.System.Volume;

public interface IVolumeService
{
	bool IsSupported { get; }

	event Action? Changed;

	Task<float?> GetVolumeAsync(CancellationToken cancellationToken = default);

	Task SetVolumeAsync(float level, CancellationToken cancellationToken = default);

	Task<bool?> GetMuteAsync(CancellationToken cancellationToken = default);

	Task SetMuteAsync(bool mute, CancellationToken cancellationToken = default);
}
