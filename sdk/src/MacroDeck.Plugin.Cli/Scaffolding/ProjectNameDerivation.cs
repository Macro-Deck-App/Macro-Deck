using System.Text;

namespace MacroDeck.Plugin.Cli.Scaffolding;

/// <summary>Derives a C# project name from a plugin's display name, and validates one whether derived or
/// typed directly with <c>--project-name</c>.</summary>
internal static class ProjectNameDerivation
{
	/// <summary>Splits on runs of non-alphanumerics, title-cases each token's first character, and joins -
	/// e.g. "Spotify Controller" -&gt; "SpotifyController", "OBS-Studio Bridge" -&gt; "OBSStudioBridge".
	/// Prefixes "Plugin" when the result would otherwise start with a digit, which is not a legal C#
	/// identifier start. Returns <see langword="null" /> when nothing alphanumeric survives at all.</summary>
	public static string? Derive(string name)
	{
		var tokens = SplitAlphanumeric(name);
		if (tokens.Count == 0)
		{
			return null;
		}

		var builder = new StringBuilder();
		foreach (var token in tokens)
		{
			builder.Append(char.ToUpperInvariant(token[0]));
			if (token.Length > 1)
			{
				builder.Append(token[1..]);
			}
		}

		return char.IsAsciiDigit(builder[0]) ? "Plugin" + builder : builder.ToString();
	}

	/// <summary>Accepts a dotted name (e.g. "Acme.LightControl") as well as a bare one - each dot-separated
	/// segment must be a legal C# identifier start followed by identifier characters, matching what
	/// <c>dotnet new -n</c> itself turns into a compiling project.</summary>
	public static bool Validate(string? projectName)
	{
		if (string.IsNullOrWhiteSpace(projectName))
		{
			return false;
		}

		var segments = projectName.Split('.');
		return segments.All(IsValidIdentifierSegment);
	}

	private static bool IsValidIdentifierSegment(string segment)
	{
		if (segment.Length == 0 || (!char.IsLetter(segment[0]) && segment[0] != '_'))
		{
			return false;
		}

		for (var i = 1; i < segment.Length; i++)
		{
			if (!char.IsLetterOrDigit(segment[i]) && segment[i] != '_')
			{
				return false;
			}
		}

		return true;
	}

	private static List<string> SplitAlphanumeric(string name)
	{
		var tokens = new List<string>();
		var current = new StringBuilder();

		foreach (var c in name)
		{
			if (char.IsLetterOrDigit(c))
			{
				current.Append(c);
				continue;
			}

			if (current.Length > 0)
			{
				tokens.Add(current.ToString());
				current.Clear();
			}
		}

		if (current.Length > 0)
		{
			tokens.Add(current.ToString());
		}

		return tokens;
	}
}
