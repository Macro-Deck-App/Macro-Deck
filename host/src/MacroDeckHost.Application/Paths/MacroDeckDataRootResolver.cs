using MacroDeckHost.Application.Configuration;

namespace MacroDeckHost.Application.Paths;

public static class MacroDeckDataRootResolver
{
	public const string DataDirectoryEnvironmentVariable = "MACRO_DECK_DATA_DIRECTORY";
	public const string DataDirEnvironmentVariable = "MACRODECK_DATA_DIR";
	public const string PortableEnvironmentVariable = "MACRODECK_PORTABLE";
	public const string PackagedMarkerFileName = ".macro-deck-packaged";
	public const string PortableMarkerFileName = "portable";
	public const string PortableDataDirectoryName = "Data";
	public const string ShellExecutableEnvironmentVariable = "MACRODECK_SHELL_EXECUTABLE";

	public static string Resolve(DataRootEnvironment environment)
	{
		var dataDirOverride = environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
		if (string.IsNullOrWhiteSpace(dataDirOverride))
		{
			dataDirOverride = environment.GetEnvironmentVariable(DataDirEnvironmentVariable);
		}

		if (!string.IsNullOrWhiteSpace(dataDirOverride))
		{
			return Path.GetFullPath(dataDirOverride);
		}

		if (environment.GetEnvironmentVariable(PortableEnvironmentVariable) == "1")
		{
			return environment.BaseDirectory;
		}

		if (BuildConfig.Channel == BuildChannel.Development)
		{
			return Path.Combine(environment.WorkingDirectory, BuildConfig.DataRootDirectoryName);
		}

		var portableDataRoot = ResolvePortableMarkerDataRoot(environment);
		if (portableDataRoot is not null)
		{
			return portableDataRoot;
		}

		var markerPath = Path.Combine(environment.BaseDirectory, PackagedMarkerFileName);
		if (!environment.FileExists(markerPath))
		{
			return environment.BaseDirectory;
		}

		return Path.Combine(GetPlatformDataRoot(environment), BuildConfig.DataRootDirectoryName);
	}

	/// <summary>
	/// True when the data root travels with the installation rather than belonging to one user account.
	/// Anything bound to a per-user facility - the OS keystore, above all - has to stay out of such an
	/// installation, or moving the folder to another machine or user stops working.
	/// </summary>
	public static bool IsPortable(DataRootEnvironment environment)
	{
		ArgumentNullException.ThrowIfNull(environment);

		var dataDirOverride = environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
		if (string.IsNullOrWhiteSpace(dataDirOverride))
		{
			dataDirOverride = environment.GetEnvironmentVariable(DataDirEnvironmentVariable);
		}

		if (!string.IsNullOrWhiteSpace(dataDirOverride))
		{
			return false;
		}

		if (environment.GetEnvironmentVariable(PortableEnvironmentVariable) == "1")
		{
			return true;
		}

		if (BuildConfig.Channel == BuildChannel.Development)
		{
			return false;
		}

		// An unmarked Production publish is a copied build folder, which ADR 0002 deliberately treats as
		// portable.
		return ResolvePortableMarkerDataRoot(environment) is not null
			|| !environment.FileExists(Path.Combine(environment.BaseDirectory, PackagedMarkerFileName));
	}

	// The marker sits beside the desktop app, not the host, so both processes agree on one folder.
	// The host lives in a host folder below the app, which is the fallback when no shell started it.
	private static string? ResolvePortableMarkerDataRoot(DataRootEnvironment environment)
	{
		var shellExecutable = environment.GetEnvironmentVariable(ShellExecutableEnvironmentVariable);
		var shellDirectory = string.IsNullOrWhiteSpace(shellExecutable)
			? Path.GetFullPath(Path.Combine(environment.BaseDirectory, ".."))
			: Path.GetDirectoryName(Path.GetFullPath(shellExecutable));
		if (shellDirectory is null
			|| !environment.FileExists(Path.Combine(shellDirectory, PortableMarkerFileName)))
		{
			return null;
		}

		return Path.Combine(shellDirectory, PortableDataDirectoryName);
	}

	private static string GetPlatformDataRoot(DataRootEnvironment environment)
	{
		switch (environment.Platform)
		{
			case DataRootPlatform.Windows:
				var appData = environment.GetEnvironmentVariable("APPDATA");
				return string.IsNullOrWhiteSpace(appData)
					? Path.Combine(environment.HomeDirectory, "AppData", "Roaming")
					: appData;
			case DataRootPlatform.MacOs:
				return Path.Combine(environment.HomeDirectory, "Library", "Application Support");
			case DataRootPlatform.Linux:
			default:
				var xdgDataHome = environment.GetEnvironmentVariable("XDG_DATA_HOME");
				return string.IsNullOrWhiteSpace(xdgDataHome)
					? Path.Combine(environment.HomeDirectory, ".local", "share")
					: xdgDataHome;
		}
	}
}
