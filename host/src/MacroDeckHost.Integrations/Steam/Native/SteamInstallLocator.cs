namespace MacroDeckHost.Integrations.Steam.Native;

internal static class SteamInstallLocator
{
	public static string? Find(ISteamPlatform platform)
	{
		ArgumentNullException.ThrowIfNull(platform);

		return Candidates(platform).FirstOrDefault(IsSteamRoot);
	}

	internal static IEnumerable<string> Candidates(ISteamPlatform platform)
	{
		var home = platform.HomeDirectory;

		switch (platform.Os)
		{
			case SteamOs.Windows:
				if (platform.ReadRegistrySteamPath() is { Length: > 0 } registered
					&& FullPathOrNull(registered) is { } full)
				{
					yield return full;
				}

				foreach (var folder in platform.ProgramFilesDirectories)
				{
					if (!string.IsNullOrEmpty(folder))
					{
						yield return Path.Combine(folder, "Steam");
					}
				}

				break;
			case SteamOs.MacOs:
				yield return Path.Combine(home, "Library", "Application Support", "Steam");
				break;
			default:
				yield return Path.Combine(home, ".steam", "steam");
				yield return Path.Combine(home, ".local", "share", "Steam");
				yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
				yield return Path.Combine(home, "snap", "steam", "common", ".local", "share", "Steam");
				break;
		}
	}

	private static string? FullPathOrNull(string path)
	{
		try
		{
			return Path.GetFullPath(path);
		}
		catch (Exception exception) when (exception is ArgumentException
			or NotSupportedException
			or PathTooLongException)
		{
			return null;
		}
	}

	internal static bool IsSteamRoot(string folder)
	{
		return Directory.Exists(Path.Combine(folder, "steamapps"));
	}
}
