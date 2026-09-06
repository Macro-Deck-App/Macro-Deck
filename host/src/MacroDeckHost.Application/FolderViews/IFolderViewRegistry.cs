using MacroDeck.Sdk.FolderViews;

namespace MacroDeckHost.Application.FolderViews;

/// <summary>One entry in the catalog a folder view picker renders.</summary>
/// <param name="FolderViewId">The qualified id a folder stores, or
/// <see cref="BuiltInFolderViews.WidgetGrid" /> for the built-in grid.</param>
/// <param name="ProviderId">The integration or plugin that owns the view. Empty for the built-in grid,
/// which no integration provides.</param>
/// <param name="Descriptor">What the provider registered.</param>
public sealed record FolderViewCatalogEntry(
	string FolderViewId,
	string ProviderId,
	FolderViewDescriptor Descriptor);

/// <summary>
/// The host side of the folder view provider contract: the live, in-memory catalog of folder renderings
/// an in-process integration or a connected plugin currently offers. A folder's own selection is persisted
/// with the folder, never here - which is what lets a folder survive its provider being uninstalled.
/// </summary>
public interface IFolderViewRegistry
{
	/// <summary>
	/// Registers a folder view, or replaces one already registered under the same owner and local id.
	/// </summary>
	/// <exception cref="ArgumentException">The owner id or the descriptor's id is invalid or empty, or the
	/// name is empty. A provider cannot collide with a built-in view: a registered id is always
	/// <c>owner::local</c>, and no built-in id carries a separator.</exception>
	Task<FolderViewRegistration> Register(string ownerId,
		FolderViewDescriptor folderView,
		CancellationToken cancellationToken = default);

	/// <summary>Withdraws a folder view. Unknown ids are ignored.</summary>
	Task Unregister(string ownerId, string localId, CancellationToken cancellationToken = default);

	/// <summary>Withdraws every folder view of one owner - what a stopping integration or a dropped plugin
	/// session leaves behind. Folders that selected them keep their selection.</summary>
	Task UnregisterAll(string ownerId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Resolves a qualified id against the live registry. Never throws. The built-in widget grid does not
	/// resolve here: it has no provider and is never served as a UI session, so a caller asking "which
	/// provider renders this folder" must ask <see cref="BuiltInFolderViews.IsWidgetGrid" /> first.
	/// </summary>
	bool TryResolve(string folderViewId, out FolderViewCatalogEntry entry);

	/// <summary>Every view currently on offer, including the built-in grid, in a stable order - the
	/// catalog the picker renders.</summary>
	IReadOnlyList<FolderViewCatalogEntry> GetAll();
}
