namespace MacroDeckHost.Domain.Common;

public static class GridDefaults
{
	public const int Rows = 3;

	public const int Columns = 5;

	public const int WidgetSpacing = 12;

	public const int WidgetBorderRadius = 22;

	/// <summary>Configurable bounds the built-in software client layout declares (issue #384). The
	/// Angular client's own grid-size controls predate this and hardcoded the same range.</summary>
	public const int MinRows = 1;

	public const int MaxRows = 8;

	public const int MinColumns = 1;

	public const int MaxColumns = 12;
}
