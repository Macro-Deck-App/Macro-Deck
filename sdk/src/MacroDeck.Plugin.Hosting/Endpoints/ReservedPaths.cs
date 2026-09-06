namespace MacroDeck.Plugin.Hosting.Endpoints;

/// <summary>
/// The path prefix the SDK owns inside a plugin's own web application.
///
/// <para>
/// Reserving it is what lets the SDK add endpoints later without breaking a plugin that happened to
/// pick the same route. The prefix is deliberately ugly for the same reason.
/// </para>
/// </summary>
public static class ReservedPaths
{
	/// <summary>Everything under this prefix belongs to the SDK.</summary>
	public const string Prefix = "/_macrodeck";

	/// <summary>Liveness. Answers as soon as the process is serving, regardless of the session.</summary>
	public const string Health = Prefix + "/health";

	/// <summary>Readiness. Answers only once a session is open.</summary>
	public const string Ready = Prefix + "/ready";

	/// <summary>The plugin's own metadata and mode.</summary>
	public const string Info = Prefix + "/info";

	/// <summary>Connection state, queue depths and capability counts.</summary>
	public const string Diagnostics = Prefix + "/diagnostics";

	/// <summary>Every route the SDK serves.</summary>
	public static IReadOnlyList<string> All { get; } = [Health, Ready, Info, Diagnostics];

	/// <summary>Whether <paramref name="path" /> falls inside the reserved prefix.</summary>
	public static bool IsReserved(string? path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return false;
		}

		var normalized = path.StartsWith('/') ? path : "/" + path;

		if (!normalized.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		// "/_macrodeckery" is not reserved: the prefix has to end the path or be followed by a
		// segment boundary, or the reservation would quietly claim unrelated routes.
		return normalized.Length == Prefix.Length || normalized[Prefix.Length] == '/';
	}
}
