using MacroDeck.Plugin.Protocol.Capabilities.WidgetTypeProvider;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// Arguments for <c>host.invoke</c> against <see cref="HostApis.WidgetTypes" />'s <c>register</c>
/// operation. The providing plugin is never named here - the host takes it from the authenticated session,
/// so one plugin cannot register a widget type in another's name, and cannot claim an id inside another's
/// namespace.
/// </summary>
public sealed record WidgetTypesRegisterArguments
{
	public required WidgetTypeDescriptorDto WidgetType { get; init; }
}

/// <summary>Result of the <c>register</c> operation: the identity the host assigned.</summary>
public sealed record WidgetTypesRegisterResult
{
	/// <summary>The qualified id - <c>provider::widget-type</c> - a widget stores as its type.</summary>
	public required string WidgetTypeId { get; init; }

	/// <summary>The integration or plugin that owns the type.</summary>
	public required string ProviderId { get; init; }
}

/// <summary>Arguments for the <c>unregister</c> operation.</summary>
public sealed record WidgetTypesUnregisterArguments
{
	/// <summary>The provider-local widget type id.</summary>
	public required string WidgetTypeId { get; init; }
}
