using System.ComponentModel;
using System.Diagnostics;

namespace MacroDeckHost.Infrastructure.Plugins;

internal static class HostProcessIdentity
{
	private static readonly Lazy<DateTimeOffset> _startedAt = new(Read);

	public static int ProcessId => Environment.ProcessId;

	public static DateTimeOffset StartedAt => _startedAt.Value;

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
