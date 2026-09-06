using System.Text;

namespace MacroDeckHost.Integrations.System.DesktopEntries;

public sealed record DesktopEntryCommand(string Program, string? Arguments);

public static class DesktopEntryParser
{
	private const string DesktopEntryGroup = "[Desktop Entry]";

	private const string IconKey = "Icon";

	private const string ExecKey = "Exec";

	private const string NameKey = "Name";

	public static bool IsDesktopEntry(string path)
		=> Path.GetExtension(path).Equals(".desktop", StringComparison.OrdinalIgnoreCase);

	public static string? TryReadIconName(IEnumerable<string> lines)
		=> TryReadValue(lines, IconKey);

	public static string? TryReadName(IEnumerable<string> lines)
		=> TryReadValue(lines, NameKey);

	public static DesktopEntryCommand? TryReadCommand(IEnumerable<string> lines)
	{
		var exec = TryReadValue(lines, ExecKey);
		if (exec is null)
		{
			return null;
		}

		var tokens = SplitExec(exec);
		if (tokens.Count == 0)
		{
			return null;
		}

		var arguments = tokens.Count > 1
			? string.Join(' ', tokens.Skip(1).Select(Quote))
			: null;

		return new DesktopEntryCommand(tokens[0], arguments);
	}

	private static string? TryReadValue(IEnumerable<string> lines, string key)
	{
		var inDesktopEntry = false;
		foreach (var rawLine in lines)
		{
			var line = rawLine.Trim();
			if (line.Length == 0 || line[0] == '#')
			{
				continue;
			}

			if (line[0] == '[')
			{
				inDesktopEntry = string.Equals(line, DesktopEntryGroup, StringComparison.Ordinal);
				continue;
			}

			if (!inDesktopEntry)
			{
				continue;
			}

			var separator = line.IndexOf('=');
			if (separator <= 0)
			{
				continue;
			}

			if (!line.AsSpan(0, separator).TrimEnd().SequenceEqual(key))
			{
				continue;
			}

			var value = line.AsSpan(separator + 1).Trim();
			return value.IsEmpty ? null : value.ToString();
		}

		return null;
	}

	private static List<string> SplitExec(string exec)
	{
		var tokens = new List<string>();
		var token = new StringBuilder();
		var quoted = false;
		var hasToken = false;

		for (var i = 0; i < exec.Length; i++)
		{
			var c = exec[i];
			if (c == '\\' && quoted && i + 1 < exec.Length)
			{
				token.Append(exec[++i]);
				continue;
			}

			if (c == '"')
			{
				quoted = !quoted;
				hasToken = true;
				continue;
			}

			if (c == '%' && !quoted && i + 1 < exec.Length)
			{
				if (exec[++i] == '%')
				{
					token.Append('%');
					hasToken = true;
				}

				continue;
			}

			if (!quoted && char.IsWhiteSpace(c))
			{
				Flush(tokens, token, ref hasToken);
				continue;
			}

			token.Append(c);
			hasToken = true;
		}

		Flush(tokens, token, ref hasToken);
		return tokens;
	}

	private static void Flush(List<string> tokens, StringBuilder token, ref bool hasToken)
	{
		if (hasToken && token.Length > 0)
		{
			tokens.Add(token.ToString());
		}

		token.Clear();
		hasToken = false;
	}

	private static string Quote(string token)
		=> token.Contains(' ', StringComparison.Ordinal) ? $"\"{token}\"" : token;
}
