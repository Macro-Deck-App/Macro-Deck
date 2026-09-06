using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using MacroDeckHost.Application.Plugins.Runtime;
using Serilog;

namespace MacroDeckHost.Infrastructure.Plugins;

public sealed class DotnetMuxerLocator : IDotnetMuxerLocator
{
	private static readonly TimeSpan _probeTimeout = TimeSpan.FromSeconds(5);

	private readonly ILogger _logger;
	private readonly object _cacheLock = new();
	private DotnetMuxer? _cached;
	private bool _resolved;

	public DotnetMuxerLocator(ILogger logger)
	{
		_logger = logger.ForContext<DotnetMuxerLocator>();
	}

	public DotnetMuxer? Locate()
	{
		lock (_cacheLock)
		{
			if (_resolved)
			{
				return _cached;
			}

			var executablePath = ResolveExecutablePath();
			_cached = executablePath is null
				? null
				: new DotnetMuxer
				{
					ExecutablePath = executablePath,
					InstalledRuntimeVersions = ProbeInstalledRuntimes(executablePath)
				};
			_resolved = true;
			return _cached;
		}
	}

	public void Invalidate()
	{
		lock (_cacheLock)
		{
			_cached = null;
			_resolved = false;
		}
	}

	private static string? ResolveExecutablePath()
	{
		var executableName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

		foreach (var rootVariable in RootEnvironmentVariables())
		{
			var root = Environment.GetEnvironmentVariable(rootVariable);
			if (string.IsNullOrWhiteSpace(root))
			{
				continue;
			}

			var candidate = Path.Combine(root, executableName);
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}

		var path = Environment.GetEnvironmentVariable("PATH");
		if (!string.IsNullOrEmpty(path))
		{
			foreach (var directory in path.Split(Path.PathSeparator))
			{
				if (string.IsNullOrWhiteSpace(directory))
				{
					continue;
				}

				var candidate = Path.Combine(directory, executableName);
				if (File.Exists(candidate))
				{
					return candidate;
				}
			}
		}

		foreach (var candidate in WellKnownLocations(executableName))
		{
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}

		return null;
	}

	private static IEnumerable<string> RootEnvironmentVariables()
	{
		yield return "DOTNET_ROOT";

		if (OperatingSystem.IsWindows())
		{
			yield return "DOTNET_ROOT(x86)";
		}
	}

	private static IEnumerable<string> WellKnownLocations(string executableName)
	{
		if (OperatingSystem.IsWindows())
		{
			var programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
			if (!string.IsNullOrWhiteSpace(programFiles))
			{
				yield return Path.Combine(programFiles, "dotnet", executableName);
			}

			var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
			if (!string.IsNullOrWhiteSpace(programFilesX86))
			{
				yield return Path.Combine(programFilesX86, "dotnet", executableName);
			}
		}
		else if (OperatingSystem.IsMacOS())
		{
			yield return Path.Combine("/usr", "local", "share", "dotnet", executableName);
			yield return Path.Combine("/opt", "homebrew", "opt", "dotnet", "libexec", executableName);
		}
		else if (OperatingSystem.IsLinux())
		{
			yield return Path.Combine("/usr", "share", "dotnet", executableName);
			yield return Path.Combine("/usr", "lib", "dotnet", executableName);
			yield return Path.Combine("/opt", "dotnet", executableName);
		}

		var home = Environment.GetEnvironmentVariable(OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME");
		if (!string.IsNullOrWhiteSpace(home))
		{
			yield return Path.Combine(home, ".dotnet", executableName);
		}
	}

	private IReadOnlyList<Version> ProbeInstalledRuntimes(string executablePath)
	{
		try
		{
			return ProbeInstalledRuntimesAsync(executablePath).GetAwaiter().GetResult();
		}
		catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException)
		{
			PluginInfrastructureLog.DotnetMuxerProbeFailed(_logger, executablePath, ex);
			return [];
		}
	}

	private async Task<IReadOnlyList<Version>> ProbeInstalledRuntimesAsync(string executablePath)
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = executablePath,
			Arguments = "--list-runtimes",
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};

		using var process = new Process { StartInfo = startInfo };
		process.Start();

		var stdOutTask = process.StandardOutput.ReadToEndAsync();
		var stdErrTask = process.StandardError.ReadToEndAsync();

		using var cts = new CancellationTokenSource(_probeTimeout);
		try
		{
			await process.WaitForExitAsync(cts.Token);
		}
		catch (OperationCanceledException)
		{
			TryKill(process);
			PluginInfrastructureLog.DotnetMuxerProbeTimedOut(_logger, executablePath);
			return [];
		}

		var output = await stdOutTask;
		await stdErrTask;
		return ParseInstalledRuntimes(output);
	}

	private static void TryKill(Process process)
	{
		try
		{
			if (!process.HasExited)
			{
				process.Kill(entireProcessTree: true);
			}
		}
		catch (InvalidOperationException)
		{
		}
		catch (Win32Exception)
		{
		}
	}

	public static IReadOnlyList<Version> ParseInstalledRuntimes(string listRuntimesOutput)
	{
		if (string.IsNullOrWhiteSpace(listRuntimesOutput))
		{
			return [];
		}

		const string prefix = "Microsoft.NETCore.App ";
		var versions = new SortedSet<Version>();

		foreach (var rawLine in listRuntimesOutput.Split('\n'))
		{
			var line = rawLine.Trim();
			if (!line.StartsWith(prefix, StringComparison.Ordinal))
			{
				continue;
			}

			var remainder = line[prefix.Length..].Trim();
			var spaceIndex = remainder.IndexOf(' ');
			var versionToken = spaceIndex < 0 ? remainder : remainder[..spaceIndex];

			var dashIndex = versionToken.IndexOf('-', StringComparison.Ordinal);
			if (dashIndex >= 0)
			{
				versionToken = versionToken[..dashIndex];
			}

			if (Version.TryParse(versionToken, out var version))
			{
				versions.Add(version);
			}
		}

		return versions.ToList();
	}

	public static bool IsRuntimeSatisfied(string requiredMajorMinor, IReadOnlyList<Version> installed)
	{
		var parts = requiredMajorMinor.Split('.');
		if (parts.Length != 2 ||
			!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var requiredMajor) ||
			!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var requiredMinor))
		{
			return false;
		}

		return installed.Any(version => version.Major == requiredMajor && version.Minor >= requiredMinor);
	}
}
