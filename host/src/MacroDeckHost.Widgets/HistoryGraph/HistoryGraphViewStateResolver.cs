using System.Globalization;
using MacroDeck.Localization;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Widgets.HistoryGraph;

/// <summary>
/// Turns the configuration, the variables it names and the retained window into everything the tree draws.
///
/// <para>
/// Stateful in one respect, deliberately: the value's reserved width only ever grows (see
/// <see cref="Digits" />). A reservation that shrank back would make the layout oscillate with the metric,
/// which is the very thing reserving it is there to stop.
/// </para>
/// </summary>
internal sealed class HistoryGraphViewStateResolver
{
	/// <summary>What the value reads as when there is none. A dash rather than a zero: a metric that is
	/// not reporting has no value, and drawing one as zero is a lie the user cannot see through.</summary>
	public const string Unavailable = "-";

	/// <summary>The ceiling on the reserved width, so one outlier cannot squeeze the labels out.</summary>
	private const double MaxDigits = 6;

	/// <summary>A decimal separator is far narrower than a digit of a tabular face.</summary>
	private const double SeparatorWidth = 0.5;

	private readonly HistoryGraphWidgetData _config;
	private readonly VariableRegistry _variables;
	private double _widestSeen;

	public HistoryGraphViewStateResolver(HistoryGraphWidgetData config, VariableRegistry variables)
	{
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(variables);

		_config = config;
		_variables = variables;
	}

	public HistoryGraphViewState Resolve(IReadOnlyList<double> samples)
	{
		ArgumentNullException.ThrowIfNull(samples);

		var variable = Resolve(_config.ValueVariable);
		var (value, unit) = FormatValue(variable);

		// The unit is measured out of the reservation on purpose: it is a fixed suffix beside the value, not
		// part of the number whose width rolls over.
		_widestSeen = Math.Max(_widestSeen, Width(value));

		return new HistoryGraphViewState
		{
			Value = value,
			Unit = unit,
			Digits = Digits(variable),
			Subtitle = ResolveSubtitle(),
			Points = Normalize(samples),
		};
	}

	/// <summary>
	/// The window mapped onto the chart's plot band. A configured bound pins that end of the scale so two
	/// graphs of the same metric are comparable and a value's height means the same thing over time;
	/// without one that end follows the window's own extreme, which is the only way a metric with no
	/// natural ceiling stays readable. A configured maximum on its own pins the floor at zero. Samples
	/// outside a configured bound flatten against it, at the floor as at the ceiling. A window whose values
	/// are all equal sits at half height only while neither bound is configured.
	/// </summary>
	private double[] Normalize(IReadOnlyList<double> samples)
	{
		if (samples.Count == 0)
		{
			return [];
		}

		var windowLow = samples[0];
		var windowHigh = samples[0];

		foreach (var sample in samples)
		{
			windowLow = Math.Min(windowLow, sample);
			windowHigh = Math.Max(windowHigh, sample);
		}

		// A non-positive maximum has always read as auto, and the field still refuses one.
		var max = Bound(_config.MaxValue) is { } ceiling and > 0 ? ceiling : (double?)null;
		var min = Bound(_config.MinValue);

		// A maximum on its own keeps pinning the floor at zero: that is what every profile written before
		// there was a minimum means by it, and a graph of a percentage still has to start at zero.
		var low = min ?? (max is not null ? 0 : windowLow);
		var high = max ?? windowHigh;

		var range = high - low;
		var points = new double[samples.Count];

		for (var i = 0; i < samples.Count; i++)
		{
			points[i] = range > 0
				? Math.Round(Math.Clamp((samples[i] - low) / range, 0, 1), 4)
				: min is null && max is null ? 0.5
				: samples[i] <= low ? 0
				: 1;
		}

		return points;
	}

	private string ResolveSubtitle()
	{
		if (!_config.ShowSubtitle)
		{
			return string.Empty;
		}

		if (!string.IsNullOrEmpty(_config.SubtitleVariable))
		{
			var variable = Resolve(_config.SubtitleVariable);

			if (variable is not null && !string.IsNullOrEmpty(variable.Value))
			{
				return variable.Value;
			}
		}

		return _config.Subtitle ?? string.Empty;
	}

	/// <summary>The value and the unit it is drawn beside, both taken from the variable: the graph no longer
	/// carries a unit of its own, and a percentage, a byte count and a duration each read the way their
	/// semantic kind says they should.</summary>
	private static (string Value, LocalizedText Unit) FormatValue(VariableEntity? variable)
	{
		if (variable is null ||
			!double.TryParse(variable.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
			!double.IsFinite(value))
		{
			return (Unavailable, default);
		}

		// An undeclared precision reads as none rather than as the invariant round-trip, because Digits sizes
		// the value's reservation off exactly the same assumption.
		var formatted = VariableValueFormatter.Format(value,
			variable.SemanticKind,
			variable.Unit,
			Math.Max(0, variable.DecimalPlaces ?? 0));

		return (formatted.Value.Literal ?? Unavailable, formatted.Unit);
	}

	/// <summary>
	/// The reservation: what the configured bounds need where there are any, a negative floor including
	/// room for its sign, never less than the widest value seen so far - so a graph of an open-ended metric
	/// settles once instead of nudging its value sideways every time a digit appears.
	/// </summary>
	private double Digits(VariableEntity? variable)
	{
		var configured = Math.Max(Reserve(Bound(_config.MaxValue), variable, reserveSign: false),
			Reserve(Bound(_config.MinValue), variable, reserveSign: true));

		return Math.Min(MaxDigits, Math.Max(configured, _widestSeen));
	}

	private static double Reserve(double? bound, VariableEntity? variable, bool reserveSign)
	{
		if (bound is not { } value)
		{
			return 0;
		}

		var decimals = Math.Max(0, variable?.DecimalPlaces ?? 0);
		var integerDigits = Math.Max(1, Math.Floor(Math.Abs(value)))
			.ToString("F0", CultureInfo.InvariantCulture)
			.Length;

		return integerDigits +
			(decimals > 0 ? decimals + SeparatorWidth : 0) +
			(reserveSign && value < 0 ? SeparatorWidth : 0);
	}

	// Zero reads as auto because the editor's number field has no unset state to store.
	private static double? Bound(double? value)
		=> value is { } bound && double.IsFinite(bound) && bound != 0 ? bound : null;

	private VariableEntity? Resolve(string? name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}

		var variable = _variables.FindByName(VariableScope.Global, null, name);

		return variable is not null && _variables.IsAvailable(variable.Id) ? variable : null;
	}

	/// <summary>The width of a rendered value in digit widths, exploiting the tabular digits it is drawn
	/// with: everything that is not a digit is materially narrower than one.</summary>
	private static double Width(string text)
	{
		var width = 0d;

		foreach (var character in text)
		{
			width += character is >= '0' and <= '9' ? 1 : SeparatorWidth;
		}

		return width;
	}
}
