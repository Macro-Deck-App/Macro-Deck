using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Domain.Common;

public static class FolderSubtree
{
	public static List<FolderEntity> Collect(IEnumerable<FolderEntity> profileFolders, FolderEntity root)
	{
		var subtree = new List<FolderEntity> { root };

		var byParent = profileFolders
			.Where(folder => folder.ParentId.HasValue)
			.GroupBy(folder => folder.ParentId!.Value)
			.ToDictionary(group => group.Key,
				group => group.OrderBy(folder => folder.Order).ThenBy(folder => folder.Id).ToList());

		var visited = new HashSet<Guid> { root.Id };
		var queue = new Queue<Guid>();
		queue.Enqueue(root.Id);
		while (queue.Count > 0)
		{
			if (!byParent.TryGetValue(queue.Dequeue(), out var children))
			{
				continue;
			}

			foreach (var child in children.Where(child => visited.Add(child.Id)))
			{
				subtree.Add(child);
				queue.Enqueue(child.Id);
			}
		}

		return subtree;
	}

	public static HashSet<Guid> AncestorsAndSelf(IEnumerable<FolderEntity> profileFolders, FolderEntity folder)
		=> FolderEntityChain.AncestorsAndSelf(folder, profileFolders).Select(f => f.Id).ToHashSet();
}
