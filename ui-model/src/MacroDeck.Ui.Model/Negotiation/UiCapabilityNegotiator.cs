using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Versioning;

namespace MacroDeck.Ui.Model.Negotiation;

/// <summary>
/// Negotiates the UI model version and one node's component, both non-fatally: a rejection degrades
/// rather than terminating, which is what makes "unknown node types are non-fatal" a property of this
/// model rather than a promise a renderer has to keep on its own. Reuses the plugin protocol's own
/// negotiation algorithm verbatim: <c>negotiated = min(renderer max, ours)</c>, failing when that falls
/// below <c>max(renderer min, our floor)</c>.
/// </summary>
public static class UiCapabilityNegotiator
{
	/// <summary>The <see cref="UiNegotiationResult.Subject" /> used for
	/// <see cref="NegotiateModelVersion" />.</summary>
	public const string ModelVersionSubject = "ui-model";

	/// <summary>
	/// Negotiates the UI model version against <see cref="UiCapabilities.UiProtocol" />. A failure here
	/// is precisely the dual-path trigger the issue describes: no supported model version means no
	/// tree, so the renderer falls back to the declared field list.
	/// </summary>
	public static UiNegotiationResult NegotiateModelVersion(UiCapabilities capabilities)
	{
		var range = capabilities.UiProtocol;
		var negotiated = Math.Min(range.Maximum, UiModelVersions.Current);
		var floor = Math.Max(range.Minimum, UiModelVersions.Minimum);

		return negotiated < floor
			? UiNegotiationResult.Fallback(ModelVersionSubject, "No overlapping UI model version.")
			: UiNegotiationResult.Accept(ModelVersionSubject, negotiated);
	}

	/// <summary>
	/// Negotiates one node's component, identified by its <c>Type</c>, against the renderer's declared
	/// <see cref="UiCapabilities.Components" />. Evaluates only <paramref name="node" /> itself - it
	/// does not recurse into <see cref="UiNode.Fallback" />; recursing into an unsupported node's
	/// fallback subtree is the renderer's obligation, documented on <see cref="UiNode.Fallback" />.
	///
	/// <para>
	/// A node with no declared <c>RequiredComponentVersion</c> requires version 1. Support requires the
	/// renderer's range to cover the required version exactly - <c>Minimum &lt;= required &lt;= Maximum</c> -
	/// never clamped down: clamping would render a node with a component older than it declared it
	/// needs. The negotiated version, when supported, is always the required version.
	/// </para>
	///
	/// <para>
	/// <see cref="UiCapabilities.SupportsAllComponents" /> wins over a contradicting or absent range: a
	/// renderer that says it supports everything is trusted at whatever version the node requires,
	/// without consulting <see cref="UiCapabilities.Components" /> at all.
	/// </para>
	/// </summary>
	public static UiNegotiationResult NegotiateComponent(UiNode node, UiCapabilities capabilities)
	{
		var required = node.RequiredComponentVersion ?? 1;

		if (capabilities.SupportsAllComponents)
		{
			return UiNegotiationResult.Accept(node.Type, required);
		}

		if (!capabilities.Components.TryGetValue(node.Type, out var range))
		{
			return UiNegotiationResult.Fallback(node.Type, "The renderer does not declare this component.");
		}

		return required >= range.Minimum && required <= range.Maximum
			? UiNegotiationResult.Accept(node.Type, required)
			: UiNegotiationResult.Fallback(node.Type,
				"The renderer's declared range for this component does not cover the required version.");
	}
}
