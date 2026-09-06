using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

public static class ActionParameterMapper
{
	public static ActionParameterDto ToDto(ActionParameter parameter) => ToDto(parameter, depth: 0);

	private static ActionParameterDto ToDto(ActionParameter parameter, int depth)
	{
		if (depth >= ProtocolLimits.MaxJsonDepth)
		{
			throw new InvalidOperationException(
				$"Action parameter '{parameter.Name}' nests deeper than {ProtocolLimits.MaxJsonDepth} levels.");
		}

		return new ActionParameterDto
		{
			Name = parameter.Name,
			Type = parameter.Type.ToString(),
			// These descriptors only ever come back from the wire, where the protocol types this text as a
			// string, so a literal is all there can be to carry.
			Label = parameter.Label.Literal,
			Description = parameter.Description.Literal,
			Placeholder = parameter.Placeholder.Literal,
			AutoPrefixHttps = parameter.AutoPrefixHttps,
			DefaultValue = parameter.DefaultValue is null
				? null
				: JsonSerializer.SerializeToElement(parameter.DefaultValue),
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
			AllowSelf = parameter.AllowSelf,
			WidgetTypes = parameter.WidgetTypes,
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
		=> new() { Value = option.Value, Label = option.Label.Literal, Metadata = option.Metadata };

	public static ActionParameter ToDomain(ActionParameterDto dto) => ToDomain(dto, depth: 0);

	private static ActionParameter ToDomain(ActionParameterDto dto, int depth)
	{
		// Mirrors the plugin-side guard: a wire tree cannot be trusted to respect the depth this host
		// itself enforces when serializing, so this is checked again on the way back in.
		if (depth >= ProtocolLimits.MaxJsonDepth)
		{
			throw new InvalidOperationException(
				$"Action parameter '{dto.Name}' nests deeper than {ProtocolLimits.MaxJsonDepth} levels.");
		}

		if (!Enum.TryParse<ActionParameterType>(dto.Type, ignoreCase: false, out var type))
		{
			throw new InvalidOperationException($"Unknown action parameter type '{dto.Type}'.");
		}

		return new ActionParameter
		{
			Name = dto.Name,
			Type = type,
			Label = dto.Label ?? default,
			Description = dto.Description ?? default,
			Placeholder = dto.Placeholder ?? default,
			AutoPrefixHttps = dto.AutoPrefixHttps,
			DefaultValue = ToValue(dto.DefaultValue),
			Required = dto.Required,
			Multiline = dto.Multiline,
			SupportsReset = dto.SupportsReset,
			LiteralOnly = dto.LiteralOnly,
			ValidationRegex = dto.ValidationRegex,
			MaxLength = dto.MaxLength,
			Min = dto.Min,
			Max = dto.Max,
			Step = dto.Step,
			ShowSlider = dto.ShowSlider,
			Options = dto.Options?.Select(ToDomain).ToList(),
			DynamicOptions = dto.DynamicOptions,
			OptionsSourceId = dto.OptionsSourceId,
			AllowSelf = dto.AllowSelf ?? true,
			WidgetTypes = dto.WidgetTypes ?? [],
			FileExtensions = dto.FileExtensions,
			Language = dto.Language,
			Children = dto.Children?.Select(child => ToDomain(child, depth + 1)).ToList(),
			ItemTemplate = dto.ItemTemplate is { } template ? ToDomain(template, depth + 1) : null
		}.ApplyVisibility(dto.VisibleWhen);
	}

	private static ActionParameter ApplyVisibility(this ActionParameter parameter, ParameterVisibilityDto? visibility)
		=> visibility is null ? parameter : parameter.OnlyWhen(visibility.ParameterName, [.. visibility.Values]);

	private static ActionParameterOption ToDomain(ActionParameterOptionDto dto)
		=> new() { Value = dto.Value, Label = dto.Label ?? default, Metadata = dto.Metadata };

	private static object? ToValue(JsonElement? element)
		=> element is { } value
			? value.ValueKind switch
			{
				JsonValueKind.String => value.GetString(),
				JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
				JsonValueKind.Number when value.TryGetDouble(out var number) => number,
				JsonValueKind.True => true,
				JsonValueKind.False => false,
				JsonValueKind.Null or JsonValueKind.Undefined => null,
				_ => value.Clone()
			}
			: null;
}
