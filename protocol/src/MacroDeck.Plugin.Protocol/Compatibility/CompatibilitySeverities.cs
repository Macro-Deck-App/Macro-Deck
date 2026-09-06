namespace MacroDeck.Plugin.Protocol.Compatibility;

/// <summary>
/// How loudly a <see cref="CompatibilityFinding" /> should be presented. Deliberately the same three
/// words the integration-issue pipeline already uses, so the UI renders both with one set of styles
/// rather than inventing a second severity vocabulary.
/// </summary>
public static class CompatibilitySeverities
{
	public const string Info = "info";

	public const string Warning = "warning";

	public const string Error = "error";

	public static readonly IReadOnlyList<string> All = [Info, Warning, Error];

	private static readonly HashSet<string> _known = new(All, StringComparer.Ordinal);

	public static bool IsKnown(string? severity) => severity is not null && _known.Contains(severity);
}
