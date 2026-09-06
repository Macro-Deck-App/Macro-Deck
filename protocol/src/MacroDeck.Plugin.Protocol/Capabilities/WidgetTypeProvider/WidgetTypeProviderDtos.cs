using MacroDeck.Localization;

namespace MacroDeck.Plugin.Protocol.Capabilities.WidgetTypeProvider;

/// <summary>
/// Mirrors the SDK's <c>WidgetTypeDescriptor</c>.
/// </summary>
public sealed record WidgetTypeDescriptorDto
{
	/// <summary>The provider-local widget type id. The host qualifies it with the owning plugin.</summary>
	public required string Id { get; init; }

	public required LocalizedText Name { get; init; }

	public LocalizedText? Description { get; init; }

	/// <summary>The stored configuration a newly added widget of this type starts with, as JSON object
	/// text. Absent reads as <c>{}</c>.</summary>
	public string? DefaultData { get; init; }

	/// <summary>A JSON Schema for the widget's stored data, as JSON text. Required when
	/// <see cref="HasConfiguration" /> is true.</summary>
	public string? DataSchema { get; init; }

	/// <summary>Whether the provider serves a <c>widget-config</c> configuration surface for this
	/// type.</summary>
	public bool HasConfiguration { get; init; }

	public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>Result of the <c>describe</c> operation.</summary>
public sealed record WidgetTypeProviderDescribePayload
{
	/// <summary>Human-readable provider name. Empty means the plugin's manifest name is used
	/// instead.</summary>
	public string ProviderName { get; init; } = string.Empty;

	public IReadOnlyList<WidgetTypeDescriptorDto> WidgetTypes { get; init; } = [];
}

/// <summary>Result of the <c>widget-types</c> operation: the provider's current catalog, which the host
/// reads to recover its catalog after a reconnect.</summary>
public sealed record WidgetTypeProviderWidgetTypesResult
{
	public IReadOnlyList<WidgetTypeDescriptorDto> WidgetTypes { get; init; } = [];
}
