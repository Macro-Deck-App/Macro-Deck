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

	protected override async Task<IReadOnlyList<DiskReading>> ReadDisksAsync(CancellationToken cancellationToken)
	{
		var mounts = LinuxDiskParser.ParseMountInfo(
			await File.ReadAllTextAsync("/proc/self/mountinfo", cancellationToken));
		var stats = LinuxDiskParser.ParseDiskStats(await ReadOptionalAsync("/proc/diskstats", cancellationToken));
		var labels = ReadLabels();

		var readings = new List<DiskReading>(mounts.Count);
		foreach (var mount in mounts)
		{
			var deviceName = ResolveDeviceName(mount.Source);
			var device = mount.Device.IsAnonymous && deviceName is not null
				? await ReadDeviceNumberAsync(deviceName, cancellationToken) ?? mount.Device
				: mount.Device;
			var (total, free) = ReadCapacity(mount.MountPoint);

			readings.Add(new DiskReading(mount.MountPoint,
				(deviceName is not null ? labels.GetValueOrDefault(deviceName) : null) ?? deviceName ?? mount.Source,
				mount.FileSystem,
				total,
				free,
				stats.TryGetValue(device, out var counters) ? counters : null));
		}

		return readings;
	}

	private static async Task<string> ReadOptionalAsync(string path, CancellationToken cancellationToken)
	{
		try
		{
			return await File.ReadAllTextAsync(path, cancellationToken);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return string.Empty;
		}
	}

	private static async Task<DeviceNumber?> ReadDeviceNumberAsync(string deviceName, CancellationToken cancellationToken)
	{
		var content = await ReadOptionalAsync($"/sys/class/block/{deviceName}/dev", cancellationToken);
		return LinuxDiskParser.ParseDeviceNumber(content);
	}

	private static string? ResolveDeviceName(string source)
	{
		if (!source.StartsWith("/dev/", StringComparison.Ordinal))
		{
			return null;
		}

		try
		{
			var target = new FileInfo(source).ResolveLinkTarget(returnFinalTarget: true);
			return Path.GetFileName(target?.FullName ?? source);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return Path.GetFileName(source);
		}
	}

	private static Dictionary<string, string> ReadLabels()
	{
		var labels = new Dictionary<string, string>(StringComparer.Ordinal);
		try
		{
			foreach (var link in new DirectoryInfo("/dev/disk/by-label").EnumerateFiles())
			{
				if (link.ResolveLinkTarget(returnFinalTarget: true) is { } target)
				{
					labels.TryAdd(Path.GetFileName(target.FullName), LinuxDiskParser.DecodeUdevLabel(link.Name));
				}
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}

		return labels;
	}

	private static (long? Total, long? Free) ReadCapacity(string mountPoint)
	{
		try
		{
			var drive = new DriveInfo(mountPoint);
			return (drive.TotalSize, drive.AvailableFreeSpace);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
		{
			return (null, null);
		}
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
