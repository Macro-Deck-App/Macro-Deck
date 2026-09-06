namespace MacroDeck.Plugin.Hosting.Credentials;

/// <summary>
/// Where a self-registering plugin keeps state it owns.
///
/// <para>
/// This mirrors the host's own platform switch rather than sharing it: the resolver lives in the
/// host's application layer, which the SDK cannot reference and must not start referencing. The two
/// are expected to agree in shape, not to be the same code.
/// </para>
/// </summary>
internal static class PluginStateDirectory
{
	/// <summary>The default root for plugin state, before the plugin id is appended.</summary>
	public static string Default()
	{
		if (OperatingSystem.IsWindows())
		{
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"MacroDeck",
				"plugins");
		}

		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		if (OperatingSystem.IsMacOS())
		{
			return Path.Combine(home, "Library", "Application Support", "MacroDeck", "plugins");
		}

		var stateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
		var root = string.IsNullOrEmpty(stateHome)
			? Path.Combine(home, ".local", "state")
			: stateHome;

		return Path.Combine(root, "macro-deck", "plugins");
	}
}
