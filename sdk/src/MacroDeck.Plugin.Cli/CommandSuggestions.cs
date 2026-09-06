namespace MacroDeck.Plugin.Cli;

/// <summary>Suggests the command the user probably meant when the first token of an invocation is not a
/// known command name - the "Did you mean 'pack'?" half of <c>CliEntryPoint</c>'s unknown-command
/// diagnostic.</summary>
internal static class CommandSuggestions
{
	/// <summary>Returns the single closest candidate to <paramref name="typed" /> by Levenshtein distance,
	/// or <see langword="null" /> when nothing is close enough to be worth suggesting, or when two or more
	/// candidates tie for closest - a tie is not a suggestion, it is a coin flip, and a wrong guess is worse
	/// than none. "Close enough" is a distance of at most 2 that is also strictly less than the typed
	/// token's own length, so a two-character typo like "ab" never "suggests" an unrelated two-character
	/// command purely because the edit distance happens to be within budget.</summary>
	public static string? For(string typed, IEnumerable<string> candidates)
	{
		string? best = null;
		var bestDistance = int.MaxValue;
		var tied = false;

		foreach (var candidate in candidates)
		{
			var distance = Distance(typed, candidate);

			if (distance < bestDistance)
			{
				best = candidate;
				bestDistance = distance;
				tied = false;
			}
			else if (distance == bestDistance)
			{
				tied = true;
			}
		}

		if (best is null || tied)
		{
			return null;
		}

		return bestDistance <= 2 && bestDistance < typed.Length ? best : null;
	}

	private static int Distance(string a, string b)
	{
		a = a.ToLowerInvariant();
		b = b.ToLowerInvariant();

		var previousRow = new int[b.Length + 1];
		var currentRow = new int[b.Length + 1];

		for (var j = 0; j <= b.Length; j++)
		{
			previousRow[j] = j;
		}

		for (var i = 1; i <= a.Length; i++)
		{
			currentRow[0] = i;

			for (var j = 1; j <= b.Length; j++)
			{
				var cost = a[i - 1] == b[j - 1] ? 0 : 1;
				currentRow[j] = Math.Min(Math.Min(currentRow[j - 1] + 1, previousRow[j] + 1),
					previousRow[j - 1] + cost);
			}

			(previousRow, currentRow) = (currentRow, previousRow);
		}

		return previousRow[b.Length];
	}
}
