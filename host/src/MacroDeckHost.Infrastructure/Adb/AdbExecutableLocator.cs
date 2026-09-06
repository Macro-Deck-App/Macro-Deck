using MacroDeckHost.Application.Adb;

namespace MacroDeckHost.Infrastructure.Adb;

internal static class AdbExecutableLocator
{
	public static (string? Path, AdbExecutableSource Source) Resolve(
		string? configuredPath,
		Func<string, bool> fileExists,
		Func<string, string?> readEnvironmentVariable,
		bool isWindows,
		bool isMacOs,
		bool isLinux)
	{
		if (!string.IsNullOrWhiteSpace(configuredPath) && fileExists(configuredPath))
		{
			return (configuredPath, AdbExecutableSource.Configured);
		}

		var executableName = isWindows ? "adb.exe" : "adb";

		foreach (var variable in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
		{
			var sdkRoot = readEnvironmentVariable(variable);
			if (string.IsNullOrWhiteSpace(sdkRoot))
			{
				continue;
			}

			var candidate = Path.Combine(sdkRoot, "platform-tools", executableName);
			if (fileExists(candidate))
			{
				return (candidate, AdbExecutableSource.AndroidSdkEnvironment);
			}
		}

		var wellKnownCandidates =
			WellKnownCandidates(readEnvironmentVariable, isWindows, isMacOs, isLinux, executableName);
		foreach (var candidate in wellKnownCandidates)
		{
			if (fileExists(candidate))
			{
				return (candidate, AdbExecutableSource.WellKnownSdkLocation);
			}
		}

		var path = readEnvironmentVariable("PATH");
		if (!string.IsNullOrEmpty(path))
		{
			foreach (var directory in path.Split(Path.PathSeparator))
			{
				if (string.IsNullOrWhiteSpace(directory))
				{
					continue;
				}

				var candidate = Path.Combine(directory, executableName);
				if (fileExists(candidate))
				{
					return (candidate, AdbExecutableSource.Path);
				}
			}
		}

		return (null, AdbExecutableSource.None);
	}

	private static IEnumerable<string> WellKnownCandidates(
		Func<string, string?> readEnvironmentVariable,
		bool isWindows,
		bool isMacOs,
		bool isLinux,
		string executableName)
	{
		if (isWindows)
		{
			var localAppData = readEnvironmentVariable("LOCALAPPDATA");
			if (!string.IsNullOrWhiteSpace(localAppData))
			{
				yield return Path.Combine(localAppData, "Android", "Sdk", "platform-tools", executableName);
			}

			var programFiles = readEnvironmentVariable("PROGRAMFILES");
			if (!string.IsNullOrWhiteSpace(programFiles))
			{
				yield return Path.Combine(programFiles, "Android", "platform-tools", executableName);
			}

			yield break;
		}

		if (isMacOs)
		{
			var home = readEnvironmentVariable("HOME");
			if (!string.IsNullOrWhiteSpace(home))
			{
				yield return Path.Combine(home, "Library", "Android", "sdk", "platform-tools", executableName);
			}

			yield return Path.Combine("/opt", "homebrew", "bin", executableName);
			yield return Path.Combine("/usr", "local", "bin", executableName);

			yield break;
		}

		if (isLinux)
		{
			var home = readEnvironmentVariable("HOME");
			if (!string.IsNullOrWhiteSpace(home))
			{
				yield return Path.Combine(home, "Android", "Sdk", "platform-tools", executableName);
			}

			yield return Path.Combine("/usr", "lib", "android-sdk", "platform-tools", executableName);
			yield return Path.Combine("/usr", "local", "bin", executableName);
			yield return Path.Combine("/usr", "bin", executableName);
		}
	}
}
