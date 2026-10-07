using System.Globalization;
using MacroDeckHost.Integrations.System.Metrics;
using MacroDeck.Localization;
using MacroDeck.Sdk.Variables;
using DiskStrings = MacroDeckHost.Localization.AppStrings.Integrations.System.Variables;

namespace MacroDeckHost.Integrations.System;

internal static class DiskVariables
{
	public const int MaxSlots = 8;

	private const string IdPrefix = "system-disk-";

	private static readonly string _systemMountPoint = OperatingSystem.IsWindows()
		? Path.GetPathRoot(Environment.SystemDirectory) ?? "/"
		: "/";

	private static readonly TimeSpan _capacityRefreshInterval = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan _activityRefreshInterval = TimeSpan.FromSeconds(2);

	public static IEnumerable<VariableDefinition> Declare(int index)
	{
		var label = index.ToString(CultureInfo.InvariantCulture);
		yield return Text(index, "name", DiskStrings.DiskNameIndexed(index: label));
		yield return Text(index, "mount_point", DiskStrings.DiskMountPointIndexed(index: label));
		yield return Text(index, "file_system", DiskStrings.DiskFileSystemIndexed(index: label));
		yield return Number(index, "total_bytes", DiskStrings.DiskTotalIndexed(index: label), VariableSemanticKinds.Bytes, "B",
			_capacityRefreshInterval);
		yield return Number(index, "used_bytes", DiskStrings.DiskUsedIndexed(index: label), VariableSemanticKinds.Bytes, "B",
			_capacityRefreshInterval);
		yield return Number(index, "free_bytes", DiskStrings.DiskFreeIndexed(index: label), VariableSemanticKinds.Bytes, "B",
			_capacityRefreshInterval);
		yield return Number(index, "usage_percent", DiskStrings.DiskUsageIndexed(index: label),
			VariableSemanticKinds.Percentage, "%", _capacityRefreshInterval);
		yield return Number(index, "read_bytes_per_second", DiskStrings.DiskReadSpeedIndexed(index: label),
			VariableSemanticKinds.BytesPerSecond, "B/s", _activityRefreshInterval);
		yield return Number(index, "write_bytes_per_second", DiskStrings.DiskWriteSpeedIndexed(index: label),
			VariableSemanticKinds.BytesPerSecond, "B/s", _activityRefreshInterval);
		yield return Number(index, "read_usage_percent", DiskStrings.DiskReadActivityIndexed(index: label),
			VariableSemanticKinds.Percentage, "%", _activityRefreshInterval);
		yield return Number(index, "write_usage_percent", DiskStrings.DiskWriteActivityIndexed(index: label),
			VariableSemanticKinds.Percentage, "%", _activityRefreshInterval);
	}

	public static bool TryParseId(string localId, out int index, out string statistic)
	{
		index = -1;
		statistic = string.Empty;
		if (!localId.StartsWith(IdPrefix, StringComparison.Ordinal))
		{
			return false;
		}

		var rest = localId[IdPrefix.Length..];
		var separator = rest.IndexOf('-');
		if (separator <= 0 ||
			!int.TryParse(rest[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out index))
		{
			return false;
		}

		statistic = rest[(separator + 1)..];
		return statistic.Length > 0;
	}

	public static object? Read(DiskSample disk, string statistic) => statistic switch
	{
		"name" => disk.Name,
		"mount-point" => disk.MountPoint,
		"file-system" => disk.FileSystem,
		"total-bytes" => disk.TotalBytes,
		"used-bytes" => disk.UsedBytes,
		"free-bytes" => disk.FreeBytes,
		"usage-percent" => disk is { TotalBytes: > 0, UsedBytes: { } used }
			? (int)Math.Round(used * 100.0 / disk.TotalBytes.Value)
			: null,
		"read-bytes-per-second" => Rate(disk.Activity?.ReadBytesPerSecond),
		"write-bytes-per-second" => Rate(disk.Activity?.WriteBytesPerSecond),
		"read-usage-percent" => Percent(disk.Activity?.ReadActivePercent),
		"write-usage-percent" => Percent(disk.Activity?.WriteActivePercent),
		_ => null
	};

	// A disk keeps its slot while present, a new disk takes the lowest free one, and a slot never
	// shrinks away: its variables stay declared and read as unavailable while it is empty.
	public static IReadOnlyList<string?> AssignSlots(IReadOnlyList<string?> current, IEnumerable<string> mountPoints)
	{
		var present = mountPoints
			.Distinct(StringComparer.Ordinal)
			.OrderBy(mountPoint => string.Equals(mountPoint, _systemMountPoint, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
			.ThenBy(mountPoint => mountPoint, StringComparer.OrdinalIgnoreCase)
			.ToList();
		var presentSet = present.ToHashSet(StringComparer.Ordinal);

		var slots = current.Select(mountPoint => mountPoint is not null && presentSet.Contains(mountPoint) ? mountPoint : null)
			.ToList();
		foreach (var mountPoint in present.Where(mountPoint => !slots.Contains(mountPoint)))
		{
			var free = slots.IndexOf(null);
			if (free >= 0)
			{
				slots[free] = mountPoint;
			}
			else if (slots.Count < MaxSlots)
			{
				slots.Add(mountPoint);
			}
		}

		return slots;
	}

	private static long? Rate(double? bytesPerSecond)
		=> bytesPerSecond is { } rate ? (long)Math.Round(rate) : null;

	private static int? Percent(double? percent)
		=> percent is { } value ? (int)Math.Round(value) : null;

	private static VariableDefinition Text(int index, string statistic, LocalizedText displayName)
		=> VariableDefinition.Eager($"system_disk_{index}_{statistic}", VariableType.Text,
				refreshInterval: _capacityRefreshInterval)
			with
			{
				DisplayName = displayName
			};

	private static VariableDefinition Number(
		int index,
		string statistic,
		LocalizedText displayName,
		string semanticKind,
		string unit,
		TimeSpan refreshInterval)
		=> VariableDefinition.Eager($"system_disk_{index}_{statistic}", VariableType.Numeric, 0, refreshInterval)
			with
			{
				DisplayName = displayName,
				Unit = unit,
				SemanticKind = semanticKind
			};
}
