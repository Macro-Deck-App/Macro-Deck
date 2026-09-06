namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// Arguments for <c>host.invoke</c> against <see cref="HostApis.Widgets" />'s <c>apply</c> operation,
/// protocol v1 shape - the shape every plugin already compiled against an earlier SDK sends today, since
/// <c>widgets/apply</c> used to serialize <c>MacroDeck.Sdk.Widgets.WidgetAppearanceRequest</c> straight
/// onto the wire with no protocol DTO of its own. <see cref="State" /> stays a plain int here rather than
/// becoming a string: <c>PluginProtocolJson.Options</c> carries no <c>JsonStringEnumConverter</c>, so an
/// int is exactly what an already-shipped plugin's <c>WidgetStateSelector</c> already serializes as, and
/// this shape exists so those bytes keep deserializing exactly as before.
/// </summary>
public sealed record WidgetsApplyArgumentsV1
{
	public required string WidgetId { get; init; }

	public required WidgetAppearancePatchDto Patch { get; init; }

	public int State { get; init; }

	/// <summary>
	/// <c>MacroDeck.Sdk.Widgets.WidgetAppearanceProperty</c> values, as plain ints for the same reason
	/// <see cref="State" /> is - see the type remarks.
	/// </summary>
	public IReadOnlyCollection<int> ClearProperties { get; init; } = [];
}

/// <summary>
/// Arguments for <c>host.invoke</c> against <see cref="HostApis.Widgets" />'s <c>apply</c> operation,
/// protocol v2 shape: stable state ids instead of the four-way <c>WidgetStateSelector</c>.
/// </summary>
public sealed record WidgetsApplyArgumentsV2
{
	public required string WidgetId { get; init; }

	public required WidgetAppearancePatchDto Patch { get; init; }

	/// <summary>Real state ids, or the <c>WidgetStates.Current</c>/<c>WidgetStates.All</c> sentinels.</summary>
	public IReadOnlyCollection<string> StateIds { get; init; } = [];

	/// <summary>See <see cref="WidgetsApplyArgumentsV1.ClearProperties" />.</summary>
	public IReadOnlyCollection<int> ClearProperties { get; init; } = [];
}

/// <summary>
/// Arguments for <c>host.invoke</c> against <see cref="HostApis.Widgets" />'s <c>invalidate-icon</c>
/// operation. <see cref="ActionId" /> is the action's declared local id, not a configured instance - the
/// host qualifies it with the calling connection's plugin id, which is also the trust boundary.
/// </summary>
public sealed record WidgetsInvalidateIconArguments
{
	public required string ActionId { get; init; }
}

/// <summary>Wire mirror of <c>MacroDeck.Sdk.Widgets.WidgetAppearancePatch</c> - every property is
/// nullable with the same "leave as it is" meaning; see the SDK type's own remarks.</summary>
public sealed record WidgetAppearancePatchDto
{
	public string? Label { get; init; }

	public string? BackgroundColor { get; init; }

	public string? LabelColor { get; init; }

	public string? IconId { get; init; }

	public string? IconFit { get; init; }

	public double? IconZoom { get; init; }

	public double? IconOffsetX { get; init; }

	public double? IconOffsetY { get; init; }

	public double? IconOpacity { get; init; }

	public string? FontFaceId { get; init; }

	public double? FontSize { get; init; }

	public string? TextAlign { get; init; }

	public string? LabelPosition { get; init; }

	public string? BorderStyle { get; init; }

	public string? BorderColor { get; init; }
}

/// <summary>
/// One entry of the <c>host.state</c> push for <see cref="HostApis.Widgets" />, protocol v1 shape:
/// <see cref="HasOnOffStates" /> instead of a state list - see
/// <c>MacroDeckHost.Plugins.Capabilities.Callbacks.WidgetStateWireCompatibility</c> for how the host
/// derives it from the widget's real state list.
/// </summary>
public sealed record WidgetTargetInfoDtoV1
{
	public required string Id { get; init; }

	public required string Label { get; init; }

	public required string Location { get; init; }

	public required string Type { get; init; }

	public bool HasOnOffStates { get; init; }

	/// <summary>See <see cref="WidgetsApplyArgumentsV1.ClearProperties" /> for why this is <c>int</c>, not the SDK enum.</summary>
	public IReadOnlyCollection<int> AppearanceProperties { get; init; } = [];
}

/// <summary>
/// One entry of the <c>host.state</c> push for <see cref="HostApis.Widgets" />, protocol v2 shape: the
/// full state list and the id of whichever one is current, instead of the collapsed on/off flag.
/// </summary>
public sealed record WidgetTargetInfoDtoV2
{
	public required string Id { get; init; }

	public required string Label { get; init; }

	public required string Location { get; init; }

	public required string Type { get; init; }

	public IReadOnlyList<WidgetStateInfoDto> States { get; init; } = [];

	public string? CurrentStateId { get; init; }

	/// <summary>See <see cref="WidgetsApplyArgumentsV1.ClearProperties" /> for why this is <c>int</c>, not the SDK enum.</summary>
	public IReadOnlyCollection<int> AppearanceProperties { get; init; } = [];
}

/// <summary>Wire mirror of <c>MacroDeck.Sdk.Widgets.WidgetStateInfo</c>.</summary>
public sealed record WidgetStateInfoDto
{
	public required string Id { get; init; }

	public required string Label { get; init; }
}
