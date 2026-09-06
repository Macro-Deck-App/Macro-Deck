using MacroDeckHost.Application.Caching;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal readonly record struct WidgetSnapshot(
	Guid Id,
	Guid FolderId,
	int X,
	int Y,
	int Width,
	int Height,
	bool IsPinned,
	PinScope Scope);

internal readonly record struct FolderSnapshot(Guid Id, Guid? ParentId, int? Rows, int? Columns, int Order);

internal readonly record struct ProfileSnapshot(List<FolderSnapshot> Folders, List<WidgetSnapshot> Widgets);

internal static class PinScopeInvariants
{
	public static ProfileSnapshot Snapshot(IFolderCache folderCache, Guid profileId)
	{
		var folders = folderCache.GetFoldersByProfileId(profileId);
		var folderSnapshot = folders
			.Select(f => new FolderSnapshot(f.Id, f.ParentId, f.Rows, f.Columns, f.Order))
			.ToList();
		var widgetSnapshot = folders
			.SelectMany(f => f.Widgets)
			.Select(w => new WidgetSnapshot(w.Id,
				w.FolderId,
				w.PositionX,
				w.PositionY,
				w.Width,
				w.Height,
				w.IsPinned,
				w.PinScope))
			.ToList();
		return new ProfileSnapshot(folderSnapshot, widgetSnapshot);
	}

	public static void AssertUnchanged(ProfileSnapshot before, IFolderCache folderCache, Guid profileId)
	{
		var after = Snapshot(folderCache, profileId);
		Assert.That(after.Folders, Is.EqualTo(before.Folders));
		Assert.That(after.Widgets, Is.EqualTo(before.Widgets));
	}

	public static void AssertDeckIsRenderable(IFolderCache folderCache, IProfileCache profileCache, Guid profileId)
	{
		var profileFolders = folderCache.GetFoldersByProfileId(profileId);
		var profile = profileCache.GetById(profileId);
		var defaultRows = profile?.DefaultRows ?? GridDefaults.Rows;
		var defaultColumns = profile?.DefaultColumns ?? GridDefaults.Columns;

		foreach (var folder in profileFolders)
		{
			var rows = GridInheritance.ResolveRows(folder, profileFolders, defaultRows);
			var columns = GridInheritance.ResolveColumns(folder, profileFolders, defaultColumns);
			var displayed = PinnedWidgetLayout.DisplayedWidgets(folder, profileFolders).ToList();

			foreach (var widget in displayed)
			{
				Assert.That(widget.PositionX,
					Is.GreaterThanOrEqualTo(0),
					$"{folder.Name}: widget {widget.Id} X is negative");
				Assert.That(widget.PositionY,
					Is.GreaterThanOrEqualTo(0),
					$"{folder.Name}: widget {widget.Id} Y is negative");
				Assert.That(widget.PositionX + widget.Width,
					Is.LessThanOrEqualTo(columns),
					$"{folder.Name}: widget {widget.Id} does not fit the {columns}-wide grid");
				Assert.That(widget.PositionY + widget.Height,
					Is.LessThanOrEqualTo(rows),
					$"{folder.Name}: widget {widget.Id} does not fit the {rows}-tall grid");
			}

			for (var i = 0; i < displayed.Count; i++)
			{
				for (var j = i + 1; j < displayed.Count; j++)
				{
					Assert.That(PinnedWidgetLayout.Overlaps(displayed[i], displayed[j]),
						Is.False,
						$"{folder.Name}: widgets {displayed[i].Id} and {displayed[j].Id} overlap");
				}
			}
		}
	}
}
