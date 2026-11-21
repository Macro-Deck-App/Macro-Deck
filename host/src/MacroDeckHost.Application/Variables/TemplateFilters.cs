using System.Globalization;
using System.Text;
using MacroDeckHost.Application.Variables.Colors;
using Scriban.Functions;
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

	private static readonly ClockDateFunctions _date = new();

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

	private static readonly ScriptObject _lenientMath = BuildLenientMath();

	private static readonly ScriptObject _math = BuildMath();

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

	internal static string Color(TemplateFilterArgument? value)
	{
		return ColorOf(value)?.ToString() ?? string.Empty;
	}

	internal static string Mix(TemplateFilterArgument? value, TemplateFilterArgument? other, object? percent)
	{
		return ColorOf(value) is { } color && ColorOf(other) is { } with && NumberOf(percent) is { } amount
			? color.Mix(with, amount).ToString()
			: string.Empty;
	}

	internal static ScriptObject CreateScope()
	{
		var scope = new ScriptObject
		{
			[PlainTextName] = _plainText,
			["color"] = _color,
			["color_mix"] = _colorMix,
			["date"] = _date,
			["math"] = _math
		};
		foreach (var modifier in _modifiers)
		{
			scope[modifier.Key] = modifier.Value;
		}

		// A filter with arguments is parsed as math.round, one without stays the bare Liquid alias round,
		// so both names have to lead to the same function.
		foreach (var function in _lenientMath)
		{
			scope[function.Key] = function.Value;
		}

		return scope;
	}

	private static ScriptObject BuildLenientMath()
	{
		var functions = new ScriptObject();
		functions.Import(typeof(TemplateMathFunctions));
		return functions;
	}

	// Shared by every render on every thread, so it is frozen: an assign to math.round in one label must
	// not reach any other.
	private static ScriptObject BuildMath()
	{
		var math = new ScriptObject();
		new MathFunctions().CopyTo(math);
		foreach (var function in _lenientMath)
		{
			math.SetValue(function.Key, function.Value, true);
		}

		math.IsReadOnly = true;
		return math;
	}

	private static DelegateCustomFunction Modifier(Func<RgbaColor, double, RgbaColor> apply)
	{
		return DelegateCustomFunction.CreateFunc<TemplateFilterArgument?, object?, string>((value, amount)
			=> ColorOf(value) is { } color && NumberOf(amount) is { } number
				? apply(color, number).ToString()
				: string.Empty);
	}

	// A variable counts only when it is a Color variable, the same rule the colour resolver applies; an
	// unavailable or unknown one never parses, so it falls through to the empty string meaning unset.
	private static RgbaColor? ColorOf(TemplateFilterArgument? argument)
	{
		return argument?.Value switch
		{
			VariableTemplateValue { IsColor: true } variable => RgbaColor.Parse(variable.Value as string),
			VariableTemplateValue => null,
			string text => RgbaColor.Parse(text),
			_ => null
		};
	}

	private static double? NumberOf(object? value)
	{
		return VariableTemplateValue.Unwrap(value) switch
		{
			IConvertible convertible and not string and not bool => convertible.ToDouble(CultureInfo.InvariantCulture),
			string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
				=> parsed,
			_ => null
		};
	}
}
