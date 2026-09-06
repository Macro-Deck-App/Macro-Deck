namespace MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;

/// <summary>Arguments for the <c>device-provider</c> capability's <c>session.open</c> operation.</summary>
public sealed record DeviceSessionOpenArguments
{
	/// <summary>Id the host assigns this session; referenced by <c>session.surface</c>, <c>session.close</c>
	/// and every <c>devices</c> host-api call the plugin makes for it.</summary>
	public required string SessionId { get; init; }

	/// <summary>The host's global device id.</summary>
	public required string DeviceId { get; init; }

	/// <summary>The provider-local id the device was registered under.</summary>
	public required string ProviderDeviceId { get; init; }
}

/// <summary>Result of the <c>session.open</c> operation.</summary>
public sealed record DeviceSessionOpenResult
{
	public required bool Accepted { get; init; }

	/// <summary>Set when <see cref="Accepted" /> is false.</summary>
	public string? RejectionReason { get; init; }
}

/// <summary>Arguments for the <c>device-provider</c> capability's <c>session.surface</c> operation.</summary>
public sealed record DeviceSessionSurfaceArguments
{
	public required string SessionId { get; init; }

	public required DeviceSurfaceDto Surface { get; init; }
}

/// <summary>Arguments for the <c>device-provider</c> capability's <c>session.close</c> operation.</summary>
public sealed record DeviceSessionCloseArguments
{
	public required string SessionId { get; init; }

	public string? Reason { get; init; }
}

/// <summary>Mirrors the SDK's <c>DeviceSurface</c>.</summary>
public sealed record DeviceSurfaceDto
{
	public required long Revision { get; init; }

	public DeviceSurfaceProfileDto? Profile { get; init; }

	public DeviceSurfaceFolderDto? Folder { get; init; }

	public required DeviceSurfaceLayoutDto Layout { get; init; }

	public required IReadOnlyList<DeviceSurfaceWidgetDto> Widgets { get; init; }
}

/// <summary>Mirrors the SDK's <c>DeviceSurfaceProfile</c>.</summary>
public sealed record DeviceSurfaceProfileDto
{
	public required string Id { get; init; }

	public required string Name { get; init; }
}

/// <summary>Mirrors the SDK's <c>DeviceSurfaceFolder</c>.</summary>
public sealed record DeviceSurfaceFolderDto
{
	public required string Id { get; init; }

	public required string Name { get; init; }

	public string? ParentId { get; init; }

	public required bool IsRoot { get; init; }
}

/// <summary>Mirrors the SDK's <c>DeviceSurfaceLayout</c>.</summary>
public sealed record DeviceSurfaceLayoutDto
{
	public required int Rows { get; init; }

	public required int Columns { get; init; }

	public int WidgetSpacing { get; init; }

	public int WidgetBorderRadius { get; init; }

	public string? BackgroundColor { get; init; }

	public string? LayoutReference { get; init; }
}

/// <summary>Mirrors the SDK's <c>DeviceSurfaceWidget</c>. <see cref="SupportedInteractions" /> carries
/// the SDK's <c>DeviceInteractionKind</c> member names as strings - see <c>DeviceDescriptorDto</c>'s
/// remarks on <c>Presence</c>.</summary>
public sealed record DeviceSurfaceWidgetDto
{
	public required string Id { get; init; }

	public required string Type { get; init; }

	public required int PositionX { get; init; }

	public required int PositionY { get; init; }

	public required int Width { get; init; }

	public required int Height { get; init; }

	public bool IsPinned { get; init; }

	public string? StateId { get; init; }

	public string? StateLabel { get; init; }

	public DeviceSurfaceAppearanceDto? Appearance { get; init; }

	public IReadOnlyList<string> SupportedInteractions { get; init; } = [];
}

/// <summary>Mirrors the SDK's <c>DeviceSurfaceAppearance</c>.</summary>
public sealed record DeviceSurfaceAppearanceDto
{
	public string? Label { get; init; }

	public string? LabelColor { get; init; }

	public string? BackgroundColor { get; init; }

	public string? IconId { get; init; }

	/// <summary>The icon's content identity - see the SDK's <c>DeviceSurfaceAppearance.IconVersion</c>.</summary>
	public string? IconVersion { get; init; }

	/// <summary>See the SDK's <c>DeviceSurfaceAppearance.HasProviderIcon</c>.</summary>
	public bool HasProviderIcon { get; init; }

	public string? IconFit { get; init; }

	public double? IconZoom { get; init; }

	public double? IconOffsetX { get; init; }

	public double? IconOffsetY { get; init; }

	public int? FontSize { get; init; }

	public string? TextAlign { get; init; }

	public string? LabelPosition { get; init; }

	public IReadOnlyDictionary<string, string> Extra { get; init; }
		= new Dictionary<string, string>(StringComparer.Ordinal);
}
