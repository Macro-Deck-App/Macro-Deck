using System.Globalization;

namespace MacroDeckHost.Integrations.Feeds.Protocol;

internal static class FeedDates
{
	private static readonly string[] _months =
		["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

	private static readonly Dictionary<string, int> _namedZones = new(StringComparer.OrdinalIgnoreCase)
	{
		["UT"] = 0,
		["UTC"] = 0,
		["GMT"] = 0,
		["Z"] = 0,
		["EST"] = -5,
		["EDT"] = -4,
		["CST"] = -6,
		["CDT"] = -5,
		["MST"] = -7,
		["MDT"] = -6,
		["PST"] = -8,
		["PDT"] = -7
	};

	public static DateTimeOffset? Parse(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}

		var trimmed = text.Trim();
		return ParseRfc822(trimmed) ?? ParseRfc3339(trimmed);
	}

	public static DateTimeOffset? ParseRfc3339(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}

		return DateTimeOffset.TryParse(text.Trim(),
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces,
			out var parsed)
			? parsed
			: null;
	}

	public static DateTimeOffset? ParseRfc822(string text)
	{
		var value = text;
		var comma = value.IndexOf(',', StringComparison.Ordinal);
		if (comma >= 0)
		{
			value = value[(comma + 1)..];
		}

		var parts = value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length < 4
			|| !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var day)
			|| Month(parts[1]) is not { } month
			|| !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
			|| !TryParseTime(parts[3], out var time))
		{
			return null;
		}

		if (parts[2].Length == 2)
		{
			year += year < 50 ? 2000 : 1900;
		}

		var offset = parts.Length > 4 ? Zone(parts[4]) : TimeSpan.Zero;
		if (offset is null)
		{
			return null;
		}

		try
		{
			return new DateTimeOffset(year, month, day, 0, 0, 0, offset.Value).Add(time);
		}
		catch (ArgumentOutOfRangeException)
		{
			return null;
		}
	}

	private static int? Month(string token)
	{
		if (token.Length < 3)
		{
			return null;
		}

		var index = Array.IndexOf(_months, token[..3].ToLowerInvariant());
		return index < 0 ? null : index + 1;
	}

	private static bool TryParseTime(string token, out TimeSpan time)
	{
		time = default;
		var pieces = token.Split(':');
		if (pieces.Length is < 2 or > 3)
		{
			return false;
		}

		var seconds = 0;
		if (!int.TryParse(pieces[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
			|| !int.TryParse(pieces[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
			|| pieces.Length == 3
			&& !int.TryParse(pieces[2], NumberStyles.None, CultureInfo.InvariantCulture, out seconds)
			|| hours > 23
			|| minutes > 59
			|| seconds > 60)
		{
			return false;
		}

		time = new TimeSpan(hours, minutes, Math.Min(seconds, 59));
		return true;
	}

	private static TimeSpan? Zone(string token)
	{
		if (token.Length == 5
			&& token[0] is '+' or '-'
			&& int.TryParse(token.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
			&& int.TryParse(token.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
		{
			var offset = new TimeSpan(hours, minutes, 0);
			return token[0] == '-' ? -offset : offset;
		}

		if (_namedZones.TryGetValue(token, out var named))
		{
			return TimeSpan.FromHours(named);
		}

		// RFC 2822 reads the military letters as an unknown zone, because RFC 822 defined their signs backwards.
		return token.Length == 1 && char.IsAsciiLetter(token[0]) ? TimeSpan.Zero : null;
	}
}
