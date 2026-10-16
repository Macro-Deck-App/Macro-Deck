using System.Globalization;

namespace MacroDeckHost.Integrations.Steam.Native;

internal static class SteamRunningApp
{
	public static int? Read(ISteamPlatform platform, string? steamRoot)
	{
		ArgumentNullException.ThrowIfNull(platform);

		if (!platform.IsSteamProcessRunning())
		{
			return null;
		}

		var recorded = platform.Os == SteamOs.Windows
			? platform.ReadRegistryRunningAppId()
			: ReadRegistryFile(platform, steamRoot);

		if (recorded is > 0)
		{
			return recorded;
		}

		return platform.Os == SteamOs.Linux && platform.FindLaunchedAppIds() is [var appId, ..] ? appId : null;
	}

	internal static int? ReadRegistryFile(ISteamPlatform platform, string? steamRoot)
	{
		foreach (var path in RegistryFiles(platform, steamRoot))
		{
			if (SteamLibrary.ReadFile(path) is not { } text
				|| ValveKeyValues.TryParse(text)
					?.Find("Registry", "HKCU", "Software", "Valve", "Steam", "RunningAppID")
					?.Value is not { } value)
			{
				continue;
			}

			return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) ? appId : null;
		}

		return null;
	}

	internal static IEnumerable<string> RegistryFiles(ISteamPlatform platform, string? steamRoot)
	{
		if (platform.Os == SteamOs.MacOs)
		{
			yield return Path.Combine(platform.HomeDirectory,
				"Library",
				"Application Support",
				"Steam",
				"registry.vdf");
			yield break;
		}

		yield return Path.Combine(platform.HomeDirectory, ".steam", "registry.vdf");

		if (steamRoot is not null)
		{
			yield return Path.Combine(steamRoot, "registry.vdf");
		}

		yield return Path.Combine(platform.HomeDirectory,
			".var",
			"app",
			"com.valvesoftware.Steam",
			".steam",
			"registry.vdf");
	}

	internal static int? TryReadReaperAppId(string commandLine)
	{
		if (string.IsNullOrEmpty(commandLine))
		{
			return null;
		}

		var arguments = commandLine.Split('\0');
		if (!arguments.Any(argument
			=> argument.EndsWith("/reaper", StringComparison.Ordinal)
			|| argument.EndsWith("/SteamLaunch", StringComparison.Ordinal)
			|| argument is "reaper" or "SteamLaunch"))
		{
			return null;
		}

		const string marker = "AppId=";
		foreach (var argument in arguments)
		{
			if (argument.StartsWith(marker, StringComparison.Ordinal)
				&& int.TryParse(argument.AsSpan(marker.Length),
					NumberStyles.None,
					CultureInfo.InvariantCulture,
					out var appId)
				&& appId > 0)
			{
				return appId;
			}
		}

		return null;
	}
}
