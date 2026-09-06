namespace MacroDeckHost.Application.FolderViews;

/// <summary>The folder views Macro Deck itself provides.</summary>
public static class BuiltInFolderViews
{
	/// <summary>
	/// The widget grid: the deck as it has always been, and the view every folder gets unless it selects
	/// another. Not a qualified id, because it is not provider-registered - the grid is rendered natively
	/// by each client, which is what keeps edit mode, drag and drop, marquee selection and the clipboard
	/// working. It is nonetheless a real, stored, selectable id, so that "which view does this folder
	/// use" has one answer rather than "the grid, unless a field is set".
	/// </summary>
	public const string WidgetGrid = "macrodeck.widget-grid";

	/// <summary>Whether <paramref name="folderViewId" /> names the built-in grid. An absent or empty id
	/// does: a folder stored before folder views existed is a grid.</summary>
	public static bool IsWidgetGrid(string? folderViewId)
		=> string.IsNullOrEmpty(folderViewId) ||
			string.Equals(folderViewId, WidgetGrid, StringComparison.Ordinal);
}
