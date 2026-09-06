namespace MacroDeckHost.Infrastructure.Adb;

internal static class AdbReverseListParser
{
	private const string TcpPrefix = "tcp:";

	public static IReadOnlyList<AdbReverseMapping> Parse(string output)
	{
		var mappings = new List<AdbReverseMapping>();

		foreach (var rawLine in output.Split('\n'))
		{
			var line = rawLine.Trim('\r').Trim();
			if (line.Length == 0)
			{
				continue;
			}

			var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			if (tokens.Length < 2)
			{
				continue;
			}

			mappings.Add(new AdbReverseMapping(tokens[^2], tokens[^1]));
		}

		return mappings;
	}

	public static int? ParseTcpPort(string spec)
	{
		if (!spec.StartsWith(TcpPrefix, StringComparison.Ordinal))
		{
			return null;
		}

		return int.TryParse(spec.AsSpan(TcpPrefix.Length), out var port) ? port : null;
	}
}

internal sealed record AdbReverseMapping(string Remote, string Local);
