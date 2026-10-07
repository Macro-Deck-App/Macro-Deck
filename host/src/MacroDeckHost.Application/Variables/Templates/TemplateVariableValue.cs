using System.Globalization;
using MacroDeckHost.Application.Variables.Colors;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Variables.Templates;

public static class TemplateVariableValue
{
	public static (string? Value, VariableTemplateError? Error) Convert(
		VariableType type,
		int? decimalPlaces,
		string rendered)
	{
		switch (type)
		{
			case VariableType.Numeric:
			{
				var parsed = decimal.TryParse(rendered.Trim(),
					NumberStyles.Float,
					CultureInfo.InvariantCulture,
					out var number);
				return parsed
					? (VariableValueSerializer.Serialize(type, number, decimalPlaces), null)
					: (null, new VariableTemplateError(VariableTemplateError.NotNumeric, rendered));
			}

			case VariableType.Boolean:
			{
				var trimmed = rendered.Trim();
				if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase) || trimmed == "1")
				{
					return ("true", null);
				}

				if (string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase) || trimmed == "0")
				{
					return ("false", null);
				}

				return (null, new VariableTemplateError(VariableTemplateError.NotBoolean, rendered));
			}

			case VariableType.Color:
				return RgbaColor.Canonicalize(rendered.Trim()) is { } color
					? (color, null)
					: (null, new VariableTemplateError(VariableTemplateError.NotColor, rendered));

			default:
				return (rendered, null);
		}
	}
}
