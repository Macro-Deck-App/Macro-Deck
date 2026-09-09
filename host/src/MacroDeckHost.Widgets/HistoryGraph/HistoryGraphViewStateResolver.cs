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
	private readonly VariableTemplateRenderer _templates;
	private readonly string? _scopeRefId;
	private double _widestSeen;

	public HistoryGraphViewStateResolver(
		HistoryGraphWidgetData config,
		VariableRegistry variables,
		string? scopeRefId = null)
	{
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(variables);

		_config = config;
		_variables = variables;
		_scopeRefId = scopeRefId;
		_templates = new VariableTemplateRenderer(variables);
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
	/// The window mapped onto the chart's plot band. A configured maximum pins the scale so two graphs of
	/// the same metric are comparable and a value's height means the same thing over time; without one the
	/// scale follows the window's own extremes, which is the only way a metric with no natural ceiling
	/// stays readable. A window whose values are all equal sits at half height rather than at the floor or
	/// the ceiling, neither of which it is any closer to.
	/// </summary>
	private double[] Normalize(IReadOnlyList<double> samples)
	{
		if (samples.Count == 0)
		{
			return [];
		}

		double low;
		double high;

		if (_config.MaxValue is { } max and > 0)
		{
			low = 0;
			high = max;
		}
		else
		{
			low = samples[0];
			high = samples[0];

			foreach (var sample in samples)
			{
				low = Math.Min(low, sample);
				high = Math.Max(high, sample);
			}
		}

		var range = high - low;
		var points = new double[samples.Count];

		for (var i = 0; i < samples.Count; i++)
		{
			points[i] = range > 0
				? Math.Round(Math.Clamp((samples[i] - low) / range, 0, 1), 4)
				: 0.5;
		}

		return points;
	}

	private string ResolveSubtitle()
	{
		if (!_config.ShowSubtitle || _config.Subtitle is not { Length: > 0 } subtitle)
		{
			return string.Empty;
		}

		if (!VariableTemplateRenderer.ContainsLiquid(subtitle))
		{
			return subtitle;
		}

		try
		{
			return _templates.Render(subtitle,
				_scopeRefId is null ? VariableScope.Global : VariableScope.Widget,
				_scopeRefId);
		}
		catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
		{
			// A subtitle is user written text, and this runs on the sampling timer: an unparsable
			// template must show itself rather than take the tick down with it.
			return subtitle;
		}
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
	/// The reservation: what the configured maximum needs where there is one, and otherwise the widest
	/// value seen so far - so a graph of an open-ended metric settles once instead of nudging its value
	/// sideways every time a digit appears.
	/// </summary>
	private double Digits(VariableEntity? variable)
	{
		var configured = 0d;

		if (_config.MaxValue is { } max && double.IsFinite(max) && max != 0)
		{
			var decimals = Math.Max(0, variable?.DecimalPlaces ?? 0);
			var integerDigits = Math.Max(1, Math.Floor(Math.Abs(max)))
				.ToString("F0", CultureInfo.InvariantCulture)
				.Length;

			configured = integerDigits + (decimals > 0 ? decimals + SeparatorWidth : 0);
		}

		return Math.Min(MaxDigits, Math.Max(configured, _widestSeen));
	}

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
