using MacroDeck.Localization;
using System.Text.Json;

namespace MacroDeck.Plugin.Protocol.Capabilities.Actions;

/// <summary>One option of a <see cref="ActionParameterDto" /> with a fixed or dynamically resolved choice list.</summary>
public sealed record ActionParameterOptionDto
{
	public required string Value { get; init; }

	public LocalizedText? Label { get; init; }

	public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Shows a parameter only while a sibling holds one of the listed values. Mirrors the host's
/// <c>ParameterVisibility</c>.
/// </summary>
public sealed record ParameterVisibilityDto
{
	public required string ParameterName { get; init; }

	public required IReadOnlyList<string> Values { get; init; }
}

/// <summary>
/// Faithful recursive mirror of the SDK's <c>ActionParameter</c>. <see cref="Type" /> is a string, not
/// the SDK's enum: <c>PluginProtocolJson.Options</c> carries no <c>JsonStringEnumConverter</c>, so an
/// enum-typed property would serialize as an integer and make its declaration order part of the wire
/// contract - a test asserts this project has no enum-typed DTO property, and this keeps it passing.
/// </summary>
public sealed record ActionParameterDto
{
	public required string Name { get; init; }

	/// <summary>One of the SDK's <c>ActionParameterType</c> member names, e.g. "String", "DynamicChoice".</summary>
	public required string Type { get; init; }

	public LocalizedText? Label { get; init; }

	public LocalizedText? Description { get; init; }

	public LocalizedText? Placeholder { get; init; }

	public bool AutoPrefixHttps { get; init; }

	/// <summary>The literal default value, in whatever shape the parameter type uses on the wire.</summary>
	public JsonElement? DefaultValue { get; init; }

	public bool Required { get; init; }

	public bool Multiline { get; init; }

	public bool SupportsReset { get; init; }

	public bool LiteralOnly { get; init; }

	public string? ValidationRegex { get; init; }

	public int? MaxLength { get; init; }

	public double? Min { get; init; }

	public double? Max { get; init; }

	public double? Step { get; init; }

	public bool ShowSlider { get; init; }

	public IReadOnlyList<ActionParameterOptionDto>? Options { get; init; }

	public bool DynamicOptions { get; init; }

	public string? OptionsSourceId { get; init; }

	/// <summary>Whether a widget-target parameter offers the "this widget" sentinel. Optional and
	/// additive: a plugin that predates it sends nothing and keeps the historic default of <c>true</c>.</summary>
	public bool? AllowSelf { get; init; }

	/// <summary>Widget type names a widget-target parameter is limited to. Empty or absent means every
	/// widget.</summary>
	public IReadOnlyList<string>? WidgetTypes { get; init; }

	public IReadOnlyList<string>? FileExtensions { get; init; }

	public string? Language { get; init; }

	/// <summary>Present only for <c>Object</c>-typed parameters.</summary>
	public IReadOnlyList<ActionParameterDto>? Children { get; init; }

	/// <summary>Present only for <c>Array</c>-typed parameters.</summary>
	public ActionParameterDto? ItemTemplate { get; init; }

	public ParameterVisibilityDto? VisibleWhen { get; init; }
}
