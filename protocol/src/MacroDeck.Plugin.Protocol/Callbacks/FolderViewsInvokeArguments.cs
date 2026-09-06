using MacroDeck.Plugin.Protocol.Capabilities.FolderViewProvider;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// Arguments for <c>host.invoke</c> against <see cref="HostApis.FolderViews" />'s <c>register</c>
/// operation. The providing plugin is never named here - the host takes it from the authenticated session,
/// so one plugin cannot register a folder view in another's name, and cannot claim an id inside another's
/// namespace.
/// </summary>
public sealed record FolderViewsRegisterArguments
{
	public required FolderViewDescriptorDto FolderView { get; init; }
}

/// <summary>Result of the <c>register</c> operation: the identity the host assigned.</summary>
public sealed record FolderViewsRegisterResult
{
	/// <summary>The qualified id - <c>provider::view</c> - a folder stores to select this view.</summary>
	public required string FolderViewId { get; init; }

	/// <summary>The integration or plugin that owns the view.</summary>
	public required string ProviderId { get; init; }
}

/// <summary>Arguments for the <c>unregister</c> operation.</summary>
public sealed record FolderViewsUnregisterArguments
{
	/// <summary>The provider-local folder view id.</summary>
	public required string FolderViewId { get; init; }
}
