using System.Globalization;
using System.Text;
using MacroDeckHost.Application.Variables.Colors;
using Scriban.Runtime;

namespace MacroDeckHost.Application.Variables;

internal static class TemplateFilters
{
	internal const string PlainTextName = "plain_text";

	private static readonly IScriptCustomFunction _plainText =
		DelegateCustomFunction.CreateFunc<string?, string>(PlainText);

	private static readonly IScriptCustomFunction _color =
		DelegateCustomFunction.CreateFunc<TemplateFilterArgument?, string>(Color);

	private static readonly IScriptCustomFunction _colorMix =
		DelegateCustomFunction.CreateFunc<TemplateFilterArgument?, TemplateFilterArgument?, object?, string>(Mix);

	private static readonly IReadOnlyDictionary<string, IScriptCustomFunction> _modifiers
		= new Dictionary<string, IScriptCustomFunction>(StringComparer.Ordinal)
		{
			["color_lighten"] = Modifier((color, amount) => color.Lighten(amount)),
			["color_darken"] = Modifier((color, amount) => color.Darken(amount)),
			["color_saturate"] = Modifier((color, amount) => color.Saturate(amount)),
			["color_desaturate"] = Modifier((color, amount) => color.Desaturate(amount)),
			["color_opacity"] = Modifier((color, amount) => color.WithOpacity(amount)),
			["color_increase_opacity"] = Modifier((color, amount) => color.IncreaseOpacity(amount)),
			["color_reduce_opacity"] = Modifier((color, amount) => color.ReduceOpacity(amount)),
			["color_hue"] = Modifier((color, amount) => color.ShiftHue(amount))
		};

	internal static string PlainText(string? value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}

		try
		{
			return value.Normalize(NormalizationForm.FormKC);
		}
		catch (ArgumentException)
		{
			return value;
		}
	}

	internal static string Color(TemplateFilterArgument? value) => ColorOf(value)?.ToString() ?? string.Empty;

	internal static string Mix(TemplateFilterArgument? value, TemplateFilterArgument? other, object? percent)
		=> ColorOf(value) is { } color && ColorOf(other) is { } with && NumberOf(percent) is { } amount
			? color.Mix(with, amount).ToString()
			: string.Empty;

	internal static ScriptObject CreateScope()
	{
		var scope = new ScriptObject { [PlainTextName] = _plainText, ["color"] = _color, ["color_mix"] = _colorMix };
		foreach (var modifier in _modifiers)
		{
			scope[modifier.Key] = modifier.Value;
		}

		return scope;
	}

	private static DelegateCustomFunction Modifier(Func<RgbaColor, double, RgbaColor> apply)
		=> DelegateCustomFunction.CreateFunc<TemplateFilterArgument?, object?, string>((value, amount)
			=> ColorOf(value) is { } color && NumberOf(amount) is { } number
				? apply(color, number).ToString()
				: string.Empty);

	// A variable counts only when it is a Color variable, the same rule the colour resolver applies; an
	// unavailable or unknown one never parses, so it falls through to the empty string meaning unset.
	private static RgbaColor? ColorOf(TemplateFilterArgument? argument) => argument?.Value switch
	{
		VariableTemplateValue { IsColor: true } variable => RgbaColor.Parse(variable.Value as string),
		VariableTemplateValue => null,
		string text => RgbaColor.Parse(text),
		_ => null
	};

	private static double? NumberOf(object? value) => VariableTemplateValue.Unwrap(value) switch
	{
		IConvertible convertible and not string and not bool => convertible.ToDouble(CultureInfo.InvariantCulture),
		string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
			=> parsed,
		_ => null
	};
}
