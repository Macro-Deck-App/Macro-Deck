namespace MacroDeck.Ui.Model.Surfaces;

/// <summary>
/// The <see cref="UiSurface.Attributes" /> keys a <see cref="UiSurfaceKinds.Folder" /> surface carries to
/// say which folder is being rendered and under which of the provider's views.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Configuration" /> travels on the surface for the same reason
/// <see cref="UiWidgetSurfaceAttributes.Data" /> does: a provider outside the host cannot read the host's
/// stored folders, so what the user configured has to arrive with the request rather than be looked up.
/// Macro Deck never interprets it - it is written and read only by the provider that owns the view.
/// </para>
/// <para>
/// Deliberately absent: the folder's grid size, spacing and border radius. Those describe the built-in
/// widget grid, which is not what a provider-rendered folder draws, and a key no reader reads is a key
/// that can never be removed.
/// </para>
/// </remarks>
public static class UiFolderSurfaceAttributes
{
	/// <summary>The folder being rendered.</summary>
	public const string FolderId = "folderId";

	/// <summary>The folder's name, so a view can title itself without a round trip.</summary>
	public const string FolderName = "folderName";

	/// <summary>The qualified id of the view the folder selected, so a provider offering several declines
	/// one it does not serve rather than guessing from the configuration's shape.</summary>
	public const string ViewId = "viewId";

	/// <summary>The view's configuration, as the JSON object it is stored as. Absent reads as empty - a
	/// view whose provider declares no configuration is opened without one.</summary>
	public const string Configuration = "configuration";
}
