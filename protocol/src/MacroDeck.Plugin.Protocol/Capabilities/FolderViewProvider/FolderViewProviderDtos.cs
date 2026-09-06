using MacroDeck.Localization;

namespace MacroDeck.Plugin.Protocol.Capabilities.FolderViewProvider;

/// <summary>
/// Mirrors the SDK's <c>FolderViewDescriptor</c>. <see cref="Navigation" /> is an open string, not an
/// enum: a reader that does not recognise a value treats it as the default, which shows the way back -
/// the safe answer, and the reason a navigation mode added later can travel through an older peer without
/// trapping anyone in a view.
/// </summary>
public sealed record FolderViewDescriptorDto
{
	/// <summary>The provider-local view id. The host qualifies it with the owning plugin.</summary>
	public required string Id { get; init; }

	public required LocalizedText Name { get; init; }

	public LocalizedText? Description { get; init; }

	/// <summary>See the SDK's <c>FolderViewNavigation</c>. Absent reads as <c>default</c>.</summary>
	public string? Navigation { get; init; }

	/// <summary>Whether the provider serves a <c>folder-view-config</c> configuration surface for this
	/// view.</summary>
	public bool HasConfiguration { get; init; }

	public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>Result of the <c>describe</c> operation.</summary>
public sealed record FolderViewProviderDescribePayload
{
	/// <summary>Human-readable provider name. Empty means the plugin's manifest name is used
	/// instead.</summary>
	public string ProviderName { get; init; } = string.Empty;

	public IReadOnlyList<FolderViewDescriptorDto> FolderViews { get; init; } = [];
}

/// <summary>Result of the <c>folder-views</c> operation: the provider's current catalog, which the host
/// reads to recover its view after a reconnect.</summary>
public sealed record FolderViewProviderFolderViewsResult
{
	public IReadOnlyList<FolderViewDescriptorDto> FolderViews { get; init; } = [];
}
