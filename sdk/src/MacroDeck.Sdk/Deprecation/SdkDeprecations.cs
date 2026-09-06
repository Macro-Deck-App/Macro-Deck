namespace MacroDeck.Sdk.Deprecation;

/// <summary>
/// Every API this SDK has ever deprecated, and every one it has since removed. The single source of
/// truth the host reads to turn an api id reported by a plugin into something a user can act on.
///
/// <para>
/// Both lists are append-only and permanent. <see cref="Removed" /> in particular is never pruned: a
/// plugin built years ago still reports the id of an API that no longer exists, and answering "unknown"
/// to that would be a worse answer than "removed in 4.0.0, use X instead".
/// </para>
///
/// <para>
/// Adding or removing an entry is deliberately not free - <c>SdkDeprecationLifecycleTests</c> pins both
/// lists against frozen literals and requires a matching row in
/// <c>docs/src/content/docs/policies/deprecations.md</c>. That is the mechanism behind issue #418's rule that
/// an API cannot be removed unless its deprecation lifecycle has been documented and tested.
/// </para>
/// </summary>
public static class SdkDeprecations
{
	/// <summary>
	/// Deprecated but still present. The action button state-provider work (issue #612) is the first
	/// data change: an N-state widget appearance supersedes the fixed on/off selector, so every wire
	/// consumer of the old selector is deprecated in favour of stable state ids.
	/// </summary>
	public static IReadOnlyList<SdkDeprecation> Active { get; } =
	[
		new()
		{
			ApiId = "T:MacroDeck.Sdk.Widgets.WidgetStateSelector",
			DisplayName = "MacroDeck.Sdk.Widgets.WidgetStateSelector",
			DeprecatedIn = "3.0.0",
			RemovedIn = "4.0.0",
			Guidance = "Address states by stable id through StateIds instead of this fixed selector.",
			Replacement = "MacroDeck.Sdk.Widgets.WidgetAppearanceRequest.StateIds"
		},
		new()
		{
			ApiId = "P:MacroDeck.Sdk.Widgets.WidgetAppearanceRequest.State",
			DisplayName = "MacroDeck.Sdk.Widgets.WidgetAppearanceRequest.State",
			DeprecatedIn = "3.0.0",
			RemovedIn = "4.0.0",
			Guidance = "Set StateIds to a stable state id, WidgetStates.Current or WidgetStates.All instead.",
			Replacement = "MacroDeck.Sdk.Widgets.WidgetAppearanceRequest.StateIds"
		},
		new()
		{
			ApiId = "P:MacroDeck.Sdk.Widgets.WidgetTargetInfo.HasOnOffStates",
			DisplayName = "MacroDeck.Sdk.Widgets.WidgetTargetInfo.HasOnOffStates",
			DeprecatedIn = "3.0.0",
			RemovedIn = "4.0.0",
			Guidance = "Read States.Count > 1 instead of this collapsed on/off flag.",
			Replacement = "MacroDeck.Sdk.Widgets.WidgetTargetInfo.States"
		}
	];

	/// <summary>Deprecated and since removed. Kept forever - see the remarks on this class.</summary>
	public static IReadOnlyList<SdkDeprecation> Removed { get; } = [];

	/// <summary>Both lists, which is what a lookup wants: a removed API is still describable.</summary>
	public static IReadOnlyList<SdkDeprecation> All { get; } = [.. Active, .. Removed];

	/// <summary>
	/// Looks up a deprecation by the documentation comment id a plugin reported. A miss is normal and
	/// not an error: a plugin built against a newer SDK can report an id this host has never heard of,
	/// which the host reports as an unknown-source finding rather than pretending to know it.
	/// </summary>
	public static bool TryGet(string apiId, out SdkDeprecation deprecation)
	{
		foreach (var candidate in All)
		{
			if (string.Equals(candidate.ApiId, apiId, StringComparison.Ordinal))
			{
				deprecation = candidate;
				return true;
			}
		}

		deprecation = null!;
		return false;
	}
}
