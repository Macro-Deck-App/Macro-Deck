using System.Globalization;
using System.Text;

namespace MacroDeckHost.Integrations.System.Metrics;

internal readonly record struct DeviceNumber(int Major, int Minor)
{
	public bool IsAnonymous => Major == 0;
}

internal sealed record LinuxMount(string MountPoint, string FileSystem, string Source, DeviceNumber Device);

internal static class LinuxDiskParser
{
	private const int SectorBytes = 512;

	private static readonly HashSet<string> _poolFileSystems = new(StringComparer.Ordinal) { "zfs", "btrfs" };
	private static readonly HashSet<string> _skippedFileSystems = new(StringComparer.Ordinal) { "squashfs", "overlay" };
	private static readonly string[] _skippedMountRoots = ["/boot", "/snap"];

	public static IReadOnlyList<LinuxMount> ParseMountInfo(string content)
	{
		var byDisk = new Dictionary<string, LinuxMount>(StringComparer.Ordinal);
		foreach (var line in content.Split('\n'))
		{
			if (ParseMountLine(line) is not { } mount || !IsDisk(mount))
			{
				continue;
			}

			var key = mount.FileSystem == "zfs"
				? "zfs:" + mount.Source.Split('/')[0]
				: string.Create(CultureInfo.InvariantCulture, $"{mount.Device.Major}:{mount.Device.Minor}");
			if (!byDisk.TryGetValue(key, out var existing) || IsPreferred(mount.MountPoint, existing.MountPoint))
			{
				byDisk[key] = mount;
			}
		}

		return byDisk.Values.ToList();
	}

	public static Dictionary<DeviceNumber, DiskCounters> ParseDiskStats(string content)
	{
		var stats = new Dictionary<DeviceNumber, DiskCounters>();
		foreach (var line in content.Split('\n'))
		{
			var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (fields.Length < 11 ||
				!int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
				!int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
				!TryParseCounter(fields[5], out var sectorsRead) ||
				!TryParseCounter(fields[6], out var msReading) ||
				!TryParseCounter(fields[9], out var sectorsWritten) ||
				!TryParseCounter(fields[10], out var msWriting))
			{
				continue;
			}

			stats[new DeviceNumber(major, minor)] = new DiskCounters(sectorsRead * SectorBytes,
				sectorsWritten * SectorBytes,
				TimeSpan.FromMilliseconds(msReading),
				TimeSpan.FromMilliseconds(msWriting));
		}

		return stats;
	}

	public static DeviceNumber? ParseDeviceNumber(string content)
	{
		var parts = content.Trim().Split(':');
		return parts.Length == 2 &&
			int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) &&
			int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
				? new DeviceNumber(major, minor)
				: null;
	}

	// udev escapes characters it cannot put in a file name as \xNN, a space in a label being \x20.
	public static string DecodeUdevLabel(string label) => Unescape(label, 'x', 16, 2);

	private static LinuxMount? ParseMountLine(string line)
	{
		var separator = line.IndexOf(" - ", StringComparison.Ordinal);
		if (separator < 0)
		{
			return null;
		}

		var head = line[..separator].Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var tail = line[(separator + 3)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (head.Length < 5 || tail.Length < 2 || ParseDeviceNumber(head[2]) is not { } device)
		{
			return null;
		}

		// mountinfo writes space, tab, newline and backslash in paths as three-digit octal escapes.
		return new LinuxMount(Unescape(head[4], null, 8, 3), tail[0], Unescape(tail[1], null, 8, 3), device);
	}

	private static bool IsDisk(LinuxMount mount)
	{
		if (_skippedFileSystems.Contains(mount.FileSystem) ||
			_skippedMountRoots.Any(root => mount.MountPoint == root ||
				mount.MountPoint.StartsWith(root + "/", StringComparison.Ordinal)))
		{
			return false;
		}

		if (_poolFileSystems.Contains(mount.FileSystem))
		{
			return true;
		}

		return mount.Source.StartsWith("/dev/", StringComparison.Ordinal) &&
			!mount.Source.StartsWith("/dev/loop", StringComparison.Ordinal);
	}

	private static bool IsPreferred(string candidate, string current)
		=> candidate.Length != current.Length
			? candidate.Length < current.Length
			: string.CompareOrdinal(candidate, current) < 0;

	private static bool TryParseCounter(string field, out ulong value)
		=> ulong.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out value);

	private static string Unescape(string value, char? marker, int radix, int digits)
	{
		if (!value.Contains('\\'))
		{
			return value;
		}

		var prefix = marker is null ? 1 : 2;
		var builder = new StringBuilder(value.Length);
		var bytes = new List<byte>();
		for (var i = 0; i < value.Length; i++)
		{
			if (value[i] == '\\' &&
				i + prefix + digits <= value.Length &&
				(marker is null || value[i + 1] == marker) &&
				TryParseDigits(value.Substring(i + prefix, digits), radix, out var code))
			{
				bytes.Add(code);
				i += prefix + digits - 1;
				continue;
			}

			Flush(builder, bytes);
			builder.Append(value[i]);
		}

		Flush(builder, bytes);
		return builder.ToString();
	}

	private static bool TryParseDigits(string digits, int radix, out byte code)
	{
		code = 0;
		try
		{
			var parsed = Convert.ToInt32(digits, radix);
			if (parsed is < 0 or > byte.MaxValue)
			{
				return false;
			}

			code = (byte)parsed;
			return true;
		}
		catch (FormatException)
		{
			return false;
		}
	}

	private static void Flush(StringBuilder builder, List<byte> bytes)
	{
		if (bytes.Count == 0)
		{
			return;
		}

		builder.Append(Encoding.UTF8.GetString(bytes.ToArray()));
		bytes.Clear();
	}
}
