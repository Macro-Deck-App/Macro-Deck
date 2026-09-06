using System.Runtime.Versioning;

namespace MacroDeckHost.Integrations.System.Metrics;

[SupportedOSPlatform("linux")]
internal sealed class LinuxSystemMetricsService : SystemMetricsServiceBase
{
	private readonly bool _hasNvidiaSmi = ProcessRunner.CommandExists("nvidia-smi");
	private readonly string? _amdGpuBusyPercentPath = FindAmdGpuBusyPercentPath();

	public override bool IsGpuSupported => _hasNvidiaSmi || _amdGpuBusyPercentPath is not null;

	protected override async Task<CpuTimes?> ReadCpuTimesAsync(CancellationToken cancellationToken)
		=> LinuxMetricsParser.ParseProcStat(await File.ReadAllTextAsync("/proc/stat", cancellationToken));

	protected override async Task<MemoryInfo?> ReadMemoryAsync(CancellationToken cancellationToken)
		=> LinuxMetricsParser.ParseMemInfo(await File.ReadAllTextAsync("/proc/meminfo", cancellationToken));

	protected override async Task<double?> ReadGpuUsageAsync(CancellationToken cancellationToken)
	{
		if (_hasNvidiaSmi)
		{
			var output = await ProcessRunner.RunAsync("nvidia-smi",
				["--query-gpu=utilization.gpu", "--format=csv,noheader,nounits"],
				cancellationToken);
			return NvidiaSmiParser.ParseUtilization(output);
		}

		if (_amdGpuBusyPercentPath is not null)
		{
			var content = await File.ReadAllTextAsync(_amdGpuBusyPercentPath, cancellationToken);
			return LinuxMetricsParser.ParseGpuBusyPercent(content);
		}

		return null;
	}

	protected override async Task<string?> ReadGpuNameAsync(CancellationToken cancellationToken)
	{
		if (!_hasNvidiaSmi)
		{
			return null;
		}

		var output = await ProcessRunner.RunAsync("nvidia-smi",
			["--query-gpu=name", "--format=csv,noheader"],
			cancellationToken);
		return NvidiaSmiParser.ParseName(output);
	}

	private static string? FindAmdGpuBusyPercentPath()
	{
		try
		{
			if (!Directory.Exists("/sys/class/drm"))
			{
				return null;
			}

			foreach (var card in Directory.GetDirectories("/sys/class/drm", "card*").Order(StringComparer.Ordinal))
			{
				var path = Path.Combine(card, "device", "gpu_busy_percent");
				if (File.Exists(path))
				{
					return path;
				}
			}

			return null;
		}
		catch
		{
			return null;
		}
	}
}
