using MacroDeckHost.Integrations.System.Metrics;

namespace MacroDeckHost.Tests.UnitTests.System;

internal sealed class FakeSystemMetricsService : ISystemMetricsService
{
	public FakeSystemMetricsService(bool supported = true, bool gpuSupported = true, int gpuCount = 1)
	{
		IsSupported = supported;
		GpuCount = gpuSupported ? gpuCount : 0;
	}

	public bool IsSupported { get; }

	public int GpuCount { get; }

	public bool IsGpuSupported => GpuCount > 0;

	public double? CpuUsage { get; set; }

	public MemoryInfo? Memory { get; set; }

	public double? GpuUsage { get; set; }

	public string? GpuName { get; set; }

	public Dictionary<int, double?> GpuUsageByIndex { get; } = new();

	public Dictionary<int, string?> GpuNameByIndex { get; } = new();

	public Task<double?> GetCpuUsageAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult(CpuUsage);

	public Task<MemoryInfo?> GetMemoryAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult(Memory);

	public Task<double?> GetGpuUsageAsync(int gpuIndex, CancellationToken cancellationToken = default)
		=> Task.FromResult(gpuIndex < 0 || gpuIndex >= GpuCount
			? null
			: GpuUsageByIndex.TryGetValue(gpuIndex, out var usage) ? usage : GpuUsage);

	public Task<string?> GetGpuNameAsync(int gpuIndex, CancellationToken cancellationToken = default)
		=> Task.FromResult(gpuIndex < 0 || gpuIndex >= GpuCount
			? null
			: GpuNameByIndex.TryGetValue(gpuIndex, out var name) ? name : GpuName);
}
