namespace MacroDeck.Sdk.Profiles;

/// <summary>
/// The fixed layout of an integration-owned profile. <see cref="RowsLocked"/>/<see cref="ColumnsLocked"/>
/// tell the UI to disable the corresponding grid editing controls.
/// </summary>
public sealed record ProfileLayout(LayoutKind Kind, int Rows, int Columns, bool RowsLocked, bool ColumnsLocked)
{
	/// <summary>A grid layout; locks both dimensions by default (virtual profiles are read-only).</summary>
	public static ProfileLayout Grid(int rows, int columns, bool locked = true)
		=> new(LayoutKind.Grid, rows, columns, locked, locked);
}
