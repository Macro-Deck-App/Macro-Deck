using MacroDeck.Plugin.Protocol.Capabilities.LayoutProvider;
using MacroDeck.Sdk.Layouts;

namespace MacroDeck.Plugin.Hosting.Capabilities.LayoutProvider;

/// <summary>Turns an SDK layout descriptor into its wire shape and back.</summary>
internal static class LayoutDescriptorMapper
{
	public static LayoutDescriptorDto ToDto(LayoutDescriptor layout)
		=> new()
		{
			Id = layout.Id,
			Name = layout.Name,
			Regions = [.. layout.Regions.Select(ToDto)],
			Capabilities = layout.Capabilities is { } capabilities ? ToDto(capabilities) : null,
			Metadata = layout.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal)
		};

	public static LayoutDescriptor ToSdk(LayoutDescriptorDto dto)
		=> new(dto.Id,
			dto.Name,
			[.. dto.Regions.Select(ToSdk)],
			dto.Capabilities is { } capabilities
				? ToSdk(capabilities)
				: null,
			dto.Metadata);

	private static LayoutRegionDto ToDto(LayoutRegion region)
		=> new()
		{
			Id = region.Id,
			Kind = region.Kind,
			Name = region.Name,
			Grid = region.Grid is { } grid ? ToDto(grid) : null,
			Count = region.Count,
			Visuals = region.Visuals is { } visuals ? ToDto(visuals) : null,
			Extra = region.Extra
		};

	private static LayoutRegion ToSdk(LayoutRegionDto dto)
		=> new()
		{
			Id = dto.Id,
			Kind = dto.Kind,
			Name = dto.Name,
			Grid = dto.Grid is { } grid ? ToSdk(grid) : null,
			Count = dto.Count,
			Visuals = dto.Visuals is { } visuals ? ToSdk(visuals) : null,
			Extra = dto.Extra
		};

	private static LayoutGridDto ToDto(LayoutGrid grid)
		=> new()
		{
			Rows = grid.Rows,
			Columns = grid.Columns,
			IsConfigurable = grid.IsConfigurable,
			MinRows = grid.MinRows,
			MaxRows = grid.MaxRows,
			MinColumns = grid.MinColumns,
			MaxColumns = grid.MaxColumns,
			SupportsRuntimeResize = grid.SupportsRuntimeResize,
			KeySize = grid.KeySize is { } keySize
				? new LayoutKeySizeDto { Width = keySize.Width, Height = keySize.Height }
				: null
		};

	private static LayoutGrid ToSdk(LayoutGridDto dto)
		=> new()
		{
			Rows = dto.Rows,
			Columns = dto.Columns,
			IsConfigurable = dto.IsConfigurable,
			MinRows = dto.MinRows,
			MaxRows = dto.MaxRows,
			MinColumns = dto.MinColumns,
			MaxColumns = dto.MaxColumns,
			SupportsRuntimeResize = dto.SupportsRuntimeResize,
			KeySize = dto.KeySize is { } keySize ? new LayoutKeySize(keySize.Width, keySize.Height) : null
		};

	private static LayoutVisualCapabilitiesDto ToDto(LayoutVisualCapabilities visuals)
		=> new()
		{
			StaticIcons = visuals.StaticIcons,
			AnimatedIcons = visuals.AnimatedIcons,
			Borders = visuals.Borders,
			BackgroundColors = visuals.BackgroundColors,
			TextLabels = visuals.TextLabels,
			Transparency = visuals.Transparency,
			WidgetSpacing = visuals.WidgetSpacing,
			CornerRadius = visuals.CornerRadius,
			CustomFolderViews = visuals.CustomFolderViews,
			MaxUpdatesPerSecond = visuals.MaxUpdatesPerSecond
		};

	private static LayoutVisualCapabilities ToSdk(LayoutVisualCapabilitiesDto dto)
		=> new()
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

	private static LayoutCapabilitiesDto ToDto(LayoutCapabilities capabilities)
		=> new() { Visuals = capabilities.Visuals is { } visuals ? ToDto(visuals) : null, Extra = capabilities.Extra };

	private static LayoutCapabilities ToSdk(LayoutCapabilitiesDto dto)
		=> new() { Visuals = dto.Visuals is { } visuals ? ToSdk(visuals) : null, Extra = dto.Extra };
}
