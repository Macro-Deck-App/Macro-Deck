namespace MacroDeck.Plugin.Analyzers;

/// <summary>
/// A hardcoded mirror of <c>MacroDeck.Plugin.Hosting.Endpoints.ReservedPaths.IsReserved</c> - the
/// analyzer cannot reference that assembly (see <see cref="WellKnownTypeNames" />'s remarks), and the
/// prefix is a single constant rather than a regex, so - unlike the identity grammar - hardcoding it here
/// costs little. <c>ReservedRoutePathAnalyzerTests</c> checks this against a battery of paths alongside
/// the real <c>ReservedPaths.IsReserved</c> to keep the two from drifting apart.
/// </summary>
internal static class ReservedPathHeuristic
{
	public const string Prefix = "/_macrodeck";

	public static bool IsReserved(string? path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return false;
		}

		var normalized = path![0] == '/' ? path : "/" + path;

		if (!normalized.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		return normalized.Length == Prefix.Length || normalized[Prefix.Length] == '/';
	}
}
