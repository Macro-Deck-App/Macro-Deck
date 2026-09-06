namespace MacroDeck.Plugin.Protocol.Capabilities.VirtualProfiles;

/// <summary>Mirrors the SDK's <c>ProfileLayout</c>. <see cref="Kind" /> is a string, not the SDK's
/// <c>LayoutKind</c> enum - see <c>ActionParameterDto</c>'s remarks.</summary>
public sealed record ProfileLayoutDto
{
	/// <summary>One of the SDK's <c>LayoutKind</c> member names. Only "Grid" exists today.</summary>
	public required string Kind { get; init; }

	public required int Rows { get; init; }

	public required int Columns { get; init; }

	public bool RowsLocked { get; init; }

	public bool ColumnsLocked { get; init; }
}

/// <summary>Mirrors the SDK's <c>VirtualWidgetDescriptor</c>.</summary>
public sealed record VirtualWidgetDescriptorDto
{
	public required string Id { get; init; }

	/// <summary>A widget type name, e.g. "ActionButton", "MusicPlayer", "Slider".</summary>
	public required string Type { get; init; }

	public required int PositionX { get; init; }

	public required int PositionY { get; init; }

	public int Width { get; init; } = 1;

	public int Height { get; init; } = 1;

	public string? Data { get; init; }
}

/// <summary>Mirrors the SDK's <c>VirtualFolderDescriptor</c>.</summary>
public sealed record VirtualFolderDescriptorDto
{
	public required string Id { get; init; }

	public required string Name { get; init; }

	public required IReadOnlyList<VirtualWidgetDescriptorDto> Widgets { get; init; }

	public string? ParentId { get; init; }

	public int Order { get; init; }
}

/// <summary>Mirrors the SDK's <c>VirtualProfileDescriptor</c>.</summary>
public sealed record VirtualProfileDescriptorDto
{
	public required string Id { get; init; }

	public required string Name { get; init; }

	public required ProfileLayoutDto Layout { get; init; }

	public required IReadOnlyList<VirtualFolderDescriptorDto> Folders { get; init; }
}

/// <summary>The full result of the <c>virtual-profiles</c> capability's <c>describe</c> operation -
/// what <c>RemotePluginSnapshotRefresher</c> folds into the snapshot's provider name and profile list.</summary>
public sealed record VirtualProfilesDescribePayload
{
	public required string ProviderName { get; init; }

	public required IReadOnlyList<VirtualProfileDescriptorDto> Profiles { get; init; }
}

/// <summary>
/// Result of the <c>profiles</c> operation: the same profile list <c>describe</c> carries, exposed as
/// its own narrow round trip - see <c>MusicPlayerInstancesResult</c>'s identical remarks.
/// </summary>
public sealed record VirtualProfilesResult
{
	public required IReadOnlyList<VirtualProfileDescriptorDto> Profiles { get; init; }
}

/// <summary>
/// Arguments for the <c>widget-interaction</c> operation, mirroring
/// <c>IProfileProvider.HandleWidgetInteractionAsync</c>'s parameters plus <c>WidgetInteraction</c>'s
/// single field. <see cref="ProfileId" /> is carried faithfully rather than re-derived host-side: the
/// host's own in-process <c>ProfileRegistry</c> always passes <see cref="string.Empty" /> for it (see
/// <c>ProfileRegistry.RouteAsync</c>), and the remote path must not diverge from that by trying to
/// "fix" or backfill it.
/// </summary>
public sealed record WidgetInteractionArguments
{
	public required string ProfileId { get; init; }

	public required string FolderId { get; init; }

	public required string WidgetId { get; init; }

	/// <summary>Mirrors the action-button trigger names, e.g. "press", "release".</summary>
	public required string TriggerType { get; init; }
}
