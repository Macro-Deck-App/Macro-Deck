using MacroDeck.Plugin.Protocol.Capabilities.VirtualProfiles;
using MacroDeck.Sdk.Profiles;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

public static class VirtualProfileCatalogMapper
{
	public static ProfileLayout ToDomain(ProfileLayoutDto dto)
	{
		if (!Enum.TryParse<LayoutKind>(dto.Kind, false, out var kind))
		{
			throw new InvalidOperationException($"Unknown virtual profile layout kind '{dto.Kind}'.");
		}

		return new ProfileLayout(kind, dto.Rows, dto.Columns, dto.RowsLocked, dto.ColumnsLocked);
	}

	public static VirtualWidgetDescriptor ToDomain(VirtualWidgetDescriptorDto dto)
	{
		return new VirtualWidgetDescriptor(dto.Id,
			dto.Type,
			dto.PositionX,
			dto.PositionY,
			dto.Width,
			dto.Height,
			dto.Data);
	}

	public static VirtualFolderDescriptor ToDomain(VirtualFolderDescriptorDto dto)
	{
		return new VirtualFolderDescriptor(dto.Id,
			dto.Name,
			[.. dto.Widgets.Select(ToDomain)],
			dto.ParentId,
			dto.Order);
	}

	public static VirtualProfileDescriptor ToDomain(VirtualProfileDescriptorDto dto)
	{
		return new VirtualProfileDescriptor(dto.Id, dto.Name, ToDomain(dto.Layout), [.. dto.Folders.Select(ToDomain)]);
	}

	public static ProfileLayoutDto ToDto(ProfileLayout layout)
	{
		return new ProfileLayoutDto
		{
			Kind = layout.Kind.ToString(),
			Rows = layout.Rows,
			Columns = layout.Columns,
			RowsLocked = layout.RowsLocked,
			ColumnsLocked = layout.ColumnsLocked
		};
	}

	public static VirtualWidgetDescriptorDto ToDto(VirtualWidgetDescriptor widget)
	{
		return new VirtualWidgetDescriptorDto
		{
			Id = widget.Id,
			Type = widget.Type,
			PositionX = widget.PositionX,
			PositionY = widget.PositionY,
			Width = widget.Width,
			Height = widget.Height,
			Data = widget.Data
		};
	}

	public static VirtualFolderDescriptorDto ToDto(VirtualFolderDescriptor folder)
	{
		return new VirtualFolderDescriptorDto
		{
			Id = folder.Id,
			Name = folder.Name,
			Widgets = [.. folder.Widgets.Select(ToDto)],
			ParentId = folder.ParentId,
			Order = folder.Order
		};
	}

	public static VirtualProfileDescriptorDto ToDto(VirtualProfileDescriptor profile)
	{
		return new VirtualProfileDescriptorDto
		{
			Id = profile.Id,
			Name = profile.Name,
			Layout = ToDto(profile.Layout),
			Folders = [.. profile.Folders.Select(ToDto)]
		};
	}
}
