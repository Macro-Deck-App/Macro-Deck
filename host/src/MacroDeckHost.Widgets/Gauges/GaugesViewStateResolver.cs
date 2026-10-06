using System.Globalization;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Widgets.Gauges;

internal sealed class GaugesViewStateResolver
{
	public const string Unavailable = "-";

	public const string WarningColor = "#ff3b30";

	private const double AutoSpan = 100;

	private readonly VariableRegistry _variables;
	private readonly VariableTemplateRenderer _templates;
	private readonly string? _scopeRefId;

	public GaugesViewStateResolver(VariableRegistry variables, string? scopeRefId = null)
	{
		ArgumentNullException.ThrowIfNull(variables);

		_variables = variables;
		_templates = new VariableTemplateRenderer(variables);
		_scopeRefId = scopeRefId;
	}

	public GaugeFace Resolve(GaugeConfig gauge)
	{
		ArgumentNullException.ThrowIfNull(gauge);

		var variable = Find(gauge.Variable);

		if (variable is null ||
			!double.TryParse(variable.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
			!double.IsFinite(value))
		{
			return new GaugeFace { Color = gauge.Color, Name = ResolveName(gauge.Name) };
		}

		var formatted = VariableValueFormatter.Format(value,
			variable.SemanticKind,
			variable.Unit,
			Math.Max(0, variable.DecimalPlaces ?? 0));

		return new GaugeFace
		{
			Level = Level(value, gauge, variable),
			Value = formatted.Value.Literal ?? Unavailable,
			Unit = formatted.Unit,
			Color = ColorOf(value, gauge, variable),
			Name = ResolveName(gauge.Name),
		};
	}

	private string ResolveName(string? name)
	{
		if (name is not { Length: > 0 } || !VariableTemplateRenderer.ContainsLiquid(name))
		{
			return name ?? string.Empty;
		}

		try
		{
			return _templates.Render(name, _scopeRefId is null ? VariableScope.Global : VariableScope.Widget, _scopeRefId);
		}
		catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
		{
			// A name is user written text that renders on every variable change; a broken template shows
			// itself instead of faulting the session.
			return name;
		}
	}

	public static double Level(double value, GaugeConfig gauge, VariableEntity? variable)
	{
		var (min, max) = Bounds(gauge, variable);
		var level = (value - min) / (max - min);

		return double.IsFinite(level) ? Math.Round(Math.Clamp(level, 0, 1), 4) : 0;
	}

	// The editor's number fields have no empty state, so a maximum of 0 stands for automatic.
	public static (double Min, double Max) Bounds(GaugeConfig gauge, VariableEntity? variable)
	{
		var min = gauge.Min ?? 0;

		if (gauge.Max is { } max && max != 0 && max > min)
		{
			return (min, max);
		}

		return variable?.Max is { } declared && double.IsFinite(declared) && declared > min
			? (min, declared)
			: (min, min + AutoSpan);
	}

	public static string? ColorOf(double value, GaugeConfig gauge, VariableEntity? variable)
	{
		if (!gauge.ThresholdsEnabled)
		{
			return IsWarning(value, gauge) ? WarningColor : gauge.Color;
		}

		var (min, max) = Bounds(gauge, variable);

		return WidgetThresholds.Effective(gauge.Thresholds, min, max).ColorAt(value) ?? gauge.Color;
	}

	public static bool IsWarning(double value, GaugeConfig gauge)
		=> gauge.WarnWhen switch
		{
			GaugeConfig.WarnAbove => value >= (gauge.WarnAt ?? 0),
			GaugeConfig.WarnBelow => value <= (gauge.WarnAt ?? 0),
			_ => false,
		};

	private VariableEntity? Find(string? name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}

		var variable = (_scopeRefId is null ? null : _variables.FindByName(VariableScope.Widget, _scopeRefId, name)) ??
			_variables.FindByName(VariableScope.Global, null, name);

		return variable is not null && _variables.IsAvailable(variable.Id) ? variable : null;
	}
}
