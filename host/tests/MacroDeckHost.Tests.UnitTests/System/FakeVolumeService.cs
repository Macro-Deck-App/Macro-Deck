using MacroDeckHost.Integrations.System.Volume;

namespace MacroDeckHost.Tests.UnitTests.System;

internal sealed class FakeVolumeService : IVolumeService
{
	private readonly Dictionary<AudioTarget, float?> _volumes = [];
	private readonly Dictionary<AudioTarget, bool?> _mutes = [];

	public FakeVolumeService(bool supported = true)
	{
		IsSupported = supported;
	}

	public bool IsSupported { get; }

	public float? Volume
	{
		get => VolumeOf(AudioTarget.DefaultOutput);
		set => _volumes[AudioTarget.DefaultOutput] = value;
	}

	public bool? Muted
	{
		get => MutedOf(AudioTarget.DefaultOutput);
		set => _mutes[AudioTarget.DefaultOutput] = value;
	}

	public List<AudioDevice> Devices { get; } = [];

	public bool SetsSucceed { get; set; } = true;

	public event Action? Changed;

	public void RaiseChanged() => Changed?.Invoke();

	public float? VolumeOf(AudioTarget target) => _volumes.GetValueOrDefault(target);

	public bool? MutedOf(AudioTarget target) => _mutes.GetValueOrDefault(target);

	public void Set(AudioTarget target, float? volume, bool? muted)
	{
		_volumes[target] = volume;
		_mutes[target] = muted;
	}

	public Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<AudioDevice>>([.. Devices]);

	public Task<float?> GetVolumeAsync(AudioTarget target, CancellationToken cancellationToken = default)
		=> Task.FromResult(VolumeOf(target));

	public Task<bool> SetVolumeAsync(AudioTarget target, float level, CancellationToken cancellationToken = default)
	{
		if (SetsSucceed)
		{
			_volumes[target] = level;
		}

		return Task.FromResult(SetsSucceed);
	}

	public Task<bool?> GetMuteAsync(AudioTarget target, CancellationToken cancellationToken = default)
		=> Task.FromResult(MutedOf(target));

	public Task<bool> SetMuteAsync(AudioTarget target, bool mute, CancellationToken cancellationToken = default)
	{
		if (SetsSucceed)
		{
			_mutes[target] = mute;
		}

		return Task.FromResult(SetsSucceed);
	}
}
