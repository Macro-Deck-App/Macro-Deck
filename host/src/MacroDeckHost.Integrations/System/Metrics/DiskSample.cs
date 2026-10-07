namespace MacroDeckHost.Integrations.System.Metrics;

public sealed record DiskSample(
	string MountPoint,
	string? Name,
	string? FileSystem,
	long? TotalBytes,
	long? FreeBytes,
	DiskActivity? Activity)
{
	public long? UsedBytes => TotalBytes is { } total && FreeBytes is { } free ? Math.Max(0, total - free) : null;
}

public sealed record DiskActivity(
	double ReadBytesPerSecond,
	double WriteBytesPerSecond,
	double? ReadActivePercent,
	double? WriteActivePercent);

internal readonly record struct DiskCounters(ulong ReadBytes, ulong WriteBytes, TimeSpan? ReadTime, TimeSpan? WriteTime);

internal sealed record DiskReading(
	string MountPoint,
	string? Name,
	string? FileSystem,
	long? TotalBytes,
	long? FreeBytes,
	DiskCounters? Counters = null,
	DiskActivity? Activity = null);
