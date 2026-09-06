using System.Text;

namespace MacroDeck.Plugin.Cli.Scaffolding;

/// <summary>Derives the wizard's suggested plugin id from the plugin name - a suggestion only, never
/// applied silently: <c>--id</c> stays a required value on the non-interactive path.</summary>
internal static class PluginIdDerivation
{
	public static string? Derive(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}

		var builder = new StringBuilder();
		var previousWasHyphen = true;

		foreach (var c in name)
		{
			if (char.IsAsciiLetterOrDigit(c))
			{
				builder.Append(char.ToLowerInvariant(c));
				previousWasHyphen = false;
			}
			else if (!previousWasHyphen)
			{
				builder.Append('-');
				previousWasHyphen = true;
			}
		}

		var kebab = builder.ToString().Trim('-');
		if (kebab.Length == 0)
		{
			return null;
		}

		// A reverse-domain id's last segment must itself start with a letter - see PluginId.IsValid - so a
		// name that kebab-cases to a leading digit (e.g. "3D Lights" -> "3d-lights") still derives a legal
		// suggestion.
		if (char.IsAsciiDigit(kebab[0]))
		{
			kebab = "app-" + kebab;
		}

		return $"com.example.{kebab}";
	}
}
