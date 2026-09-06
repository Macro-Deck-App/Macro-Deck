using System.Globalization;

namespace MacroDeckHost.Integrations.Http.Actions;

internal sealed class HttpStatusExpectation
{
	private static readonly HttpStatusExpectation _empty = new([], matchesAny: false);

	private readonly IReadOnlyList<(int Min, int Max)> _ranges;
	private readonly bool _matchesAny;

	private HttpStatusExpectation(IReadOnlyList<(int Min, int Max)> ranges, bool matchesAny)
	{
		_ranges = ranges;
		_matchesAny = matchesAny;
	}

	public bool Matches(int statusCode)
		=> _matchesAny || _ranges.Any(range => statusCode >= range.Min && statusCode <= range.Max);

	public static bool TryParse(string? expression, out HttpStatusExpectation expectation)
	{
		var text = string.IsNullOrWhiteSpace(expression) ? "2xx" : expression.Trim();

		if (string.Equals(text, "*", StringComparison.Ordinal) ||
			string.Equals(text, "any", StringComparison.OrdinalIgnoreCase))
		{
			expectation = new HttpStatusExpectation([], matchesAny: true);
			return true;
		}

		var tokens = text.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
		if (tokens.Length == 0)
		{
			expectation = _empty;
			return false;
		}

		var ranges = new List<(int Min, int Max)>(tokens.Length);
		foreach (var token in tokens)
		{
			if (!TryParseToken(token, out var range))
			{
				expectation = _empty;
				return false;
			}

			ranges.Add(range);
		}

		expectation = new HttpStatusExpectation(ranges, matchesAny: false);
		return true;
	}

	private static bool TryParseToken(string token, out (int Min, int Max) range)
	{
		range = default;

		if (token.Length == 3 && token[0] is >= '1' and <= '5' && token[1] is 'x' or 'X' && token[2] is 'x' or 'X')
		{
			var digit = token[0] - '0';
			range = (digit * 100, (digit * 100) + 99);
			return true;
		}

		var dash = token.IndexOf('-', StringComparison.Ordinal);
		if (dash > 0)
		{
			if (TryParseCode(token[..dash], out var min) &&
				TryParseCode(token[(dash + 1)..], out var max) &&
				min <= max)
			{
				range = (min, max);
				return true;
			}

			return false;
		}

		if (TryParseCode(token, out var exact))
		{
			range = (exact, exact);
			return true;
		}

		return false;
	}

	private static bool TryParseCode(string text, out int code)
	{
		if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out code) && code is >= 100 and <= 599)
		{
			return true;
		}

		code = 0;
		return false;
	}
}
