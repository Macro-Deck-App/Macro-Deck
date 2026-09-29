using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeck.Plugin.Hosting.Capabilities.MusicPlayer;

// Linked into the host as well, so both sides of the wire coerce option values by the same table and a provider sees the same types in and out of process.
internal static partial class MusicPlayerOptionValues
{
	public static bool IsSupported(ActionParameter option)
		=> option.Name is { } name &&
			NamePattern().IsMatch(name) &&
			option.Type switch
			{
				ActionParameterType.String or ActionParameterType.Number or ActionParameterType.Boolean => true,
				ActionParameterType.Choice => !option.DynamicOptions && option.Options is { Count: > 0 },
				_ => false
			};

	public static IReadOnlyList<ActionParameter> Supported(IReadOnlyList<ActionParameter>? declared)
	{
		if (declared is null || declared.Count == 0)
		{
			return [];
		}

		var names = new HashSet<string>(StringComparer.Ordinal);
		var supported = new List<ActionParameter>();

		foreach (var option in declared)
		{
			if (option is not null && IsSupported(option) && names.Add(option.Name))
			{
				supported.Add(option);
			}
		}

		return supported;
	}

	public static IReadOnlyDictionary<string, object> Normalize(
		IReadOnlyList<ActionParameter> supported,
		IReadOnlyDictionary<string, JsonElement>? values)
	{
		var result = new Dictionary<string, object>(StringComparer.Ordinal);

		foreach (var option in supported)
		{
			var value = values is not null && values.TryGetValue(option.Name, out var element)
				? element
				: default;

			result[option.Name] = option.Type switch
			{
				ActionParameterType.Number => NumberValue(option, value),
				ActionParameterType.Boolean => BooleanValue(option, value),
				ActionParameterType.Choice => ChoiceValue(option, value),
				_ => StringValue(option, value)
			};
		}

		return result;
	}

	private static string StringValue(ActionParameter option, JsonElement value)
		=> value.ValueKind switch
		{
			JsonValueKind.String => value.GetString() ?? string.Empty,
			JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
			_ => Convert.ToString(option.DefaultValue, CultureInfo.InvariantCulture) ?? string.Empty
		};

	private static double NumberValue(ActionParameter option, JsonElement value)
	{
		double? parsed = value.ValueKind switch
		{
			JsonValueKind.Number => value.GetDouble(),
			JsonValueKind.String when double.TryParse(value.GetString(),
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var number) => number,
			_ => null
		};

		var result = parsed is { } candidate && double.IsFinite(candidate) ? candidate : DefaultNumber(option);

		if (option.Min is { } min && result < min)
		{
			result = min;
		}

		if (option.Max is { } max && result > max)
		{
			result = max;
		}

		return result;
	}

	private static double DefaultNumber(ActionParameter option)
		=> option.DefaultValue switch
		{
			IConvertible convertible when TryConvert(convertible, out var number) => number,
			_ => option.Min ?? 0
		};

	private static bool TryConvert(IConvertible convertible, out double number)
	{
		try
		{
			number = convertible.ToDouble(CultureInfo.InvariantCulture);
			return double.IsFinite(number);
		}
		catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
		{
			number = 0;
			return false;
		}
	}

	private static bool BooleanValue(ActionParameter option, JsonElement value)
		=> value.ValueKind switch
		{
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
			_ => option.DefaultValue is bool fallback && fallback
		};

	private static string ChoiceValue(ActionParameter option, JsonElement value)
	{
		var choices = option.Options!;

		if (value.ValueKind == JsonValueKind.String && IsChoice(choices, value.GetString()))
		{
			return value.GetString()!;
		}

		return option.DefaultValue is string fallback && IsChoice(choices, fallback) ? fallback : choices[0].Value;
	}

	private static bool IsChoice(IReadOnlyList<ActionParameterOption> choices, string? value)
		=> choices.Any(choice => string.Equals(choice.Value, value, StringComparison.Ordinal));

	[GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9_-]{0,63}\z", RegexOptions.CultureInvariant)]
	private static partial Regex NamePattern();
}
