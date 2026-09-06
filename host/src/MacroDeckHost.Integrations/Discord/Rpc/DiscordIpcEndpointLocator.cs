namespace MacroDeckHost.Integrations.Discord.Rpc;

internal static class DiscordIpcEndpointLocator
{
	internal const int SocketCount = 10;

	private static readonly string[] _sandboxSubdirectories =
	[
		string.Empty,
		"snap.discord",
		"snap.discord-canary",
		"app/com.discordapp.Discord",
		"app/com.discordapp.DiscordCanary",
		"app/com.discordapp.DiscordPTB"
	];

	public static IReadOnlyList<string> WindowsPipeNames()
		=> [.. Enumerable.Range(0, SocketCount).Select(i => $"discord-ipc-{i}")];

	public static IReadOnlyList<string> UnixSocketDirectories()
	{
		var candidates = new[]
		{
			Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"),
			Environment.GetEnvironmentVariable("TMPDIR"),
			Environment.GetEnvironmentVariable("TMP"),
			Environment.GetEnvironmentVariable("TEMP"),
			"/tmp"
		};

		var seen = new HashSet<string>(StringComparer.Ordinal);
		var directories = new List<string>(candidates.Length);
		foreach (var candidate in candidates)
		{
			if (string.IsNullOrWhiteSpace(candidate))
			{
				continue;
			}

			var normalized = candidate.TrimEnd('/');
			if (normalized.Length > 0 && seen.Add(normalized))
			{
				directories.Add(normalized);
			}
		}

		return directories;
	}

	public static IReadOnlyList<string> UnixSocketPaths()
	{
		var directories = UnixSocketDirectories();
		var paths = new List<string>(directories.Count * _sandboxSubdirectories.Length * SocketCount);

		foreach (var directory in directories)
		{
			foreach (var subdirectory in _sandboxSubdirectories)
			{
				var root = subdirectory.Length == 0 ? directory : $"{directory}/{subdirectory}";
				for (var index = 0; index < SocketCount; index++)
				{
					paths.Add($"{root}/discord-ipc-{index}");
				}
			}
		}

		return paths;
	}
}
