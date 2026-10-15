using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace MacroDeckHost.Integrations.Steam.Native;

internal enum SteamOs
{
	Windows,

	MacOs,

	Linux
}

internal interface ISteamPlatform
{
	SteamOs Os { get; }

	string HomeDirectory { get; }

	IReadOnlyList<string> ProgramFilesDirectories { get; }

	string? ReadRegistrySteamPath();

	int? ReadRegistryRunningAppId();

	bool IsSteamProcessRunning();

	IReadOnlyList<int> FindLaunchedAppIds();

	bool CanCloseGames { get; }

	IReadOnlyList<SteamGameProcess> FindGameProcesses(int appId, string? installDirectory);

	bool RequestClose(SteamGameProcess process);

	bool Kill(SteamGameProcess process);

	bool IsRunning(SteamGameProcess process);
}

internal sealed class SystemSteamPlatform : ISteamPlatform
{
	private const string SteamKey = @"Software\Valve\Steam";

	private const int MaximumProcessEntries = 4096;

	private const long LaunchScanIntervalMilliseconds = 15_000;

	private static readonly string[] _processNames = ["steam", "steam_osx", "Steam"];

	private readonly Lock _scanSync = new();

	private long _lastScan = long.MinValue;
	private IReadOnlyList<int> _lastLaunched = [];

	public SteamOs Os
		=> OperatingSystem.IsWindows() ? SteamOs.Windows :
			OperatingSystem.IsMacOS() ? SteamOs.MacOs : SteamOs.Linux;

	public string HomeDirectory => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

	public IReadOnlyList<string> ProgramFilesDirectories
		=>
		[
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
		];

	public string? ReadRegistrySteamPath()
	{
		return OperatingSystem.IsWindows() ? ReadValue("SteamPath") : null;
	}

	public int? ReadRegistryRunningAppId()
	{
		return OperatingSystem.IsWindows()
			&& int.TryParse(ReadValue("RunningAppID"), NumberStyles.None, CultureInfo.InvariantCulture, out var appId)
				? appId
				: null;
	}

	public bool IsSteamProcessRunning()
	{
		try
		{
			var running = false;
			foreach (var name in _processNames)
			{
				var processes = Process.GetProcessesByName(name);
				running |= processes.Length > 0;
				foreach (var process in processes)
				{
					process.Dispose();
				}

				if (running)
				{
					break;
				}
			}

			return running;
		}
		catch (Exception exception) when (exception is InvalidOperationException
			or PlatformNotSupportedException
			or Win32Exception)
		{
			return false;
		}
	}

	public bool CanCloseGames => OperatingSystem.IsWindows();

	public IReadOnlyList<SteamGameProcess> FindGameProcesses(int appId, string? installDirectory)
	{
		return OperatingSystem.IsWindows() && installDirectory is not null
			? SteamGameProcesses.FindOnWindows(installDirectory)
			: [];
	}

	public bool RequestClose(SteamGameProcess process)
	{
		return OperatingSystem.IsWindows() && SteamGameProcesses.RequestClose(process);
	}

	public bool Kill(SteamGameProcess process)
	{
		return OperatingSystem.IsWindows() && SteamGameProcesses.Kill(process);
	}

	public bool IsRunning(SteamGameProcess process)
	{
		return SteamGameProcesses.IsRunning(process);
	}

	public IReadOnlyList<int> FindLaunchedAppIds()
	{
		if (!OperatingSystem.IsLinux() || !Directory.Exists("/proc"))
		{
			return [];
		}

		lock (_scanSync)
		{
			var now = Environment.TickCount64;
			if (_lastScan != long.MinValue && now - _lastScan < LaunchScanIntervalMilliseconds)
			{
				return _lastLaunched;
			}

			_lastLaunched = ScanLaunchedAppIds();
			_lastScan = now;
			return _lastLaunched;
		}
	}

	private static List<int> ScanLaunchedAppIds()
	{
		var found = new List<int>();
		try
		{
			var inspected = 0;
			foreach (var directory in Directory.EnumerateDirectories("/proc"))
			{
				if (++inspected > MaximumProcessEntries)
				{
					break;
				}

				if (!int.TryParse(Path.GetFileName(directory), NumberStyles.None, CultureInfo.InvariantCulture, out _))
				{
					continue;
				}

				try
				{
					var commandLine = File.ReadAllText(Path.Combine(directory, "cmdline"));
					if (SteamRunningApp.TryReadReaperAppId(commandLine) is { } appId)
					{
						found.Add(appId);
					}
				}
				catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
				{
				}
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return found;
		}

		return found;
	}

	[SupportedOSPlatform("windows")]
	private static string? ReadValue(string name)
	{
		try
		{
			using var key = Registry.CurrentUser.OpenSubKey(SteamKey);
			return key?.GetValue(name)?.ToString();
		}
		catch (Exception exception) when (exception is UnauthorizedAccessException
			or IOException
			or SecurityException)
		{
			return null;
		}
	}
}
