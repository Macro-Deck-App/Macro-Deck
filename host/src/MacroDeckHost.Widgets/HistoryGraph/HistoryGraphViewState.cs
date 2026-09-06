using MacroDeck.Localization;

namespace MacroDeckHost.Widgets.HistoryGraph;

/// <summary>Everything the History Graph tree reads, resolved. A record so an unchanged resolve compares
/// equal and writes nothing.</summary>
internal sealed record HistoryGraphViewState
{
	public static readonly HistoryGraphViewState Empty = new();

	/// <summary>The value drawn large, already formatted to the variable's semantic kind and decimal places.
	/// <see cref="HistoryGraphViewStateResolver.Unavailable" /> whenever the variable is missing,
	/// unavailable or not a number.</summary>
	public string Value { get; init; } = HistoryGraphViewStateResolver.Unavailable;

	/// <summary>The unit drawn after the value, taken from the variable itself rather than from the widget's
	/// own configuration. Absent whenever the variable declares none.</summary>
	public LocalizedText Unit { get; init; }

	/// <summary>Whether there is a unit to draw at all.</summary>
	public bool HasUnit => !Unit.IsEmpty;

	/// <summary>How many digit widths the value reserves, so a rollover does not shift it around its own
	/// centre. Zero reserves nothing.</summary>
	public double Digits { get; init; }

	/// <summary>The resolved subtitle, empty when there is none to draw.</summary>
	public string Subtitle { get; init; } = string.Empty;

	/// <summary>The retained window normalised to the chart's plot band, oldest first.</summary>
	public IReadOnlyList<double> Points { get; init; } = [];

	public bool Equals(HistoryGraphViewState? other)
		=> other is not null &&
			string.Equals(Value, other.Value, StringComparison.Ordinal) &&
			Unit.Equals(other.Unit) &&
			Digits.Equals(other.Digits) &&
			string.Equals(Subtitle, other.Subtitle, StringComparison.Ordinal) &&
			Points.SequenceEqual(other.Points);

	public override int GetHashCode() => HashCode.Combine(Value, Unit, Digits, Subtitle, Points.Count);
}
