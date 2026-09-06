using MacroDeck.Plugin.Protocol.Capabilities.LayoutProvider;
using MacroDeck.Sdk.Layouts;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

/// <summary>Turns the wire shape of a layout registration into the SDK descriptor the host works with.</summary>
public static class LayoutDescriptorMapper
{
	public static LayoutDescriptor ToDescriptor(LayoutDescriptorDto dto)
	{
		ArgumentNullException.ThrowIfNull(dto);

		return new LayoutDescriptor(dto.Id,
			dto.Name,
			[.. dto.Regions.Select(ToRegion)],
			ToCapabilities(dto.Capabilities),
			dto.Metadata);
	}

	private static LayoutRegion ToRegion(LayoutRegionDto dto)
		=> new()
		{
			Id = dto.Id,
			Kind = dto.Kind,
			Name = dto.Name,
			Grid = ToGrid(dto.Grid),
			Count = dto.Count,
			Visuals = ToVisuals(dto.Visuals),
			Extra = dto.Extra
		};

	private static LayoutGrid? ToGrid(LayoutGridDto? dto)
		=> dto is null
			? null
			: new LayoutGrid
			{
				Rows = dto.Rows,
				Columns = dto.Columns,
				IsConfigurable = dto.IsConfigurable,
				MinRows = dto.MinRows,
				MaxRows = dto.MaxRows,
				MinColumns = dto.MinColumns,
				MaxColumns = dto.MaxColumns,
				SupportsRuntimeResize = dto.SupportsRuntimeResize,
				KeySize = dto.KeySize is null ? null : new LayoutKeySize(dto.KeySize.Width, dto.KeySize.Height)
			};

	private static LayoutVisualCapabilities? ToVisuals(LayoutVisualCapabilitiesDto? dto)
		=> dto is null
			? null
			: new LayoutVisualCapabilities
			{
				StaticIcons = dto.StaticIcons,
				AnimatedIcons = dto.AnimatedIcons,
				Borders = dto.Borders,
				BackgroundColors = dto.BackgroundColors,
				TextLabels = dto.TextLabels,
				Transparency = dto.Transparency,
				WidgetSpacing = dto.WidgetSpacing,
				CornerRadius = dto.CornerRadius,
				CustomFolderViews = dto.CustomFolderViews,
				MaxUpdatesPerSecond = dto.MaxUpdatesPerSecond
			};

	private static LayoutCapabilities? ToCapabilities(LayoutCapabilitiesDto? dto)
		=> dto is null ? null : new LayoutCapabilities { Visuals = ToVisuals(dto.Visuals), Extra = dto.Extra };
}
