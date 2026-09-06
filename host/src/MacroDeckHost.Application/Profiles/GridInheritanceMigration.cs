using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Profiles;

public static class GridInheritanceMigration
{
	public static bool Normalize(ProfileEntity profile, IReadOnlyCollection<FolderEntity> folders)
	{
		var changed = false;
		foreach (var folder in OrderedFromRoots(folders))
		{
			changed |= NormalizeAxis(folder,
				folders,
				profile.DefaultRows,
				f => f.Rows,
				(f, v) => f.Rows = v,
				GridInheritance.ResolveRows);
			changed |= NormalizeAxis(folder,
				folders,
				profile.DefaultColumns,
				f => f.Columns,
				(f, v) => f.Columns = v,
				GridInheritance.ResolveColumns);
		}

		return changed;
	}

	private static bool NormalizeAxis(
		FolderEntity folder,
		IReadOnlyCollection<FolderEntity> folders,
		int profileDefault,
		Func<FolderEntity, int?> getValue,
		Action<FolderEntity, int?> setValue,
		Func<FolderEntity, IEnumerable<FolderEntity>, int, int> resolve)
	{
		if (getValue(folder) != profileDefault)
		{
			return false;
		}

		var parent = folder.ParentId is { } parentId ? folders.FirstOrDefault(f => f.Id == parentId) : null;
		var resolvedIfCleared = parent is null ? profileDefault : resolve(parent, folders, profileDefault);
		if (resolvedIfCleared != profileDefault)
		{
			return false;
		}

		setValue(folder, null);
		return true;
	}

	private static List<FolderEntity> OrderedFromRoots(IReadOnlyCollection<FolderEntity> folders)
	{
		var byParent = folders.Where(folder => folder.ParentId.HasValue)
			.GroupBy(folder => folder.ParentId!.Value)
			.ToDictionary(group => group.Key, group => group.ToList());

		var roots = folders.Where(folder => folder.ParentId is null).ToList();
		var visited = new HashSet<Guid>(roots.Select(folder => folder.Id));
		var queue = new Queue<FolderEntity>(roots);
		var ordered = new List<FolderEntity>();

		while (queue.Count > 0)
		{
			var folder = queue.Dequeue();
			ordered.Add(folder);

			if (!byParent.TryGetValue(folder.Id, out var children))
			{
				continue;
			}

			foreach (var child in children.Where(child => visited.Add(child.Id)))
			{
				queue.Enqueue(child);
			}
		}

		return ordered;
	}
}
