using MacroDeck.Plugin.Protocol.Capabilities.LayoutProvider;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// Arguments for <c>host.invoke</c> against <see cref="HostApis.Layouts" />'s <c>register</c> operation.
/// The providing plugin is never named here - the host takes it from the authenticated session, so one
/// plugin cannot register a layout in another's name, and cannot claim an id inside another's namespace.
/// </summary>
public sealed record LayoutsRegisterArguments
{
	public required LayoutDescriptorDto Layout { get; init; }
}

/// <summary>Result of the <c>register</c> operation: the identity the host assigned.</summary>
public sealed record LayoutsRegisterResult
{
	/// <summary>The qualified id - <c>provider::layout</c> - a device's layout reference must carry.</summary>
	public required string LayoutId { get; init; }

	/// <summary>The integration or plugin that owns the layout.</summary>
	public required string ProviderId { get; init; }
}

/// <summary>Arguments for the <c>unregister</c> operation.</summary>
public sealed record LayoutsUnregisterArguments
{
	/// <summary>The provider-local layout id.</summary>
	public required string LayoutId { get; init; }
}
