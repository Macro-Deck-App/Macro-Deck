namespace MacroDeck.Sdk.Layouts;

/// <summary>
/// One addressable area of a surface. <see cref="Kind" /> is an open string from
/// <see cref="LayoutRegionKinds" /> rather than an enum, so a region type added later travels through an
/// older host intact instead of breaking it.
/// </summary>
/// <remarks>
/// Only <see cref="LayoutRegionKinds.Grid" /> currently carries typed geometry, in <see cref="Grid" />.
/// Every other kind round-trips with its id, name, <see cref="Count" /> and <see cref="Extra" />, which is
/// enough for a provider to declare encoders or pedals today and for Macro Deck to describe them later
/// without a contract change.
/// </remarks>
public sealed record LayoutRegion
{
	/// <summary>Stable id, unique within the layout. Non-empty.</summary>
	public required string Id { get; init; }

	/// <summary>One of <see cref="LayoutRegionKinds" />, or a kind this version does not know.</summary>
	public required string Kind { get; init; }

	/// <summary>Human-readable name, shown where a region has to be named. Optional.</summary>
	public string? Name { get; init; }

	/// <summary>Set when <see cref="Kind" /> is <see cref="LayoutRegionKinds.Grid" />; otherwise null.</summary>
	public LayoutGrid? Grid { get; init; }

	/// <summary>
	/// How many inputs the region has, for the kinds that are a count rather than a geometry - four
	/// encoders, two pedals. Zero for a grid, whose size lives in <see cref="Grid" />.
	/// </summary>
	public int Count { get; init; }

	/// <summary>Overrides the layout's visual capabilities for this region. Null means inherit them.</summary>
	public LayoutVisualCapabilities? Visuals { get; init; }

	/// <summary>Provider-defined extras. Keys and values are opaque to the host.</summary>
	public IReadOnlyDictionary<string, string> Extra { get; init; }
		= new Dictionary<string, string>(StringComparer.Ordinal);
}
