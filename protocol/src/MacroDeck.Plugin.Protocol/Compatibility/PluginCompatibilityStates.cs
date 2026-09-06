namespace MacroDeck.Plugin.Protocol.Compatibility;

/// <summary>
/// The overall verdict on one plugin's compatibility, as one word the UI can show on a card.
///
/// <para>
/// Ordered by severity via <see cref="Rank" />: a plugin's state is the worst of everything found about
/// it, never the first thing found. The ordering is a product decision, not an implementation detail -
/// a capability the host rejected is a loss the user has <em>now</em>, so it outranks an API scheduled
/// for removal in a future release.
/// </para>
///
/// <para>Append-only within a protocol major.</para>
/// </summary>
public static class PluginCompatibilityStates
{
	/// <summary>Nothing to report.</summary>
	public const string Compatible = "compatible";

	/// <summary>Confirmed use of deprecated APIs, none of them due for removal yet.</summary>
	public const string DeprecatedApis = "deprecated_apis";

	/// <summary>Nothing is broken, but the plugin is behind - typically an older SDK.</summary>
	public const string UpdateRecommended = "update_recommended";

	/// <summary>Confirmed use of an API whose declared removal version has been reached.</summary>
	public const string UpdateRequired = "update_required";

	/// <summary>The session works, but at least one declared capability was rejected.</summary>
	public const string PartiallyIncompatible = "partially_incompatible";

	/// <summary>The plugin cannot work with this host at all - no negotiable protocol version.</summary>
	public const string Incompatible = "incompatible";

	public static readonly IReadOnlyList<string> All =
	[
		Compatible, DeprecatedApis, UpdateRecommended, UpdateRequired, PartiallyIncompatible, Incompatible,
	];

	private static readonly HashSet<string> _known = new(All, StringComparer.Ordinal);

	public static bool IsKnown(string? state) => state is not null && _known.Contains(state);

	/// <summary>
	/// Severity order, ascending. An unknown state ranks below everything so a state added by a newer
	/// host never silently outranks one this host understands.
	/// </summary>
	public static int Rank(string? state) => state switch
	{
		Compatible => 0,
		DeprecatedApis => 1,
		UpdateRecommended => 2,
		UpdateRequired => 3,
		PartiallyIncompatible => 4,
		Incompatible => 5,
		_ => -1
	};
}
