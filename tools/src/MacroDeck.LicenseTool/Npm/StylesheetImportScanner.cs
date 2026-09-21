namespace MacroDeck.LicenseTool.Npm;

internal static class StylesheetImportScanner
{
	private static readonly string[] LoadRules = ["@use", "@forward", "@import"];

	public static IReadOnlyList<string> Scan(string source, bool lineComments)
	{
		var targets = new List<string>();
		var index = 0;
		string? pendingRule = null;
		while (index < source.Length)
		{
			var character = source[index];
			if (character == '/' && Peek(source, index + 1) == '*')
			{
				var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
				index = end < 0 ? source.Length : end + 2;
			}
			else if (lineComments && character == '/' && Peek(source, index + 1) == '/')
			{
				while (index < source.Length && source[index] != '\n')
				{
					index++;
				}
			}
			else if (character is '\'' or '"')
			{
				var value = ReadString(source, ref index, character);
				if (pendingRule is not null)
				{
					targets.Add(value);
					pendingRule = pendingRule == "@import" ? pendingRule : null;
				}
			}
			else if (character == '@')
			{
				var start = index;
				index++;
				while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] == '-'))
				{
					index++;
				}

				var rule = source[start..index];
				pendingRule = LoadRules.Contains(rule, StringComparer.Ordinal) ? rule : null;
			}
			else if (character is ';' or '{' or '}')
			{
				pendingRule = null;
				index++;
			}
			else if (pendingRule is not null && character != ',' && !char.IsWhiteSpace(character))
			{
				if (pendingRule == "@import")
				{
					pendingRule = null;
				}

				index++;
			}
			else
			{
				index++;
			}
		}

		return targets;
	}

	private static string ReadString(string source, ref int index, char quote)
	{
		var start = ++index;
		while (index < source.Length && source[index] != quote && source[index] != '\n')
		{
			index += source[index] == '\\' ? 2 : 1;
		}

		var value = source[start..Math.Min(index, source.Length)];
		index++;
		return value;
	}

	private static char Peek(string source, int index) => index < source.Length ? source[index] : '\0';
}
