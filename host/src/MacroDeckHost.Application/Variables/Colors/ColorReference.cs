using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MacroDeck.Plugin.Protocol.Limits;

namespace MacroDeckHost.Application.Variables.Colors;

public enum ColorModifier
{
	Lighten,
	Darken,
	Saturate,
	Desaturate,
	Opacity,
	IncreaseOpacity,
	ReduceOpacity,
	Hue,
	Mix
}

public sealed record ColorStep(ColorModifier Modifier, double Amount, string? MixVariable = null, RgbaColor? MixColor = null);

public sealed partial record ColorReference(string Variable, IReadOnlyList<ColorStep> Steps)
{
	public static bool MightBeReference(string? text)
		=> text is not null &&
			text.Contains("{{", StringComparison.Ordinal) &&
			text.Contains("color", StringComparison.Ordinal);

	public static bool TryParse(string? text, out ColorReference reference)
	{
		reference = null!;
		if (!MightBeReference(text))
		{
			return false;
		}

		if (text!.Length > ProtocolLimits.MaxColorReferenceLength)
		{
			return false;
		}

		var trimmed = text.Trim();
		if (!trimmed.StartsWith("{{", StringComparison.Ordinal) || !trimmed.EndsWith("}}", StringComparison.Ordinal))
		{
			return false;
		}

		var segments = trimmed[2..^2].Split('|');
		if (segments.Length < 2 || segments.Length - 2 > ProtocolLimits.MaxColorReferenceSteps)
		{
			return false;
		}

		var root = RootPattern().Match(segments[0]);
		if (!root.Success || !ColorFilterPattern().IsMatch(segments[1]))
		{
			return false;
		}

		var steps = new List<ColorStep>(segments.Length - 2);
		for (var i = 2; i < segments.Length; i++)
		{
			if (TryParseStep(segments[i]) is not { } step)
			{
				return false;
			}

			steps.Add(step);
		}

		reference = new ColorReference(root.Groups["name"].Value, steps);
		return true;
	}

	public IEnumerable<string> VariableNames()
	{
		yield return Variable;

		foreach (var step in Steps)
		{
			if (step.MixVariable is { } name)
			{
				yield return name;
			}
		}
	}

	private static ColorStep? TryParseStep(string segment)
	{
		var amount = AmountStepPattern().Match(segment);
		if (amount.Success)
		{
			return new ColorStep(ModifierOf(amount.Groups["op"].Value), Number(amount.Groups["amount"].Value));
		}

		var mix = MixStepPattern().Match(segment);
		if (!mix.Success)
		{
			return null;
		}

		var value = Number(mix.Groups["amount"].Value);
		if (mix.Groups["var"].Success)
		{
			return new ColorStep(ColorModifier.Mix, value, MixVariable: mix.Groups["var"].Value);
		}

		return RgbaColor.TryParse(mix.Groups["hex"].Value, out var color)
			? new ColorStep(ColorModifier.Mix, value, MixColor: color)
			: null;
	}

	private static ColorModifier ModifierOf(string op) => op switch
	{
		"lighten" => ColorModifier.Lighten,
		"darken" => ColorModifier.Darken,
		"saturate" => ColorModifier.Saturate,
		"desaturate" => ColorModifier.Desaturate,
		"opacity" => ColorModifier.Opacity,
		"increase_opacity" => ColorModifier.IncreaseOpacity,
		"reduce_opacity" => ColorModifier.ReduceOpacity,
		_ => ColorModifier.Hue
	};

	private static double Number(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

	[GeneratedRegex(@"\A\s*vars\.(?<name>[a-z][a-z0-9_]*)\s*\z", RegexOptions.CultureInvariant)]
	private static partial Regex RootPattern();

	[GeneratedRegex(@"\A\s*color\s*\z", RegexOptions.CultureInvariant)]
	private static partial Regex ColorFilterPattern();

	[GeneratedRegex(@"\A\s*color_(?<op>lighten|darken|saturate|desaturate|opacity|increase_opacity|reduce_opacity|hue)\s*:\s*(?<amount>-?\d+(?:\.\d+)?)\s*\z",
		RegexOptions.CultureInvariant)]
	private static partial Regex AmountStepPattern();

	[GeneratedRegex(
		"\\A\\s*color_mix\\s*:\\s*(?:vars\\.(?<var>[a-z][a-z0-9_]*)|\"(?<hex>#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{3}))\")\\s*,\\s*(?<amount>-?\\d+(?:\\.\\d+)?)\\s*\\z",
		RegexOptions.CultureInvariant)]
	private static partial Regex MixStepPattern();
}

public static class ColorReferenceScanner
{
	private static readonly IReadOnlySet<string> _none = new HashSet<string>(StringComparer.Ordinal);

	public static IReadOnlySet<string> Names(string? json)
	{
		if (string.IsNullOrWhiteSpace(json) || !ColorReference.MightBeReference(json))
		{
			return _none;
		}

		try
		{
			using var document = JsonDocument.Parse(json);
			var names = new HashSet<string>(StringComparer.Ordinal);
			Collect(document.RootElement, names);
			return names.Count == 0 ? _none : names;
		}
		catch (JsonException)
		{
			return _none;
		}
	}

	public static bool References(string? value, string variableName)
		=> ColorReference.TryParse(value, out var reference) &&
			reference.VariableNames().Contains(variableName, StringComparer.Ordinal);

	private static void Collect(JsonElement element, HashSet<string> names)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				foreach (var property in element.EnumerateObject())
				{
					Collect(property.Value, names);
				}

				break;
			case JsonValueKind.Array:
				foreach (var item in element.EnumerateArray())
				{
					Collect(item, names);
				}

				break;
			case JsonValueKind.String
				when ColorReference.TryParse(element.GetString(), out var reference):
				names.UnionWith(reference.VariableNames());
				break;
		}
	}
}
