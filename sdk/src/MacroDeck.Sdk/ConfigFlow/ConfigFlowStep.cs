using MacroDeck.Sdk.Actions;
using MacroDeck.Localization;

namespace MacroDeck.Sdk.ConfigFlow;

/// <summary>
/// A single screen of a config flow. The fields reuse the <see cref="ActionParameter"/>
/// schema so they render with the same controls as action parameters.
/// </summary>
public sealed class ConfigFlowStep
{
	public required string StepId { get; init; }

	public LocalizedText Title { get; init; }

	/// <summary>The sentence introducing <see cref="Instructions"/> (or the whole guide, if there are none).</summary>
	public LocalizedText Description { get; init; }

	/// <summary>
	/// Values the step needs to show that are not tied to a single instruction (a bare endpoint, a
	/// generated token). Rendered above <see cref="Instructions"/>.
	/// </summary>
	public IReadOnlyList<ConfigFlowCopyValue> Values { get; init; } = [];

	/// <summary>
	/// Ordered setup instructions, rendered above the fields as a numbered list. Prefer this over
	/// packing a numbered list into <see cref="Description"/>; keep <see cref="Description"/> for the
	/// sentence introducing the list.
	/// </summary>
	public IReadOnlyList<ConfigFlowInstruction> Instructions { get; init; } = [];

	/// <summary>
	/// Optional labeled links shown on the step, e.g. setup documentation or a provider's
	/// developer portal. Empty by default; a step may expose several.
	/// </summary>
	public IReadOnlyList<ConfigFlowLink> Links { get; init; } = [];

	public required IReadOnlyList<ActionParameter> Fields { get; init; }

	/// <summary>
	/// Fields the step hides behind an "Advanced configuration" switch. Use it for the setting that
	/// exists as a way out rather than as a choice - an own client id, a non-default endpoint - so the
	/// ordinary path stays a single control.
	///
	/// They must not be <c>Required</c>: the point of hiding a field is that leaving it alone is a
	/// valid answer. The dialog therefore gates Continue on the visible fields only, and it opens the
	/// section on its own when one of these already carries a value, so a re-shown step never hides
	/// something the user typed.
	/// </summary>
	public IReadOnlyList<ActionParameter> AdvancedFields { get; init; } = [];
}
