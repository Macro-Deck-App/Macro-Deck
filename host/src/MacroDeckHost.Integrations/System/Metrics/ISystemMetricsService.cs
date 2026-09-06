namespace MacroDeckHost.Integrations.System.Metrics;

public interface ISystemMetricsService
{
	bool IsSupported { get; }

	bool IsGpuSupported { get; }

	Task<double?> GetCpuUsageAsync(CancellationToken cancellationToken = default);

	Task<MemoryInfo?> GetMemoryAsync(CancellationToken cancellationToken = default);

	Task<double?> GetGpuUsageAsync(CancellationToken cancellationToken = default);

	Task<string?> GetGpuNameAsync(CancellationToken cancellationToken = default);
}

public sealed record MemoryInfo(long TotalBytes, long AvailableBytes)
{
	public long UsedBytes => TotalBytes - AvailableBytes;
}
