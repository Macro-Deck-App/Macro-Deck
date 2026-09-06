using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Profiles;

public static class StartFolderInvariant
{
	public static bool Normalize(ProfileEntity profile, ICollection<FolderEntity> folders)
	{
		var profileFolders = folders.ToList();
		if (profileFolders.Count == 0)
		{
			folders.Add(CreateHomeFolder(profile));
			return true;
		}

		var changed = false;
		var roots = profileFolders.Where(folder => folder.ParentId is null).ToList();
		if (roots.Count == 0)
		{
			var restoredRoot = SelectOldest(profileFolders);
			restoredRoot.ParentId = null;
			roots.Add(restoredRoot);
			changed = true;
		}

		var markedRoots = roots.Where(folder => folder.IsDefault).ToList();
		var hasChildMarker = profileFolders.Any(folder => folder.IsDefault && folder.ParentId is not null);
		if (markedRoots.Count == 1 && !hasChildMarker)
		{
			return changed;
		}

		var startFolder = SelectOldest(markedRoots.Count > 0 ? markedRoots : roots);
		return MarkAsStart(profileFolders, startFolder).Count > 0 || changed;
	}

	public static IReadOnlyList<FolderEntity> MarkAsStart(IEnumerable<FolderEntity> folders,
		FolderEntity startFolder)
	{
		if (startFolder.ParentId is not null)
		{
			throw new ArgumentException("Only a root folder can be the start folder.", nameof(startFolder));
		}

		var changed = new List<FolderEntity>();
		foreach (var folder in folders)
		{
			var isStart = folder.Id == startFolder.Id;
			if (folder.IsDefault == isStart)
			{
				continue;
			}

			folder.IsDefault = isStart;
			changed.Add(folder);
		}

		return changed;
	}

	public static FolderEntity SelectOldestRoot(IEnumerable<FolderEntity> folders)
	{
		var roots = folders.Where(folder => folder.ParentId is null).ToList();
		if (roots.Count == 0)
		{
			throw new InvalidOperationException("A start folder requires at least one root folder.");
		}

		return SelectOldest(roots);
	}

	private static FolderEntity CreateHomeFolder(ProfileEntity profile)
		=> new()
		{
			Id = Guid.NewGuid(),
			ProfileId = profile.Id,
			Name = "Home",
			Order = 0,
			// Inherited (issue #246), not copied: a root folder's grid then follows the profile default.
			Rows = null,
			Columns = null,
			BackgroundColor = profile.DefaultBackgroundColor,
			IsDefault = true,
			CreatedAt = DateTime.UtcNow
		};

	private static FolderEntity SelectOldest(IReadOnlyCollection<FolderEntity> folders)
	{
		var hasModernTimestamps = folders.All(folder => folder.CreatedAt != default);
		return hasModernTimestamps
			? folders.OrderBy(folder => folder.CreatedAt).ThenBy(folder => folder.Id).First()
			: folders.OrderBy(folder => folder.Order).ThenBy(folder => folder.Id).First();
	}
}
