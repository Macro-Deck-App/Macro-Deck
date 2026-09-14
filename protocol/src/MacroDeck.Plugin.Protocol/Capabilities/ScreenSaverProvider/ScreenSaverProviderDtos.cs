using MacroDeck.Localization;

namespace MacroDeck.Plugin.Protocol.Capabilities.ScreenSaverProvider;

/// <summary>Mirrors the SDK's <c>ScreenSaverDescriptor</c>.</summary>
public sealed record ScreenSaverDescriptorDto
{
	/// <summary>The provider-local screensaver id. The host qualifies it with the owning plugin.</summary>
	public required string Id { get; init; }

	public required LocalizedText Name { get; init; }

	public LocalizedText? Description { get; init; }

	/// <summary>Whether the provider serves a <c>screensaver-config</c> configuration surface for this
	/// screensaver.</summary>
	public bool HasConfiguration { get; init; }

	/// <summary>Whether input reaches the tree. Absent reads as false: any input dismisses.</summary>
	public bool Interactive { get; init; }

	public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>Result of the <c>describe</c> operation.</summary>
public sealed record ScreenSaverProviderDescribePayload
{
	/// <summary>Human-readable provider name. Empty means the plugin's manifest name is used
	/// instead.</summary>
	public string ProviderName { get; init; } = string.Empty;

	public IReadOnlyList<ScreenSaverDescriptorDto> ScreenSavers { get; init; } = [];
}

/// <summary>Result of the <c>screensavers</c> operation: the provider's current catalog, which the host
/// reads to recover its screensavers after a reconnect.</summary>
public sealed record ScreenSaverProviderScreenSaversResult
{
	public IReadOnlyList<ScreenSaverDescriptorDto> ScreenSavers { get; init; } = [];
}
