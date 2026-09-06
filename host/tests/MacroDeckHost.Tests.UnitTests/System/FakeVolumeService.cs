using MacroDeckHost.Integrations.System.Volume;

namespace MacroDeckHost.Tests.UnitTests.System;

internal sealed class FakeVolumeService : IVolumeService
{
	public FakeVolumeService(bool supported = true)
	{
		IsSupported = supported;
	}

	public bool IsSupported { get; }

	public float? Volume { get; set; }

	public bool? Muted { get; set; }

	public Task<float?> GetVolumeAsync(CancellationToken cancellationToken = default) => Task.FromResult(Volume);

	public Task SetVolumeAsync(float level, CancellationToken cancellationToken = default)
	{
		Volume = level;
		return Task.CompletedTask;
	}

	public Task<bool?> GetMuteAsync(CancellationToken cancellationToken = default) => Task.FromResult(Muted);

	public Task SetMuteAsync(bool mute, CancellationToken cancellationToken = default)
	{
		Muted = mute;
		return Task.CompletedTask;
	}
}
