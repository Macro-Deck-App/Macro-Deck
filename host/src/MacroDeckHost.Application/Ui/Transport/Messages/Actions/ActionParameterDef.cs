using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Actions;

public class ActionParameterDef
{
	public string Name { get; set; } = string.Empty;

	[JsonConverter(typeof(JsonStringEnumConverter))]
	public ActionParameterType Type { get; set; }

	public LocalizedText Description { get; set; }

	public LocalizedText Label { get; set; }

	public LocalizedText Placeholder { get; set; }

	public bool AutoPrefixHttps { get; set; }

	public JsonElement? DefaultValue { get; set; }

	public bool Required { get; set; }

	public bool Multiline { get; set; }

	public bool SupportsReset { get; set; }
	public bool LiteralOnly { get; set; }

	public string? ValidationRegex { get; set; }

	public int? MaxLength { get; set; }

	public double? Min { get; set; }

	public double? Max { get; set; }

	public double? Step { get; set; }

	public bool ShowSlider { get; set; }

	public List<ActionParameterOptionDto>? Options { get; set; }

	public bool DynamicOptions { get; set; }

	public string? OptionsSourceId { get; set; }

	/// <summary>Whether a widget-target parameter offers the "this widget" sentinel.</summary>
	public bool AllowSelf { get; set; } = true;

	/// <summary>Widget type names a widget-target parameter is limited to. Empty means every widget.</summary>
	public IReadOnlyList<string> WidgetTypes { get; set; } = [];

	public List<string>? FileExtensions { get; set; }

	public string? Language { get; set; }

	public List<ActionParameterDef>? Children { get; set; }

	public ActionParameterDef? ItemTemplate { get; set; }

	public ParameterVisibilityDto? VisibleWhen { get; set; }
}

public class ParameterVisibilityDto
{
	public string ParameterName { get; set; } = string.Empty;

	public List<string> Values { get; set; } = [];
}
