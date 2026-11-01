using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace MacroDeckHost.Infrastructure.Plugins;

internal static class HostProcessIdentity
{
	private const int StartTimeField = 19;
	private const int UserHz = 100;

	private static readonly Lazy<DateTimeOffset> _startedAt = new(Read);

	public static int ProcessId => Environment.ProcessId;

	public static DateTimeOffset StartedAt => _startedAt.Value;

	public static DateTimeOffset StartedAtForLaunch()
	{
		if (!OperatingSystem.IsLinux())
		{
			return StartedAt;
		}

		try
		{
			return FromProcFs(File.ReadAllText("/proc/self/stat"),
					File.ReadAllText("/proc/uptime"),
					DateTimeOffset.UtcNow)
				?? StartedAt;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return StartedAt;
		}
	}

	// .NET on Linux derives StartTime from a boot time cached once per process, so a clock step makes
	// the plugin's reading differ from a cached one. The base here is taken at each launch to match.
	internal static DateTimeOffset? FromProcFs(string stat, string uptime, DateTimeOffset now)
	{
		var commandEnd = stat.LastIndexOf(')');
		if (commandEnd < 0)
		{
			return null;
		}

		var fields = stat[(commandEnd + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (fields.Length <= StartTimeField
			|| !long.TryParse(fields[StartTimeField], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
			|| !double.TryParse(uptime.Split(' ')[0],
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var seconds))
		{
			return null;
		}

		return now - TimeSpan.FromSeconds(seconds) + TimeSpan.FromSeconds(ticks / (double)UserHz);
	}

	private static DateTimeOffset Read()
	{
		try
		{
			using var process = Process.GetCurrentProcess();
			return process.StartTime.ToUniversalTime();
		}
		catch (Exception ex) when (ex is InvalidOperationException
			or Win32Exception
			or NotSupportedException
			or PlatformNotSupportedException)
		{
			return DateTimeOffset.UtcNow;
		}
	}
}
