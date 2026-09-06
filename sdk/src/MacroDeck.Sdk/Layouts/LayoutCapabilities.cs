namespace MacroDeck.Sdk.Layouts;

/// <summary>
/// Surface-wide facts about a layout that are not tied to one region. Region geometry lives on
/// <see cref="LayoutRegion" />; this is what a consumer needs before it looks at any region.
/// </summary>
public sealed record LayoutCapabilities
{
	/// <summary>A layout that declares nothing. Registering with this is valid.</summary>
	public static readonly LayoutCapabilities None = new();

	/// <summary>
	/// Default rendering capabilities for every region that does not override them in
	/// <see cref="LayoutRegion.Visuals" />. Null means the layout says nothing, which is read as "nothing
	/// is supported" rather than "everything is".
	/// </summary>
	public LayoutVisualCapabilities? Visuals { get; init; }

	/// <summary>Provider-defined extras. Keys and values are opaque to the host.</summary>
	public IReadOnlyDictionary<string, string> Extra { get; init; }
		= new Dictionary<string, string>(StringComparer.Ordinal);
}
