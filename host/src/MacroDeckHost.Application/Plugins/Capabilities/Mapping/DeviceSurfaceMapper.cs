using MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;
using MacroDeck.Sdk.Devices;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

/// <summary>Turns a projected device surface into the wire shape a remote provider renders from.</summary>
public static class DeviceSurfaceMapper
{
	public static DeviceSurfaceDto ToDto(DeviceSurface surface)
	{
		ArgumentNullException.ThrowIfNull(surface);

		return new DeviceSurfaceDto
		{
			Revision = surface.Revision,
			Profile = surface.Profile is { } profile
				? new DeviceSurfaceProfileDto { Id = profile.Id, Name = profile.Name }
				: null,
			Folder = surface.Folder is { } folder
				? new DeviceSurfaceFolderDto
				{
					Id = folder.Id, Name = folder.Name, ParentId = folder.ParentId, IsRoot = folder.IsRoot
				}
				: null,
			Layout = new DeviceSurfaceLayoutDto
			{
				Rows = surface.Layout.Rows,
				Columns = surface.Layout.Columns,
				WidgetSpacing = surface.Layout.WidgetSpacing,
				WidgetBorderRadius = surface.Layout.WidgetBorderRadius,
				BackgroundColor = surface.Layout.BackgroundColor,
				LayoutReference = surface.Layout.LayoutReference
			},
			Widgets = [.. surface.Widgets.Select(ToDto)]
		};
	}

	private static DeviceSurfaceWidgetDto ToDto(DeviceSurfaceWidget widget)
		=> new()
		{
			Id = widget.Id,
			Type = widget.Type,
			PositionX = widget.PositionX,
			PositionY = widget.PositionY,
			Width = widget.Width,
			Height = widget.Height,
			IsPinned = widget.IsPinned,
			StateId = widget.StateId,
			StateLabel = widget.StateLabel,
			Appearance = widget.Appearance is { } appearance
				? new DeviceSurfaceAppearanceDto
				{
					Label = appearance.Label,
					LabelColor = appearance.LabelColor,
					BackgroundColor = appearance.BackgroundColor,
					IconId = appearance.IconId,
					IconVersion = appearance.IconVersion,
					HasProviderIcon = appearance.HasProviderIcon,
					IconFit = appearance.IconFit,
					IconZoom = appearance.IconZoom,
					IconOffsetX = appearance.IconOffsetX,
					IconOffsetY = appearance.IconOffsetY,
					FontSize = appearance.FontSize,
					TextAlign = appearance.TextAlign,
					LabelPosition = appearance.LabelPosition,
					Extra = appearance.Extra
				}
				: null,
			// Member names, not numbers: a provider built against a later SDK must be able to recognise a
			// kind by name, and one built against an earlier one must be able to ignore an unknown name.
			SupportedInteractions = [.. widget.SupportedInteractions.Select(kind => kind.ToString())]
		};
}
