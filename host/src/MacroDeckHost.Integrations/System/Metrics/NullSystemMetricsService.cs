namespace MacroDeckHost.Integrations.System.Metrics;

internal sealed class NullSystemMetricsService : ISystemMetricsService
{
	public bool IsSupported => false;

	public bool IsGpuSupported => false;

	public int GpuCount => 0;

	public Task<double?> GetCpuUsageAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult<double?>(null);

	public Task<MemoryInfo?> GetMemoryAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult<MemoryInfo?>(null);

	public Task<double?> GetGpuUsageAsync(int gpuIndex, CancellationToken cancellationToken = default)
		=> Task.FromResult<double?>(null);

	public Task<string?> GetGpuNameAsync(int gpuIndex, CancellationToken cancellationToken = default)
		=> Task.FromResult<string?>(null);
}
