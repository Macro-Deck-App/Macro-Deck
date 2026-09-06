using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace MacroDeck.Plugin.Hosting.Transport;

// Identified by process id and start time together: ids are reused, so a pid alone could match a
// stranger that inherited it, and the plugin would exit against the wrong process or never at all.
internal readonly record struct HostProcessTarget(int ProcessId, DateTimeOffset StartedAt)
{
	// Process.StartTime on Linux is reconstructed from /proc and is not bit-stable across reads, so an
	// exact comparison would flag a live host as gone.
	private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(2);

	// Anything short of a positive id with a parseable timestamp means no watch at all. A fallback to
	// pid 0 or a negative id would reach the OS, where those select whole process groups.
	public static bool TryParse(string? processId, string? startedAt, out HostProcessTarget target)
	{
		target = default;

		if (string.IsNullOrEmpty(processId) || string.IsNullOrEmpty(startedAt))
		{
			return false;
		}

		if (!int.TryParse(processId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) || pid <= 0)
		{
			return false;
		}

		if (!DateTimeOffset.TryParseExact(startedAt,
			"O",
			CultureInfo.InvariantCulture,
			DateTimeStyles.None,
			out var parsedStartedAt))
		{
			return false;
		}

		target = new HostProcessTarget(pid, parsedStartedAt);
		return true;
	}

	public bool IsAlive()
	{
		Process process;

		try
		{
			process = Process.GetProcessById(ProcessId);
		}
		catch (ArgumentException)
		{
			return false;
		}

		try
		{
			var drift = process.StartTime.ToUniversalTime() - StartedAt.UtcDateTime;
			return drift.Duration() <= StartTimeTolerance;
		}
		catch (Exception exception) when (exception is InvalidOperationException
			or NotSupportedException
			or Win32Exception)
		{
			return false;
		}
		finally
		{
			process.Dispose();
		}
	}
}
