using MacroDeck.Sdk.Decks;

namespace MacroDeckHost.Integrations.Deck.Actions;

internal static class FolderCycle
{
	public const string FolderOrder = "folder-order";
	public const string Alphabetical = "alphabetical";
	public const string Custom = "custom";

	public static IReadOnlyList<string> CustomSequence(
		IReadOnlyList<DeckFolder> folders,
		IReadOnlyList<string> pickedIds)
	{
		var known = folders.Select(folder => folder.Id).ToHashSet(StringComparer.Ordinal);
		return pickedIds.Where(known.Contains).Distinct(StringComparer.Ordinal).ToList();
	}

	public static IReadOnlyList<string> ProfileSequence(
		IReadOnlyList<DeckFolder> folders,
		string profileId,
		string order)
	{
		var inProfile = folders.Where(folder => folder.ProfileId == profileId).ToList();
		if (order == Alphabetical)
		{
			return inProfile
				.OrderBy(folder => folder.Label, StringComparer.InvariantCultureIgnoreCase)
				.ThenBy(folder => folder.Id, StringComparer.Ordinal)
				.Select(folder => folder.Id)
				.ToList();
		}

		return TreeOrder(inProfile);
	}

	public static string? Step(IReadOnlyList<string> sequence, string currentFolderId, bool forward, bool wrap)
	{
		if (sequence.Count == 0)
		{
			return null;
		}

		var index = IndexOf(sequence, currentFolderId);
		if (index < 0)
		{
			return forward ? sequence[0] : sequence[^1];
		}

		var next = index + (forward ? 1 : -1);
		if (next >= 0 && next < sequence.Count)
		{
			return sequence[next];
		}

		if (!wrap)
		{
			return null;
		}

		return forward ? sequence[0] : sequence[^1];
	}

	private static int IndexOf(IReadOnlyList<string> sequence, string folderId)
	{
		for (var index = 0; index < sequence.Count; index++)
		{
			if (sequence[index] == folderId)
			{
				return index;
			}
		}

		return -1;
	}

	private static List<string> TreeOrder(List<DeckFolder> folders)
	{
		var ids = folders.Select(folder => folder.Id).ToHashSet(StringComparer.Ordinal);
		var children = folders.ToLookup(folder => folder.ParentId is { } parentId && ids.Contains(parentId)
			? parentId
			: null);
		var sequence = new List<string>(folders.Count);
		var visited = new HashSet<string>(StringComparer.Ordinal);
		Visit(null);
		return sequence;

		void Visit(string? parentId)
		{
			var siblings = children[parentId]
				.OrderBy(folder => folder.Order ?? 0)
				.ThenBy(folder => folder.Id, StringComparer.Ordinal);
			foreach (var folder in siblings)
			{
				if (visited.Add(folder.Id))
				{
					sequence.Add(folder.Id);
					Visit(folder.Id);
				}
			}
		}
	}
}
