using System.Collections.Immutable;

namespace MacroDeck.Plugin.Analyzers;

/// <summary>
/// The fifteen capability kinds a plugin can declare, hardcoded rather than shared with
/// <c>MacroDeck.Plugin.Protocol.Handshake.CapabilityKinds</c>.
///
/// <para>
/// An analyzer running inside the compiler only ever sees the symbols of whatever it is analyzing - it
/// cannot execute the target framework's own code, so it cannot read <c>CapabilityKinds.All</c>'s value
/// even if this project referenced that assembly. The vocabulary is flat and has changed roughly once in
/// the SDK's history, so hardcoding it here and asserting equality with the real list in a test (a drift
/// test) is the appropriate tool - not linked source, which is reserved for the identity grammar's regex
/// patterns, where the two copies are one nontrivial pattern apart from disagreeing silently.
/// </para>
/// </summary>
internal static class CapabilityKindVocabulary
{
	public static readonly ImmutableArray<string> All =
	[
		"actions", "events", "variables", "icons", "config-flow", "music-player", "weather",
		"virtual-profiles", "issues", "ui", "localization", "device-provider", "layout-provider",
		"folder-view-provider", "migration", "widget-type-provider"
	];

	public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind);
}
