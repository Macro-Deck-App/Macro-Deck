using System.Runtime.InteropServices;

namespace MacroDeck.Plugin.Packaging.Manifest;

/// <summary>
/// Resolves which manifest entrypoints a running RID may use. Exact match plus a fixed, small set of
/// silicon fallbacks - no <c>"any"</c> key and no <c>dotnet</c> muxer entrypoints, since #412 does not
/// support framework-dependent plugins. <c>linux-musl-*</c> resolves no fallback: an explicit
/// non-goal, not an oversight.
/// </summary>
public static class PluginRuntimeIdentifiers
{
	private static readonly Dictionary<string, string> _fallbacks = new(StringComparer.Ordinal)
	{
		["osx-arm64"] = "osx-x64",
		["win-arm64"] = "win-x64"
	};

	/// <summary>The host's own RID. Kept separate from <see cref="CandidatesFor"/> so that method stays
	/// a pure function of its argument and is testable without depending on the running machine.</summary>
	public static string Current => RuntimeInformation.RuntimeIdentifier;

	/// <summary>Candidate RIDs to look up in a manifest's entrypoints, most specific first.</summary>
	public static IReadOnlyList<string> CandidatesFor(string rid)
		=> _fallbacks.TryGetValue(rid, out var fallback) ? [rid, fallback] : [rid];
}
