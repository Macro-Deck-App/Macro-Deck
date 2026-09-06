using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Ui.Handlers;

public static class FolderDtoMapper
{
	public static Folder MapToDto(FolderEntity entity)
	{
		var folder = new Folder
		{
			Id = entity.Id.ToString(),
			Name = entity.Name,
			ProfileId = entity.ProfileId.ToString(),
			Order = entity.Order,
			Rows = entity.Rows,
			Columns = entity.Columns,
			WidgetSpacing = entity.WidgetSpacing,
			WidgetBorderRadius = entity.WidgetBorderRadius,
			IsDefault = entity.IsDefault,
			// Normalized rather than passed through: a folder stored before folder views existed has no
			// id at all, and every client would otherwise need the same "absent means grid" rule.
			ViewId = BuiltInFolderViews.IsWidgetGrid(entity.ViewId)
				? BuiltInFolderViews.WidgetGrid
				: entity.ViewId!,
			ViewConfiguration = entity.ViewConfiguration
		};

		if (entity.ParentId.HasValue)
		{
			folder.ParentId = entity.ParentId.Value.ToString();
		}

		if (entity.BackgroundColor is not null)
		{
			folder.BackgroundColor = entity.BackgroundColor;
		}

		folder.Widgets.AddRange(entity.Widgets.Select(MapWidgetToDto));

		return folder;
	}

	public static FolderPlacement MapToPlacement(FolderEntity entity)
	{
		var placement = new FolderPlacement
		{
			Id = entity.Id.ToString(),
			Order = entity.Order,
			IsDefault = entity.IsDefault
		};

		if (entity.ParentId.HasValue)
		{
			placement.ParentId = entity.ParentId.Value.ToString();
		}

		return placement;
	}

	public static Widget MapWidgetToDto(WidgetEntity entity)
		=> new()
		{
			Id = entity.Id.ToString(),
			Type = entity.Type,
			PositionX = entity.PositionX,
			PositionY = entity.PositionY,
			Width = entity.Width,
			Height = entity.Height,
			Data = entity.Data,
			IsPinned = entity.IsPinned,
			PinScope = entity.PinScope
		};
}
