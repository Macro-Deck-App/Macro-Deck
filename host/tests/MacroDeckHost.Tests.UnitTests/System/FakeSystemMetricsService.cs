using MacroDeckHost.Integrations.System.Metrics;

namespace MacroDeckHost.Tests.UnitTests.System;

internal sealed class FakeSystemMetricsService : ISystemMetricsService
{
	public FakeSystemMetricsService(bool supported = true, bool gpuSupported = true)
	{
		IsSupported = supported;
		IsGpuSupported = gpuSupported;
	}

	public bool IsSupported { get; }

	public bool IsGpuSupported { get; }

	public double? CpuUsage { get; set; }

	public MemoryInfo? Memory { get; set; }

	public double? GpuUsage { get; set; }

	public string? GpuName { get; set; }

	public Task<double?> GetCpuUsageAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult(CpuUsage);

	public Task<MemoryInfo?> GetMemoryAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult(Memory);

	public Task<double?> GetGpuUsageAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult(GpuUsage);

	public Task<string?> GetGpuNameAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult(IsGpuSupported ? GpuName : null);
}
