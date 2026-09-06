using System.Globalization;
using MacroDeck.Localization;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using VariableSemanticKinds = MacroDeck.Sdk.Variables.VariableSemanticKinds;

namespace MacroDeckHost.Application.Variables;

/// <summary>
/// A variable's value and its unit, still apart. Two clients reading the same variable may be reading in
/// different languages, so the pieces are handed over separately and the client joins them; a rendered
/// string here would pick one reader's language for everybody.
/// </summary>
public readonly record struct FormattedVariableValue(LocalizedText Value, LocalizedText Unit);

/// <summary>
/// Renders a variable's value the way its <c>SemanticKind</c> says it means something - seconds as a clock,
/// a byte count on the 1024 ladder - so every consumer formats it the same way instead of each widget
/// solving it again.
/// </summary>
public static class VariableValueFormatter
{
	private static readonly string[] _byteRungs = ["B", "KB", "MB", "GB", "TB", "PB"];

	public static FormattedVariableValue Format(VariableEntity entity)
	{
		if (entity.Type != VariableType.Numeric ||
			!double.TryParse(entity.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
		{
			return new FormattedVariableValue(entity.Value, Unit(entity.Unit));
		}

		return Format(value, entity.SemanticKind, entity.Unit, entity.DecimalPlaces);
	}

	/// <summary>
	/// <paramref name="semanticKind"/> is an open string: a kind the host does not know renders as a plain
	/// number with its declared unit rather than as an error, which is what lets a provider introduce one
	/// without a host release.
	/// </summary>
	public static FormattedVariableValue Format(double value, string? semanticKind, string? unit, int? decimalPlaces)
	{
		if (!double.IsFinite(value))
		{
			return new FormattedVariableValue(Number(value, decimalPlaces), Unit(unit));
		}

		return semanticKind switch
		{
			VariableSemanticKinds.Duration => new FormattedVariableValue(Duration(value), default),
			VariableSemanticKinds.Bytes => Bytes(value),
			_ => new FormattedVariableValue(Number(value, decimalPlaces), Unit(unit))
		};
	}

	private static LocalizedText Duration(double seconds)
	{
		var total = (long)Math.Round(Math.Abs(seconds), MidpointRounding.AwayFromZero);
		var hours = total / 3600;
		var minutes = total % 3600 / 60;
		var remainder = total % 60;
		var sign = seconds < 0 ? "-" : string.Empty;

		return hours > 0
			? string.Create(CultureInfo.InvariantCulture, $"{sign}{hours}:{minutes:00}:{remainder:00}")
			: string.Create(CultureInfo.InvariantCulture, $"{sign}{minutes:00}:{remainder:00}");
	}

	// The declared unit is superseded by the rung the value lands on: a value already scaled by its
	// provider says so with a kind other than bytes.
	private static FormattedVariableValue Bytes(double value)
	{
		var scaled = value;
		var rung = 0;
		while (Math.Abs(scaled) >= 1024 && rung < _byteRungs.Length - 1)
		{
			scaled /= 1024;
			rung++;
		}

		var text = scaled.ToString(rung == 0 ? "F0" : "F1", CultureInfo.InvariantCulture);
		return new FormattedVariableValue(text, _byteRungs[rung]);
	}

	private static LocalizedText Number(double value, int? decimalPlaces)
		=> decimalPlaces is { } places
			? value.ToString("F" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
			: value.ToString(CultureInfo.InvariantCulture);

	private static LocalizedText Unit(string? unit)
		=> string.IsNullOrWhiteSpace(unit) ? default : LocalizedText.FromLiteral(unit);
}
