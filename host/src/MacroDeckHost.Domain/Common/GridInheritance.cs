using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Domain.Common;

public static class GridInheritance
{
	public static int ResolveRows(FolderEntity folder, IEnumerable<FolderEntity> profileFolders, int profileDefaultRows)
		=> Resolve(folder, profileFolders, f => f.Rows, profileDefaultRows);

	public static int ResolveColumns(
		FolderEntity folder,
		IEnumerable<FolderEntity> profileFolders,
		int profileDefaultColumns)
		=> Resolve(folder, profileFolders, f => f.Columns, profileDefaultColumns);

	public static List<FolderEntity> WithCandidate(
		IEnumerable<FolderEntity> profileFolders,
		Guid folderId,
		int? rows,
		int? columns,
		Guid? parentId)
		=> profileFolders
			.Select(folder => folder.Id == folderId ? Clone(folder, rows, columns, parentId) : folder)
			.ToList();

	public static List<GridConflict> FindGridConflicts(
		IReadOnlyCollection<FolderEntity> profileFolders,
		int profileDefaultRows,
		int profileDefaultColumns)
	{
		var conflicts = new List<GridConflict>();
		foreach (var folder in profileFolders)
		{
			var rows = ResolveRows(folder, profileFolders, profileDefaultRows);
			var columns = ResolveColumns(folder, profileFolders, profileDefaultColumns);
			var requiredRows = PinnedWidgetLayout.RequiredRows(folder, profileFolders);
			var requiredColumns = PinnedWidgetLayout.RequiredColumns(folder, profileFolders);
			if (rows < requiredRows || columns < requiredColumns)
			{
				conflicts.Add(new GridConflict(folder, requiredColumns, requiredRows));
			}
		}

		return conflicts;
	}

	public static List<GridConflict> NewConflicts(
		IReadOnlyCollection<GridConflict> current,
		IReadOnlyCollection<GridConflict> candidate)
		=> candidate.Where(conflict => current.All(existing => existing.Folder.Id != conflict.Folder.Id)).ToList();

	public static string DescribeConflicts(IEnumerable<GridConflict> conflicts)
		=> string.Join("; ",
			conflicts.Select(conflict =>
				$"{conflict.Folder.Name} needs at least {conflict.RequiredColumns} x {conflict.RequiredRows} " +
				"to keep every widget (including pinned ones) inside"));

	private static int Resolve(
		FolderEntity folder,
		IEnumerable<FolderEntity> profileFolders,
		Func<FolderEntity, int?> select,
		int profileDefault)
		=> Resolve<int>(folder, profileFolders, select) ?? profileDefault;

	private static T? Resolve<T>(
		FolderEntity folder,
		IEnumerable<FolderEntity> profileFolders,
		Func<FolderEntity, T?> select)
		where T : struct
		=> FolderEntityChain.AncestorsAndSelf(folder, profileFolders)
			.Select(select)
			.FirstOrDefault(value => value is not null);

	private static FolderEntity Clone(FolderEntity folder, int? rows, int? columns, Guid? parentId)
		=> new()
		{
			Id = folder.Id,
			Name = folder.Name,
			ProfileId = folder.ProfileId,
			ParentId = parentId,
			Order = folder.Order,
			Rows = rows,
			Columns = columns,
			BackgroundColor = folder.BackgroundColor,
			WidgetSpacing = folder.WidgetSpacing,
			WidgetBorderRadius = folder.WidgetBorderRadius,
			IsDefault = folder.IsDefault,
			CreatedAt = folder.CreatedAt,
			Widgets = folder.Widgets
		};
}

public sealed record GridConflict(FolderEntity Folder, int RequiredColumns, int RequiredRows);
