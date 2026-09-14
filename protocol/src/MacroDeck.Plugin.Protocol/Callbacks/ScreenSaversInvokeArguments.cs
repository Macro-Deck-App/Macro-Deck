using MacroDeck.Plugin.Protocol.Capabilities.ScreenSaverProvider;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// Arguments for <c>host.invoke</c> against <see cref="HostApis.ScreenSavers" />'s <c>register</c>
/// operation. The providing plugin is never named here - the host takes it from the authenticated session,
/// so one plugin cannot register a screensaver in another's name.
/// </summary>
public sealed record ScreenSaversRegisterArguments
{
	public required ScreenSaverDescriptorDto ScreenSaver { get; init; }
}

/// <summary>Result of the <c>register</c> operation: the identity the host assigned.</summary>
public sealed record ScreenSaversRegisterResult
{
	/// <summary>The qualified id - <c>provider::screensaver</c> - a device stores to select it.</summary>
	public required string ScreenSaverId { get; init; }

	/// <summary>The integration or plugin that owns the screensaver.</summary>
	public required string ProviderId { get; init; }
}

/// <summary>Arguments for the <c>unregister</c> operation.</summary>
public sealed record ScreenSaversUnregisterArguments
{
	/// <summary>The provider-local screensaver id.</summary>
	public required string ScreenSaverId { get; init; }
}
