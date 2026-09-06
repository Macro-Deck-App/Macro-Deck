using MacroDeckHost.Application.Persistence.Profiles;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Infrastructure.Caching;

internal static class ProfileFileMapper
{
	public static ProfileFile ToFile(ProfileEntity profile, IEnumerable<FolderEntity> folders)
		=> new()
		{
			Id = profile.Id,
			Name = profile.Name,
			Order = profile.Order,
			LayoutType = profile.LayoutType,
			DefaultRows = profile.DefaultRows,
			DefaultColumns = profile.DefaultColumns,
			DefaultBackgroundColor = profile.DefaultBackgroundColor,
			DefaultWidgetSpacing = profile.DefaultWidgetSpacing,
			DefaultWidgetBorderRadius = profile.DefaultWidgetBorderRadius,
			Folders = folders
				.OrderBy(f => f.Order)
				.ThenBy(f => f.Id)
				.Select(ToFolderFile)
				.ToList()
		};

	public static ProfileEntity ToProfileEntity(ProfileFile file)
		=> new()
		{
			Id = file.Id,
			Name = file.Name,
			Order = file.Order,
			LayoutType = file.LayoutType,
			DefaultRows = file.DefaultRows,
			DefaultColumns = file.DefaultColumns,
			DefaultBackgroundColor = file.DefaultBackgroundColor,
			DefaultWidgetSpacing = file.DefaultWidgetSpacing,
			DefaultWidgetBorderRadius = file.DefaultWidgetBorderRadius
		};

	public static IEnumerable<FolderEntity> ToFolderEntities(ProfileFile file)
		=> file.Folders.Select(folder => ToFolderEntity(folder, file.Id));

	public static ProfileFolder ToFolderFile(FolderEntity folder)
		=> new()
		{
			Id = folder.Id,
			Name = folder.Name,
			ParentId = folder.ParentId,
			Order = folder.Order,
			Rows = folder.Rows,
			Columns = folder.Columns,
			BackgroundColor = folder.BackgroundColor,
			WidgetSpacing = folder.WidgetSpacing,
			WidgetBorderRadius = folder.WidgetBorderRadius,
			IsDefault = folder.IsDefault,
			ViewId = folder.ViewId,
			ViewConfiguration = folder.ViewConfiguration,
			CreatedAt = folder.CreatedAt,
			Widgets = folder.Widgets
				.Select(ToWidgetFile)
				.ToList(),
			FocusRules = folder.FocusRules.Count > 0 ? folder.FocusRules.Select(ToFocusRuleFile).ToList() : null
		};

	private static ProfileFolderFocusRule ToFocusRuleFile(FolderFocusRule rule)
		=> new()
		{
			Id = rule.Id,
			Enabled = rule.Enabled,
			ApplicationIdentity = rule.ApplicationIdentity,
			IdentityKind = rule.IdentityKind,
			DeviceId = rule.DeviceId,
			ReturnOnFocusLoss = rule.ReturnOnFocusLoss
		};

	private static ProfileWidget ToWidgetFile(WidgetEntity widget)
		=> new()
		{
			Id = widget.Id,
			Type = widget.Type,
			PositionX = widget.PositionX,
			PositionY = widget.PositionY,
			Width = widget.Width,
			Height = widget.Height,
			Data = widget.Data,
			IsPinned = widget.IsPinned,
			PinScope = widget.PinScope
		};

	private static FolderEntity ToFolderEntity(ProfileFolder folder, Guid profileId)
		=> new()
		{
			Id = folder.Id,
			ProfileId = profileId,
			Name = folder.Name,
			ParentId = folder.ParentId,
			Order = folder.Order,
			Rows = folder.Rows,
			Columns = folder.Columns,
			BackgroundColor = folder.BackgroundColor,
			WidgetSpacing = folder.WidgetSpacing,
			WidgetBorderRadius = folder.WidgetBorderRadius,
			IsDefault = folder.IsDefault,
			ViewId = folder.ViewId,
			ViewConfiguration = folder.ViewConfiguration,
			CreatedAt = folder.CreatedAt,
			Widgets = folder.Widgets
				.Select(widget => ToWidgetEntity(widget, folder.Id))
				.ToList(),
			FocusRules = folder.FocusRules?.Select(ToFocusRuleEntity).ToList() ?? []
		};

	private static FolderFocusRule ToFocusRuleEntity(ProfileFolderFocusRule rule)
		=> new()
		{
			Id = rule.Id,
			Enabled = rule.Enabled,
			ApplicationIdentity = rule.ApplicationIdentity,
			IdentityKind = rule.IdentityKind,
			DeviceId = rule.DeviceId,
			ReturnOnFocusLoss = rule.ReturnOnFocusLoss
		};

	private static WidgetEntity ToWidgetEntity(ProfileWidget widget, Guid folderId)
		=> new()
		{
			Id = widget.Id,
			FolderId = folderId,
			Type = widget.Type,
			PositionX = widget.PositionX,
			PositionY = widget.PositionY,
			Width = widget.Width,
			Height = widget.Height,
			Data = widget.Data,
			IsPinned = widget.IsPinned,
			PinScope = widget.PinScope
		};
}
