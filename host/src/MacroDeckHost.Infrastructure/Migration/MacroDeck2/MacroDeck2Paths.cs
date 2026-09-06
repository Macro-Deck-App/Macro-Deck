namespace MacroDeckHost.Infrastructure.Migration.MacroDeck2;

/// <summary>The layout of a Macro Deck 2 user directory, as its <c>ApplicationPaths</c> defines it.</summary>
internal sealed class MacroDeck2Paths
{
	public MacroDeck2Paths(string root)
	{
		Root = root;
	}

	public string Root { get; }

	public string ConfigFile => Path.Combine(Root, "config.json");

	public string DevicesFile => Path.Combine(Root, "devices.json");

	public string ProfilesDirectory => Path.Combine(Root, "profiles");

	/// <summary>
	/// The pre-2.9 database. Macro Deck 2 renames it to <c>profiles.db.migrated</c> once it has written the
	/// JSON files, so an unsuffixed file here means the JSON directory was never produced.
	/// </summary>
	public string LegacyProfilesDatabase => Path.Combine(Root, "profiles.db");

	public string VariablesDatabase => Path.Combine(Root, "variables.db");

	public string IconPacksDirectory => Path.Combine(Root, "iconpacks");

	public string PluginConfigDirectory => Path.Combine(Root, "configs");

	public string PluginCredentialsDirectory => Path.Combine(Root, "credentials");

	public string PluginsDirectory => Path.Combine(Root, "plugins");

	/// <summary>
	/// Whether this looks like a Macro Deck 2 directory at all. Deliberately lenient: an installation that
	/// never had a plugin, an icon pack or a variable is still worth migrating, so only the profiles carry
	/// the decision.
	/// </summary>
	public bool LooksLikeMacroDeck2()
		=> Directory.Exists(ProfilesDirectory) || File.Exists(LegacyProfilesDatabase) || File.Exists(ConfigFile);

	public static string? TryDetectDefaultPath()
	{
		if (!OperatingSystem.IsWindows())
		{
			return null;
		}

		var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		if (string.IsNullOrEmpty(appData))
		{
			return null;
		}

		// A directory that merely exists is not a detection: an uninstall leaves the folder behind, and the
		// desktop UI only offers to migrate when something was genuinely found.
		var candidate = Path.Combine(appData, "Macro Deck");
		return new MacroDeck2Paths(candidate).LooksLikeMacroDeck2() ? candidate : null;
	}
}
