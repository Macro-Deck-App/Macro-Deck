using System.Text;
using System.Text.RegularExpressions;

namespace MacroDeck.LicenseTool.Model;

internal static class Glob
{
	public static IReadOnlyList<string> Expand(string baseDirectory, string pattern)
	{
		var normalized = pattern.Replace('\\', '/');
		var firstWildcard = normalized.IndexOfAny(['*', '?']);
		if (firstWildcard < 0)
		{
			var single = Path.Combine(baseDirectory, normalized);
			return File.Exists(single) ? [normalized] : [];
		}

		var fixedPart = normalized[..firstWildcard];
		var rootRelative = fixedPart.Contains('/', StringComparison.Ordinal) ? fixedPart[..fixedPart.LastIndexOf('/')] : "";
		var root = Path.Combine(baseDirectory, rootRelative);
		if (!Directory.Exists(root))
		{
			return [];
		}

		var regex = new Regex(ToRegex(normalized), RegexOptions.CultureInvariant);
		return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
			.Select(file => Path.GetRelativePath(baseDirectory, file).Replace('\\', '/'))
			.Where(relative => regex.IsMatch(relative))
			.Order(StringComparer.Ordinal)
			.ToList();
	}

	private static string ToRegex(string pattern)
	{
		var builder = new StringBuilder("^");
		for (var index = 0; index < pattern.Length; index++)
		{
			var character = pattern[index];
			if (character == '*' && index + 1 < pattern.Length && pattern[index + 1] == '*')
			{
				index++;
				if (index + 1 < pattern.Length && pattern[index + 1] == '/')
				{
					index++;
					builder.Append("(?:.*/)?");
				}
				else
				{
					builder.Append(".*");
				}
			}
			else if (character == '*')
			{
				builder.Append("[^/]*");
			}
			else if (character == '?')
			{
				builder.Append("[^/]");
			}
			else
			{
				builder.Append(Regex.Escape(character.ToString()));
			}
		}

		return builder.Append('$').ToString();
	}
}
