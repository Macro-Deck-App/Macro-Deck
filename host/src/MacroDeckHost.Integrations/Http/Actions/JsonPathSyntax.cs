using System.Globalization;

namespace MacroDeckHost.Integrations.Http.Actions;

internal readonly record struct JsonPathSegment(string? Property, int Index)
{
	public bool IsIndex => Property is null;
}

internal static class JsonPathSyntax
{
	public static bool TryParse(string path, out IReadOnlyList<JsonPathSegment> segments)
	{
		segments = [];

		var trimmed = path.Trim();
		if (trimmed.Length == 0)
		{
			return false;
		}

		if (trimmed[0] == '$')
		{
			trimmed = trimmed[1..];
			if (trimmed.StartsWith('.'))
			{
				trimmed = trimmed[1..];
			}
		}

		if (trimmed.Length == 0)
		{
			return true;
		}

		var parsed = new List<JsonPathSegment>();
		foreach (var segment in trimmed.Split('.'))
		{
			if (!TryParseSegment(segment, parsed))
			{
				return false;
			}
		}

		segments = parsed;
		return true;
	}

	private static bool TryParseSegment(string segment, List<JsonPathSegment> parsed)
	{
		var bracketIndex = segment.IndexOf('[', StringComparison.Ordinal);
		var name = bracketIndex < 0 ? segment : segment[..bracketIndex];
		var indices = bracketIndex < 0 ? string.Empty : segment[bracketIndex..];

		if (name.Length > 0)
		{
			parsed.Add(new JsonPathSegment(name, 0));
		}
		else if (indices.Length == 0)
		{
			return false;
		}

		var position = 0;
		while (position < indices.Length)
		{
			if (indices[position] != '[')
			{
				return false;
			}

			var close = indices.IndexOf(']', position);
			if (close < 0)
			{
				return false;
			}

			var digits = indices[(position + 1)..close];
			if (digits.Length == 0
				|| !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
			{
				return false;
			}

			parsed.Add(new JsonPathSegment(null, index));
			position = close + 1;
		}

		return true;
	}
}
