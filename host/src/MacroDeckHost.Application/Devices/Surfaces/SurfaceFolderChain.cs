using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Devices.Surfaces;

/// <summary>
/// The <see cref="FolderChain" /> shape of the transport folder DTOs, and the inheritance resolution
/// on top of it. The DTO seam is deliberate: it is what lets a virtual profile - whose folders exist
/// only as provider-declared DTOs and never as persisted entities - render and navigate exactly like a
/// stored one.
/// </summary>
internal static class SurfaceFolderChain
{
	public static string IdOf(Folder folder) => folder.Id;

	public static bool ParentIdOf(Folder folder, out string parentId)
	{
		parentId = folder.ParentId ?? string.Empty;
		return !string.IsNullOrEmpty(folder.ParentId);
	}

	public static IEnumerable<Widget> WidgetsOf(Folder folder) => folder.Widgets;

	/// <summary>
	/// Every widget the folder renders, each paired with the folder that owns it. A pinned widget is
	/// displayed here but still lives in its home folder, and running its trigger is only ever addressed
	/// to that folder - which is why the pair, not the widget alone, is what the surface is built from.
	/// </summary>
	public static IEnumerable<(Widget Widget, string FolderId)> DisplayedWidgets(
		Folder folder,
		IReadOnlyList<Folder> profileFolders)
		=> FolderChain.DisplayedWidgets<Folder, string, (Widget Widget, string FolderId)>(folder,
			profileFolders,
			IdOf,
			ParentIdOf,
			owner => WidgetsOf(owner).Select(widget => (widget, IdOf(owner))),
			entry => entry.Widget.IsPinned,
			entry => entry.Widget.PinScope == PinScope.Profile,
			StringComparer.Ordinal);

	public static int ResolveRows(Folder folder, IReadOnlyList<Folder> profileFolders, int profileDefault)
		=> Resolve(folder, profileFolders, f => f.Rows) ?? profileDefault;

	public static int ResolveColumns(Folder folder, IReadOnlyList<Folder> profileFolders, int profileDefault)
		=> Resolve(folder, profileFolders, f => f.Columns) ?? profileDefault;

	public static int ResolveWidgetSpacing(Folder folder, IReadOnlyList<Folder> profileFolders, int? profileDefault)
		=> Resolve(folder, profileFolders, f => f.WidgetSpacing) ?? profileDefault ?? GridDefaults.WidgetSpacing;

	public static int ResolveWidgetBorderRadius(
		Folder folder,
		IReadOnlyList<Folder> profileFolders,
		int? profileDefault)
		=> Resolve(folder, profileFolders, f => f.WidgetBorderRadius) ??
			profileDefault ??
			GridDefaults.WidgetBorderRadius;

	/// <summary>
	/// Deliberately not a chain walk, unlike every other value here: the client resolves a background
	/// from the folder's own value and then the profile default alone, and a device and a browser have to
	/// render the same surface.
	/// </summary>
	public static string? ResolveBackgroundColor(Folder folder, string? profileDefault)
		=> folder.BackgroundColor ?? profileDefault;

	private static T? Resolve<T>(Folder folder, IReadOnlyList<Folder> profileFolders, Func<Folder, T?> select)
		where T : struct
		=> Chain(folder, profileFolders).Select(select).FirstOrDefault(value => value is not null);

	private static IEnumerable<Folder> Chain(Folder folder, IReadOnlyList<Folder> profileFolders)
		=> FolderChain.AncestorsAndSelf<Folder, string>(folder,
			profileFolders,
			IdOf,
			ParentIdOf,
			StringComparer.Ordinal);
}
