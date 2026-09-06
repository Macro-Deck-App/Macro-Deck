using MacroDeckHost.Application.Ui.Transport.Messages.Folders;

namespace MacroDeckHost.Application.Profiles;

public static class StartFolderResolver
{
	public static Folder? Resolve(IProfileRegistry profiles, string profileId)
		=> SelectStartFolder(profiles.GetFoldersForProfile(profileId));

	public static Folder? SelectStartFolder(IReadOnlyList<Folder> folders)
	{
		var roots = folders
			.Where(folder => folder.ParentId is null)
			.OrderBy(folder => folder.Order)
			.ThenBy(folder => folder.Id, StringComparer.Ordinal)
			.ToList();

		if (roots.Count == 0)
		{
			return null;
		}

		return roots.FirstOrDefault(folder => folder.IsDefault) ?? roots[0];
	}
}
