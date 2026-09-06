namespace MacroDeck.Plugin.Protocol.Capabilities.LayoutProvider;

/// <summary>Mirrors the SDK's <c>LayoutKeySize</c>.</summary>
public sealed record LayoutKeySizeDto
{
	public required int Width { get; init; }

	public required int Height { get; init; }
}

/// <summary>Mirrors the SDK's <c>LayoutVisualCapabilities</c>.</summary>
public sealed record LayoutVisualCapabilitiesDto
{
	public bool StaticIcons { get; init; }

	public bool AnimatedIcons { get; init; }

	public bool Borders { get; init; }

	public bool BackgroundColors { get; init; }

	public bool TextLabels { get; init; }

	public bool Transparency { get; init; }

	public bool WidgetSpacing { get; init; }

	public bool CornerRadius { get; init; }

	/// <summary>Whether the surface can render a folder view at all - see the SDK's
	/// <c>LayoutVisualCapabilities.CustomFolderViews</c>.</summary>
	public bool CustomFolderViews { get; init; }

	public int? MaxUpdatesPerSecond { get; init; }
}

/// <summary>Mirrors the SDK's <c>LayoutGrid</c>.</summary>
public sealed record LayoutGridDto
{
	public required int Rows { get; init; }

	public required int Columns { get; init; }

	public bool IsConfigurable { get; init; }

	public int MinRows { get; init; } = 1;

	public int MaxRows { get; init; } = 1;

	public int MinColumns { get; init; } = 1;

	public int MaxColumns { get; init; } = 1;

	public bool SupportsRuntimeResize { get; init; }

	public LayoutKeySizeDto? KeySize { get; init; }
}

/// <summary>
/// Mirrors the SDK's <c>LayoutRegion</c>. <see cref="Kind" /> is an open string, not an enum: a reader
/// that does not recognise a kind must skip that region and keep the rest of the descriptor, so a region
/// type added later travels through an older peer without discarding the layout it belongs to.
/// </summary>
public sealed record LayoutRegionDto
{
	public required string Id { get; init; }

	/// <summary>One of the SDK's <c>LayoutRegionKinds</c> values, or a kind this version does not know.</summary>
	public required string Kind { get; init; }

	public string? Name { get; init; }

	/// <summary>Present when <see cref="Kind" /> is <c>grid</c>.</summary>
	public LayoutGridDto? Grid { get; init; }

	public int Count { get; init; }

	public LayoutVisualCapabilitiesDto? Visuals { get; init; }

	public IReadOnlyDictionary<string, string> Extra { get; init; }
		= new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>Mirrors the SDK's <c>LayoutCapabilities</c>.</summary>
public sealed record LayoutCapabilitiesDto
{
	public LayoutVisualCapabilitiesDto? Visuals { get; init; }

	public IReadOnlyDictionary<string, string> Extra { get; init; }
		= new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>Mirrors the SDK's <c>LayoutDescriptor</c>.</summary>
public sealed record LayoutDescriptorDto
{
	/// <summary>The provider-local layout id. The host qualifies it as <c>provider::id</c>.</summary>
	public required string Id { get; init; }

	public required string Name { get; init; }

	public required IReadOnlyList<LayoutRegionDto> Regions { get; init; }

	public LayoutCapabilitiesDto? Capabilities { get; init; }

	public IReadOnlyDictionary<string, string> Metadata { get; init; }
		= new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>The full result of the <c>layout-provider</c> capability's <c>describe</c> operation.</summary>
public sealed record LayoutProviderDescribePayload
{
	public required string ProviderName { get; init; }

	public required IReadOnlyList<LayoutDescriptorDto> Layouts { get; init; }
}

/// <summary>
/// Result of the <c>layouts</c> operation: the same layout list <c>describe</c> carries, exposed as its
/// own narrow round trip - see <c>DeviceProviderDevicesResult</c>'s identical remarks.
/// </summary>
public sealed record LayoutProviderLayoutsResult
{
	public required IReadOnlyList<LayoutDescriptorDto> Layouts { get; init; }
}
