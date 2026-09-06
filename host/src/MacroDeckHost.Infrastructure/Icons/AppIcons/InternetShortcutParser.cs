namespace MacroDeckHost.Infrastructure.Icons.AppIcons;

internal static class InternetShortcutParser
{
	private const string SectionName = "InternetShortcut";
	private const string IconFileKey = "IconFile";

	public static string? TryReadIconFile(IEnumerable<string> lines)
	{
		var inSection = false;
		foreach (var rawLine in lines)
		{
			var line = rawLine.Trim();
			if (line.Length == 0 || line[0] == ';')
			{
				continue;
			}

			if (line[0] == '[')
			{
				inSection = IsInternetShortcutSection(line);
				continue;
			}

			if (!inSection)
			{
				continue;
			}

			var separator = line.IndexOf('=');
			if (separator <= 0)
			{
				continue;
			}

			var key = line[..separator].Trim();
			if (!key.Equals(IconFileKey, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var value = Environment.ExpandEnvironmentVariables(line[(separator + 1)..].Trim()).Trim();
			if (value.Length > 0)
			{
				return value;
			}
		}

		return null;
	}

	private static bool IsInternetShortcutSection(string line)
	{
		var end = line.IndexOf(']');
		var name = end > 1 ? line[1..end] : line[1..];
		return name.Trim().Equals(SectionName, StringComparison.OrdinalIgnoreCase);
	}
}
