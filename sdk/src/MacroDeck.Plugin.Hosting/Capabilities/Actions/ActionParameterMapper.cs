using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Plugin.Hosting.Localization;

namespace MacroDeck.Plugin.Hosting.Capabilities.Actions;

/// <summary>
/// Converts an in-process <see cref="ActionParameter" /> tree into its wire DTO. The plugin side only
/// ever writes this direction - the host is the one that reconstructs <see cref="ActionParameter" />
/// from what comes back, since it is the host, not the plugin, that hands the SDK's own type to
/// <see cref="MacroDeck.Sdk.Actions.IActionDefinition.Parameters" /> on the remote adapter.
/// </summary>
internal static class ActionParameterMapper
{
	public static ActionParameterDto ToDto(ActionParameter parameter) => ToDto(parameter, depth: 0);

	private static ActionParameterDto ToDto(ActionParameter parameter, int depth)
	{
		// A guard against a pathological (or malicious) recursive template, not a case the built-in
		// parameter factories can produce - MaxJsonDepth already bounds what the wire will accept, so
		// mirroring it here fails the same tree earlier, with a message instead of a serializer error.
		if (depth >= ProtocolLimits.MaxJsonDepth)
		{
			throw new InvalidOperationException(
				$"Action parameter '{parameter.Name}' nests deeper than {ProtocolLimits.MaxJsonDepth} levels.");
		}

		return new ActionParameterDto
		{
			Name = parameter.Name,
			Type = parameter.Type.ToString(),
			Label = PluginText.ToWireOrNull(parameter.Label),
			Description = PluginText.ToWireOrNull(parameter.Description),
			Placeholder = PluginText.ToWireOrNull(parameter.Placeholder),
			AutoPrefixHttps = parameter.AutoPrefixHttps,
			DefaultValue = ToElement(parameter.DefaultValue),
			Required = parameter.Required,
			Multiline = parameter.Multiline,
			SupportsReset = parameter.SupportsReset,
			LiteralOnly = parameter.LiteralOnly,
			ValidationRegex = parameter.ValidationRegex,
			MaxLength = parameter.MaxLength,
			Min = parameter.Min,
			Max = parameter.Max,
			Step = parameter.Step,
			ShowSlider = parameter.ShowSlider,
			Options = parameter.Options?.Select(ToDto).ToList(),
			DynamicOptions = parameter.DynamicOptions,
			OptionsSourceId = parameter.OptionsSourceId,
			FileExtensions = parameter.FileExtensions,
			Language = parameter.Language,
			Children = parameter.Children?.Select(child => ToDto(child, depth + 1)).ToList(),
			ItemTemplate = parameter.ItemTemplate is { } template ? ToDto(template, depth + 1) : null,
			VisibleWhen = parameter.VisibleWhen is { } visibility
				? new ParameterVisibilityDto { ParameterName = visibility.ParameterName, Values = visibility.Values }
				: null
		};
	}

	private static ActionParameterOptionDto ToDto(ActionParameterOption option)
		=> new() { Value = option.Value, Label = PluginText.ToWireOrNull(option.Label), Metadata = option.Metadata };

	private static JsonElement? ToElement(object? value)
		=> value is null ? null : JsonSerializer.SerializeToElement(value, PluginProtocolJson.Options);
}
