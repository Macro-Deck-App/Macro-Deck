using MacroDeckHost.Application.Configuration;

namespace MacroDeckHost.Application.Paths;

public static class MacroDeckDataRootResolver
{
	public const string DataDirectoryEnvironmentVariable = "MACRO_DECK_DATA_DIRECTORY";
	public const string DataDirEnvironmentVariable = "MACRODECK_DATA_DIR";
	public const string PortableEnvironmentVariable = "MACRODECK_PORTABLE";
	public const string PackagedMarkerFileName = ".macro-deck-packaged";

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

		// An unmarked Production publish is a copied build folder, which ADR 0002 deliberately treats as
		// portable. A Development build roots its data under the working directory instead.
		return BuildConfig.Channel != BuildChannel.Development &&
			!environment.FileExists(Path.Combine(environment.BaseDirectory, PackagedMarkerFileName));
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
