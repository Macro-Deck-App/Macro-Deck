using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Domain.Common;

/// <summary>Reads the parent id a chain walk should follow, or reports that the folder has none.</summary>
public delegate bool ParentIdSelector<in TFolder, TId>(TFolder folder, out TId parentId);

/// <summary>
/// The folder → parent → profile walks expressed over an (id, parent id, widgets) shape alone, so the
/// persisted entity model and the transport DTOs - which key on <see cref="Guid" /> and on
/// <see cref="string" /> respectively - share one implementation instead of two that can drift.
/// </summary>
public static class FolderChain
{
	/// <summary>
	/// The folder, then each ancestor, lazily. Enumeration stops at the first folder whose parent is
	/// unset or missing from <paramref name="profileFolders" />, and a corrupt cycle terminates rather
	/// than looping.
	/// </summary>
	public static IEnumerable<TFolder> AncestorsAndSelf<TFolder, TId>(
		TFolder folder,
		IEnumerable<TFolder> profileFolders,
		Func<TFolder, TId> idOf,
		ParentIdSelector<TFolder, TId> parentOf,
		IEqualityComparer<TId>? comparer = null)
		where TId : notnull
	{
		var byId = Index(profileFolders, idOf, comparer);
		var visited = new HashSet<TId>(comparer);
		var current = folder;
		while (visited.Add(idOf(current)))
		{
			yield return current;

			if (!parentOf(current, out var parentId) || !byId.TryGetValue(parentId, out var parent))
			{
				yield break;
			}

			current = parent;
		}
	}

	/// <summary>The folder's own widgets, then every pinned widget from elsewhere whose scope reaches it.</summary>
	public static IEnumerable<TWidget> DisplayedWidgets<TFolder, TId, TWidget>(
		TFolder folder,
		IEnumerable<TFolder> profileFolders,
		Func<TFolder, TId> idOf,
		ParentIdSelector<TFolder, TId> parentOf,
		Func<TFolder, IEnumerable<TWidget>> widgetsOf,
		Func<TWidget, bool> isPinned,
		Func<TWidget, bool> pinnedProfileWide,
		IEqualityComparer<TId>? comparer = null)
		where TId : notnull
	{
		var folders = Materialize(profileFolders);
		return widgetsOf(folder)
			.Concat(PinnedWidgetsOutside(folder,
				folders,
				idOf,
				parentOf,
				widgetsOf,
				isPinned,
				pinnedProfileWide,
				comparer));
	}

	public static IEnumerable<TWidget> PinnedWidgetsOutside<TFolder, TId, TWidget>(
		TFolder folder,
		IEnumerable<TFolder> profileFolders,
		Func<TFolder, TId> idOf,
		ParentIdSelector<TFolder, TId> parentOf,
		Func<TFolder, IEnumerable<TWidget>> widgetsOf,
		Func<TWidget, bool> isPinned,
		Func<TWidget, bool> pinnedProfileWide,
		IEqualityComparer<TId>? comparer = null)
		where TId : notnull
	{
		var folders = Materialize(profileFolders);
		var reaching = new HashSet<TId>(AncestorsAndSelf(folder, folders, idOf, parentOf, comparer).Select(idOf),
			comparer);
		var ownId = idOf(folder);
		var idComparer = comparer ?? EqualityComparer<TId>.Default;

		return folders
			.Where(candidate => !idComparer.Equals(idOf(candidate), ownId))
			.SelectMany(candidate => widgetsOf(candidate)
				.Where(widget => isPinned(widget) &&
					(pinnedProfileWide(widget) || reaching.Contains(idOf(candidate)))));
	}

	private static IReadOnlyCollection<TFolder> Materialize<TFolder>(IEnumerable<TFolder> folders)
		=> folders as IReadOnlyCollection<TFolder> ?? folders.ToList();

	private static Dictionary<TId, TFolder> Index<TFolder, TId>(
		IEnumerable<TFolder> folders,
		Func<TFolder, TId> idOf,
		IEqualityComparer<TId>? comparer)
		where TId : notnull
	{
		var byId = new Dictionary<TId, TFolder>(comparer);
		foreach (var folder in folders)
		{
			byId[idOf(folder)] = folder;
		}

		return byId;
	}
}

/// <summary>The <see cref="FolderChain" /> shape of the persisted folder entities.</summary>
public static class FolderEntityChain
{
	public static Guid IdOf(FolderEntity folder) => folder.Id;

	public static bool ParentIdOf(FolderEntity folder, out Guid parentId)
	{
		parentId = folder.ParentId ?? Guid.Empty;
		return folder.ParentId.HasValue;
	}

	public static IEnumerable<WidgetEntity> WidgetsOf(FolderEntity folder) => folder.Widgets;

	public static IEnumerable<FolderEntity> AncestorsAndSelf(
		FolderEntity folder,
		IEnumerable<FolderEntity> profileFolders)
		=> FolderChain.AncestorsAndSelf<FolderEntity, Guid>(folder, profileFolders, IdOf, ParentIdOf);
}
