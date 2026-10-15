using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Integrations.Steam.Native;

internal sealed record SteamGameProcess(int Id, DateTime StartTime);

internal static class SteamGameProcesses
{
	private const uint ProcessQueryLimitedInformation = 0x1000;

	public static bool IsUnder(string path, string directory, bool ignoreCase)
	{
		if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(directory))
		{
			return false;
		}

		var root = directory.TrimEnd('/', '\\');
		var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

		return root.Length > 0
			&& path.Length > root.Length + 1
			&& path.StartsWith(root, comparison)
			&& path[root.Length] is '/' or '\\';
	}

	[SupportedOSPlatform("windows")]
	public static IReadOnlyList<SteamGameProcess> FindOnWindows(string installDirectory)
	{
		var found = new List<SteamGameProcess>();
		foreach (var process in Process.GetProcesses())
		{
			using (process)
			{
				if (ImagePath(process.Id) is { } path
					&& IsUnder(path, installDirectory, true)
					&& Identify(process) is { } identified)
				{
					found.Add(identified);
				}
			}
		}

		return found;
	}

	[SupportedOSPlatform("windows")]
	public static bool RequestClose(SteamGameProcess target)
	{
		using var process = Open(target);
		try
		{
			return process is not null && process.CloseMainWindow();
		}
		catch (InvalidOperationException)
		{
			return false;
		}
	}

	[SupportedOSPlatform("windows")]
	public static bool Kill(SteamGameProcess target)
	{
		using var process = Open(target);
		if (process is null)
		{
			return false;
		}

		try
		{
			process.Kill();
			return true;
		}
		catch (Exception exception) when (exception is Win32Exception
			or InvalidOperationException
			or NotSupportedException)
		{
			return false;
		}
	}

	// A process that cannot be inspected is reported as running, so a close never claims success it cannot see.
	public static bool IsRunning(SteamGameProcess target)
	{
		Process process;
		try
		{
			process = Process.GetProcessById(target.Id);
		}
		catch (ArgumentException)
		{
			return false;
		}

		using (process)
		{
			try
			{
				return !process.HasExited && process.StartTime == target.StartTime;
			}
			catch (Exception exception) when (exception is Win32Exception or NotSupportedException)
			{
				return true;
			}
			catch (InvalidOperationException)
			{
				return false;
			}
		}
	}

	// A process id can be reused once the game exits, so every action first checks the start time it was found with.
	private static Process? Open(SteamGameProcess target)
	{
		Process? process = null;
		try
		{
			process = Process.GetProcessById(target.Id);
			if (!process.HasExited && process.StartTime == target.StartTime)
			{
				return process;
			}
		}
		catch (Exception exception) when (exception is ArgumentException
			or InvalidOperationException
			or Win32Exception
			or NotSupportedException)
		{
		}

		process?.Dispose();
		return null;
	}

	private static SteamGameProcess? Identify(Process process)
	{
		try
		{
			return new SteamGameProcess(process.Id, process.StartTime);
		}
		catch (Exception exception) when (exception is InvalidOperationException
			or Win32Exception
			or NotSupportedException)
		{
			return null;
		}
	}

	[SupportedOSPlatform("windows")]
	private static string? ImagePath(int pid)
	{
		var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
		if (handle == IntPtr.Zero)
		{
			return null;
		}

		try
		{
			var buffer = new char[1024];
			var size = (uint)buffer.Length;
			return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
		}
		finally
		{
			CloseHandle(handle);
		}
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr OpenProcess(
		uint desiredAccess,
		[MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
		uint processId);

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint flags, char[] buffer, ref uint size);

	[DllImport("kernel32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CloseHandle(IntPtr handle);
}
