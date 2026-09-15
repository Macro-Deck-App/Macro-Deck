using System.ComponentModel;
using System.Diagnostics;
using MacroDeckHost.Application.Plugins.Runtime;
using Serilog;

namespace MacroDeckHost.Infrastructure.Plugins;

public sealed class DotnetMuxerLocator : IDotnetMuxerLocator
{
	public const string BundledRuntimeDirectoryName = "runtime";

	private static readonly TimeSpan _probeTimeout = TimeSpan.FromSeconds(5);

	private readonly ILogger _logger;
	private readonly string _bundledRoot;
	private readonly Func<IEnumerable<string>> _systemCandidatePaths;
	private readonly Func<string, IReadOnlyDictionary<string, IReadOnlyList<Version>>?> _probe;
	private readonly object _cacheLock = new();
	private DotnetMuxer? _bundled;
	private bool _bundledResolved;
	private DotnetMuxer? _system;
	private bool _systemResolved;

	public DotnetMuxerLocator(ILogger logger)
		: this(logger,
			Path.Combine(AppContext.BaseDirectory, BundledRuntimeDirectoryName),
			SystemCandidatePaths,
			null)
	{
	}

	internal DotnetMuxerLocator(ILogger logger,
		string bundledRoot,
		Func<IEnumerable<string>> systemCandidatePaths,
		Func<string, IReadOnlyDictionary<string, IReadOnlyList<Version>>?>? probe)
	{
		_logger = logger.ForContext<DotnetMuxerLocator>();
		_bundledRoot = bundledRoot;
		_systemCandidatePaths = systemCandidatePaths;
		_probe = probe ?? ProbeInstalledFrameworks;
	}

	private static string ExecutableName => OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

	public DotnetMuxerSelection? Locate(IReadOnlyList<DotnetFrameworkRequirement> requirements)
	{
		lock (_cacheLock)
		{
			var bundled = Bundled();
			if (bundled?.InstalledFrameworks is { } bundledFrameworks && FirstUnmet(requirements, bundledFrameworks) is null)
			{
				return new DotnetMuxerSelection { Muxer = bundled };
			}

			var system = System();
			if (system is not null &&
				(system.InstalledFrameworks is null || FirstUnmet(requirements, system.InstalledFrameworks) is null))
			{
				return new DotnetMuxerSelection { Muxer = system };
			}

			var fallback = bundled ?? system;
			return fallback is null
				? null
				: new DotnetMuxerSelection
				{
					Muxer = fallback,
					UnmetRequirement = FirstUnmet(requirements, fallback.InstalledFrameworks!)
				};
		}
	}

	public void Invalidate()
	{
		lock (_cacheLock)
		{
			_bundled = null;
			_bundledResolved = false;
			_system = null;
			_systemResolved = false;
		}
	}

	private DotnetMuxer? Bundled()
	{
		if (!_bundledResolved)
		{
			var executablePath = Path.Combine(_bundledRoot, ExecutableName);
			_bundled = File.Exists(executablePath)
				? new DotnetMuxer
				{
					ExecutablePath = executablePath,
					InstalledFrameworks = ReadBundledFrameworks(_bundledRoot)
				}
				: null;
			_bundledResolved = true;
		}

		return _bundled;
	}

	private DotnetMuxer? System()
	{
		if (!_systemResolved)
		{
			var bundledPath = Path.GetFullPath(Path.Combine(_bundledRoot, ExecutableName));
			var executablePath = _systemCandidatePaths()
				.Where(File.Exists)
				.FirstOrDefault(candidate => !string.Equals(Path.GetFullPath(candidate),
					bundledPath,
					StringComparison.OrdinalIgnoreCase));

			_system = executablePath is null
				? null
				: new DotnetMuxer { ExecutablePath = executablePath, InstalledFrameworks = _probe(executablePath) };
			_systemResolved = true;
		}

		return _system;
	}

	private static DotnetFrameworkRequirement? FirstUnmet(IReadOnlyList<DotnetFrameworkRequirement> requirements,
		IReadOnlyDictionary<string, IReadOnlyList<Version>> installed)
	{
		return requirements.FirstOrDefault(requirement =>
			!requirement.IsSatisfiedBy(installed.GetValueOrDefault(requirement.Name) ?? []));
	}

	private static Dictionary<string, IReadOnlyList<Version>> ReadBundledFrameworks(string root)
	{
		var frameworks = new Dictionary<string, IReadOnlyList<Version>>(StringComparer.Ordinal);
		var shared = Path.Combine(root, "shared");
		if (!Directory.Exists(shared))
		{
			return frameworks;
		}

		foreach (var frameworkDirectory in Directory.EnumerateDirectories(shared))
		{
			var versions = Directory.EnumerateDirectories(frameworkDirectory)
				.Select(directory => ParseVersion(Path.GetFileName(directory)))
				.OfType<Version>()
				.Distinct()
				.Order()
				.ToList();

			if (versions.Count > 0)
			{
				frameworks[Path.GetFileName(frameworkDirectory)] = versions;
			}
		}

		return frameworks;
	}

	private static IEnumerable<string> SystemCandidatePaths()
	{
		foreach (var rootVariable in RootEnvironmentVariables())
		{
			var root = Environment.GetEnvironmentVariable(rootVariable);
			if (!string.IsNullOrWhiteSpace(root))
			{
				yield return Path.Combine(root, ExecutableName);
			}
		}

		var path = Environment.GetEnvironmentVariable("PATH");
		if (!string.IsNullOrEmpty(path))
		{
			foreach (var directory in path.Split(Path.PathSeparator))
			{
				if (!string.IsNullOrWhiteSpace(directory))
				{
					yield return Path.Combine(directory, ExecutableName);
				}
			}
		}

		foreach (var candidate in WellKnownLocations(ExecutableName))
		{
			yield return candidate;
		}
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

	private IReadOnlyDictionary<string, IReadOnlyList<Version>>? ProbeInstalledFrameworks(string executablePath)
	{
		try
		{
			var output = ProbeListRuntimesAsync(executablePath).GetAwaiter().GetResult();
			if (output is null)
			{
				return null;
			}

			var frameworks = ParseInstalledFrameworks(output);
			return frameworks.Count == 0 ? null : frameworks;
		}
		catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException)
		{
			PluginInfrastructureLog.DotnetMuxerProbeFailed(_logger, executablePath, ex);
			return null;
		}
	}

	private async Task<string?> ProbeListRuntimesAsync(string executablePath)
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
			return null;
		}

		var output = await stdOutTask;
		await stdErrTask;
		return output;
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

	public static IReadOnlyDictionary<string, IReadOnlyList<Version>> ParseInstalledFrameworks(
		string listRuntimesOutput)
	{
		var frameworks = new Dictionary<string, SortedSet<Version>>(StringComparer.Ordinal);

		if (string.IsNullOrWhiteSpace(listRuntimesOutput))
		{
			return new Dictionary<string, IReadOnlyList<Version>>(StringComparer.Ordinal);
		}

		foreach (var rawLine in listRuntimesOutput.Split('\n'))
		{
			var parts = rawLine.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length < 2 || ParseVersion(parts[1]) is not { } version)
			{
				continue;
			}

			if (!frameworks.TryGetValue(parts[0], out var versions))
			{
				versions = [];
				frameworks[parts[0]] = versions;
			}

			versions.Add(version);
		}

		return frameworks.ToDictionary(pair => pair.Key,
			pair => (IReadOnlyList<Version>)pair.Value.ToList(),
			StringComparer.Ordinal);
	}

	internal static Version? ParseVersion(string token)
	{
		var dashIndex = token.IndexOf('-', StringComparison.Ordinal);
		if (dashIndex >= 0)
		{
			token = token[..dashIndex];
		}

		return Version.TryParse(token, out var version) ? version : null;
	}
}
