using MacroDeck.Plugin.Protocol.Capabilities.VirtualProfiles;
using MacroDeck.Sdk.Profiles;

namespace MacroDeck.Plugin.Hosting.Capabilities.VirtualProfiles;

/// <summary>Maps the SDK's virtual-profile types to their wire DTOs.</summary>
internal static class VirtualProfileDescriptorMapper
{
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
