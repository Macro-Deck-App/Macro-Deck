using System.Globalization;

namespace MacroDeckHost.Application.Logging;

public static class LogFileStamp
{
	// A day split on size continues as day_001, day_002: the sequence orders as a number, so
	// day_1000 comes after day_999, which an ordinal comparison gets wrong.
	public static int Compare(string? left, string? right)
	{
		var (leftDay, leftSequence) = Split(left);
		var (rightDay, rightSequence) = Split(right);
		var byDay = string.CompareOrdinal(leftDay, rightDay);

		return byDay != 0 ? byDay : leftSequence.CompareTo(rightSequence);
	}

	private static (string Day, long Sequence) Split(string? stamp)
	{
		if (string.IsNullOrEmpty(stamp))
		{
			return (string.Empty, 0);
		}

		var separator = stamp.IndexOf('_', StringComparison.Ordinal);
		if (separator < 0
			|| !long.TryParse(stamp[(separator + 1)..],
				NumberStyles.None,
				CultureInfo.InvariantCulture,
				out var sequence))
		{
			return (stamp, 0);
		}

		return (stamp[..separator], sequence);
	}
}
