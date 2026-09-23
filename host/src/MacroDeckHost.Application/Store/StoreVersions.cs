using MacroDeck.Plugin.Packaging.Versioning;

namespace MacroDeckHost.Application.Store;

public static class StoreVersions
{
	// A version that is not SemVer compares by case-insensitive text, as the download metadata always has.
	public static bool Same(string left, string right) =>
		SemanticVersion.TryParse(left, out var leftVersion) &&
		SemanticVersion.TryParse(right, out var rightVersion)
			? leftVersion.Equals(rightVersion)
			: string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

	public static bool IsOlder(string? candidate, string? than) =>
		SemanticVersion.TryParse(candidate, out var candidateVersion) &&
		SemanticVersion.TryParse(than, out var thanVersion) &&
		candidateVersion < thanVersion;
}
