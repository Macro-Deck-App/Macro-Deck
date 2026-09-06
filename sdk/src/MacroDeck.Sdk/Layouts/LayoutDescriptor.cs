namespace MacroDeck.Sdk.Layouts;

/// <summary>
/// The surface a device or client exposes: its regions and what they can render. A descriptor describes
/// hardware, never profile content - Macro Deck reads it to constrain and validate a profile, and never
/// writes anything back into it.
/// </summary>
/// <param name="Id">
/// Provider-local id, stable across restarts and unique within the provider. Macro Deck qualifies it with
/// the owning provider - <c>your.plugin.id::layout-name</c> - and that qualified form is what a
/// <c>DeviceDescriptor.LayoutReference</c> points at. Must be non-empty.
/// </param>
/// <param name="Name">Human-readable layout name, e.g. "Stream Deck XL".</param>
/// <param name="Regions">
/// The layout's regions. May be empty for a device that has no addressable surface at all; a layout
/// Macro Deck can render a deck onto needs exactly one <see cref="LayoutRegionKinds.Grid" /> region.
/// </param>
/// <param name="Capabilities">Surface-wide capabilities. Null is read as <see cref="LayoutCapabilities.None" />.</param>
/// <param name="Metadata">Provider-defined metadata. Opaque to the host.</param>
public sealed record LayoutDescriptor(
	string Id,
	string Name,
	IReadOnlyList<LayoutRegion> Regions,
	LayoutCapabilities? Capabilities = null,
	IReadOnlyDictionary<string, string>? Metadata = null)
{
	/// <summary>
	/// The single grid region Macro Deck renders a deck onto, or null when the layout declares none - a
	/// pedal board, say. A layout with more than one grid has no primary grid: Macro Deck will not guess
	/// which surface a profile belongs to, and treats it as unconstrained.
	/// </summary>
	public LayoutRegion? PrimaryGrid
	{
		get
		{
			LayoutRegion? found = null;

			foreach (var region in Regions)
			{
				if (!string.Equals(region.Kind, LayoutRegionKinds.Grid, StringComparison.Ordinal))
				{
					continue;
				}

				if (found is not null)
				{
					return null;
				}

				found = region;
			}

			return found?.Grid is null ? null : found;
		}
	}
}
