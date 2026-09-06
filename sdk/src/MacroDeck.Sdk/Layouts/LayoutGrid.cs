namespace MacroDeck.Sdk.Layouts;

/// <summary>
/// The geometry of a <see cref="LayoutRegionKinds.Grid" /> region. <see cref="Rows" /> and
/// <see cref="Columns" /> are what the surface has right now; when <see cref="IsConfigurable" /> is set
/// they are the current values within the declared bounds, and otherwise they are fixed and Macro Deck
/// holds a profile edited for this layout to exactly them.
/// </summary>
public sealed record LayoutGrid
{
	public required int Rows { get; init; }

	public required int Columns { get; init; }

	/// <summary>
	/// Whether the user may choose a different size. A fixed grid (the default) is hardware that has the
	/// keys it has, and the four Min/Max values below are then ignored; a configurable one is bounded by
	/// them, per axis, and an axis whose minimum equals its maximum is settled just as a fixed one is.
	/// </summary>
	public bool IsConfigurable { get; init; }

	public int MinRows { get; init; } = 1;

	public int MaxRows { get; init; } = 1;

	public int MinColumns { get; init; } = 1;

	public int MaxColumns { get; init; } = 1;

	/// <summary>
	/// Whether a size change takes effect on a live surface. When false, a configurable grid still accepts
	/// a new size but the provider may only apply it when the device next connects.
	/// </summary>
	public bool SupportsRuntimeResize { get; init; }

	/// <summary>Pixel size of one key, where the hardware has a fixed one.</summary>
	public LayoutKeySize? KeySize { get; init; }

	/// <summary>
	/// Whether the row count is settled: a fixed grid, or a configurable one whose bounds leave no choice.
	/// Deriving it here rather than declaring it separately is what keeps the host and the editor from
	/// disagreeing about a grid that is nominally configurable but pinned to a single value.
	/// </summary>
	public bool RowsLocked => !IsConfigurable || MinRows >= MaxRows;

	/// <summary>The column-axis counterpart of <see cref="RowsLocked" />.</summary>
	public bool ColumnsLocked => !IsConfigurable || MinColumns >= MaxColumns;
}
