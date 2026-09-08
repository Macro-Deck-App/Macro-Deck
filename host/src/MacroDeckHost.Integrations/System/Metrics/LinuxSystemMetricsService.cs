using System.Runtime.Versioning;

namespace MacroDeckHost.Integrations.System.Metrics;

[SupportedOSPlatform("linux")]
internal sealed class LinuxSystemMetricsService : SystemMetricsServiceBase
{
	private readonly bool _hasNvidiaSmi = ProcessRunner.CommandExists("nvidia-smi");
	private readonly IReadOnlyList<string> _amdGpuBusyPercentPaths = LinuxDrmEnumerator.FindAmdGpuBusyPercentPaths();
	private readonly int _nvidiaGpuCount;

	public LinuxSystemMetricsService()
	{
		_nvidiaGpuCount = LinuxDrmEnumerator.CountNvidiaGpus() ?? (_hasNvidiaSmi ? 1 : 0);
	}

	public override int GpuCount => _nvidiaGpuCount + _amdGpuBusyPercentPaths.Count;

	protected override async Task<CpuTimes?> ReadCpuTimesAsync(CancellationToken cancellationToken)
		=> LinuxMetricsParser.ParseProcStat(await File.ReadAllTextAsync("/proc/stat", cancellationToken));

	protected override async Task<MemoryInfo?> ReadMemoryAsync(CancellationToken cancellationToken)
		=> LinuxMetricsParser.ParseMemInfo(await File.ReadAllTextAsync("/proc/meminfo", cancellationToken));

	protected override async Task<IReadOnlyList<GpuSample>> ReadGpuSnapshotAsync(CancellationToken cancellationToken)
	{
		var samples = new List<GpuSample>(GpuCount);

		if (_nvidiaGpuCount > 0)
		{
			var nvidia = await ReadNvidiaGpusAsync(cancellationToken);
			for (var index = 0; index < _nvidiaGpuCount; index++)
			{
				samples.Add(index < nvidia.Count ? nvidia[index] : new GpuSample(null, null));
			}
		}

		foreach (var path in _amdGpuBusyPercentPaths)
		{
			double? usage;
			try
			{
				usage = LinuxMetricsParser.ParseGpuBusyPercent(await File.ReadAllTextAsync(path, cancellationToken));
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				usage = null;
			}

			samples.Add(new GpuSample(null, usage));
		}

		return samples;
	}

	private async Task<IReadOnlyList<GpuSample>> ReadNvidiaGpusAsync(CancellationToken cancellationToken)
	{
		if (!_hasNvidiaSmi)
		{
			return [];
		}

		try
		{
			var output = await ProcessRunner.RunAsync("nvidia-smi",
				["--query-gpu=index,name,utilization.gpu", "--format=csv,noheader,nounits"],
				cancellationToken);
			return NvidiaSmiParser.ParseGpus(output);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			return [];
		}
	}
}
