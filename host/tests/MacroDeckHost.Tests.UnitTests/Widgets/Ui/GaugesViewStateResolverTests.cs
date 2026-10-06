using System.Globalization;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Widgets.Gauges;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

[TestFixture]
public class GaugesViewStateResolverTests
{
	private const string Metric = "gauge_metric";

	[TestCase(42, 0.42)]
	[TestCase(-5, 0)]
	[TestCase(250, 1)]
	public void The_level_is_the_value_between_minimum_and_maximum_clamped_to_the_ring(double value, double level)
	{
		var face = Resolve(value, new GaugeConfig { Id = "g", Variable = Metric, Min = 0, Max = 100 });

		Assert.That(face.Level, Is.EqualTo(level).Within(0.0001));
	}

	[Test]
	public void A_minimum_shifts_where_the_ring_starts()
	{
		var face = Resolve(25, new GaugeConfig { Id = "g", Variable = Metric, Min = 20, Max = 30 });

		Assert.That(face.Level, Is.EqualTo(0.5).Within(0.0001));
	}

	[TestCase(null)]
	[TestCase(0d)]
	[TestCase(10d)]
	[TestCase(20d)]
	public void A_maximum_that_is_absent_zero_or_not_above_the_minimum_uses_the_variables_own_maximum(double? max)
	{
		var face = Resolve(30, new GaugeConfig { Id = "g", Variable = Metric, Min = 20, Max = max }, declaredMax: 40);

		Assert.That(face.Level, Is.EqualTo(0.5).Within(0.0001));
	}

	[Test]
	public void Without_any_usable_maximum_the_ring_spans_a_hundred_above_the_minimum()
	{
		var face = Resolve(60, new GaugeConfig { Id = "g", Variable = Metric, Min = 10 }, declaredMax: 5);

		Assert.That(face.Level, Is.EqualTo(0.5).Within(0.0001));
	}

	[Test]
	public void Equal_bounds_never_produce_a_level_that_is_not_a_number()
	{
		var face = Resolve(0, new GaugeConfig { Id = "g", Variable = Metric, Min = 0, Max = 0 });

		Assert.Multiple(() =>
		{
			Assert.That(double.IsFinite(face.Level), Is.True);
			Assert.That(face.Level, Is.Zero);
		});
	}

	[Test]
	public void The_value_is_formatted_with_the_variables_own_unit_and_precision()
	{
		var face = Resolve(21.456, new GaugeConfig { Id = "g", Variable = Metric }, decimals: 1, unit: "°C");

		Assert.Multiple(() =>
		{
			Assert.That(face.Value, Is.EqualTo("21.5"));
			Assert.That(face.HasUnit, Is.True);
		});
	}

	[Test]
	public void A_missing_variable_reads_as_a_dash_with_an_empty_ring()
	{
		var resolver = new GaugesViewStateResolver(new VariableRegistry());

		var face = resolver.Resolve(new GaugeConfig
			{ Id = "g", Variable = "nope", WarnWhen = GaugeConfig.WarnBelow, WarnAt = 20, Color = "#123456" });

		Assert.Multiple(() =>
		{
			Assert.That(face.Value, Is.EqualTo(GaugesViewStateResolver.Unavailable));
			Assert.That(face.Level, Is.Zero);
			Assert.That(face.Color, Is.EqualTo("#123456"), "a gauge with no reading must not raise a below warning");
		});
	}

	[TestCase(GaugeConfig.WarnAbove, 90, 90, true)]
	[TestCase(GaugeConfig.WarnAbove, 89, 90, false)]
	[TestCase(GaugeConfig.WarnBelow, 20, 20, true)]
	[TestCase(GaugeConfig.WarnBelow, 21, 20, false)]
	public void Crossing_the_threshold_turns_the_ring_the_warning_colour(string when,
		double value,
		double threshold,
		bool warns)
	{
		var face = Resolve(value,
			new GaugeConfig { Id = "g", Variable = Metric, Color = "#00ff00", WarnWhen = when, WarnAt = threshold });

		Assert.That(face.Color, Is.EqualTo(warns ? GaugesViewStateResolver.WarningColor : "#00ff00"));
	}

	[TestCase(GaugeConfig.WarnBelow, 0, true)]
	[TestCase(GaugeConfig.WarnBelow, 1, false)]
	[TestCase(GaugeConfig.WarnAbove, 0, true)]
	public void A_direction_without_a_stored_threshold_warns_at_the_zero_the_editor_shows(string when,
		double value,
		bool warns)
	{
		var face = Resolve(value, new GaugeConfig { Id = "g", Variable = Metric, WarnWhen = when });

		Assert.That(face.Color, Is.EqualTo(warns ? GaugesViewStateResolver.WarningColor : null));
	}

	[Test]
	public void A_threshold_without_a_direction_never_warns()
	{
		var face = Resolve(99, new GaugeConfig { Id = "g", Variable = Metric, WarnAt = 10 });

		Assert.That(face.Color, Is.Null);
	}

	[Test]
	public void A_name_with_variables_is_rendered_and_a_plain_name_is_kept()
	{
		var registry = Registry(42);
		registry.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "cpu_name",
			Scope = VariableScope.Global,
			Type = VariableType.Text,
			Classification = VariableClassification.User,
			Value = "M3 Max",
			UpdatedAt = DateTime.UtcNow,
		});
		var resolver = new GaugesViewStateResolver(registry);

		Assert.Multiple(() =>
		{
			Assert.That(resolver.Resolve(new GaugeConfig { Id = "g", Variable = Metric, Name = "CPU {{ vars.cpu_name }}" }).Name,
				Is.EqualTo("CPU M3 Max"));
			Assert.That(resolver.Resolve(new GaugeConfig { Id = "g", Variable = Metric, Name = "CPU" }).Name, Is.EqualTo("CPU"));
			Assert.That(resolver.Resolve(new GaugeConfig { Id = "g", Variable = "missing", Name = "{{ vars.cpu_name }}" }).Name,
				Is.EqualTo("M3 Max"), "the name renders even while the gauge has no reading");
		});
	}

	[Test]
	public void A_widget_scoped_variable_wins_over_a_global_one_of_the_same_name()
	{
		var widgetId = Guid.NewGuid().ToString();
		var registry = Registry(10);
		registry.Upsert(Variable(80, VariableScope.Widget, widgetId));

		var face = new GaugesViewStateResolver(registry, widgetId)
			.Resolve(new GaugeConfig { Id = "g", Variable = Metric, Max = 100 });

		Assert.That(face.Level, Is.EqualTo(0.8).Within(0.0001));
	}

	private static GaugeFace Resolve(double value,
		GaugeConfig gauge,
		double? declaredMax = null,
		int? decimals = 0,
		string? unit = null)
		=> new GaugesViewStateResolver(Registry(value, declaredMax, decimals, unit)).Resolve(gauge);

	private static VariableRegistry Registry(double value,
		double? declaredMax = null,
		int? decimals = 0,
		string? unit = null)
	{
		var registry = new VariableRegistry();
		registry.Upsert(Variable(value, VariableScope.Global, null, declaredMax, decimals, unit));

		return registry;
	}

	private static VariableEntity Variable(double value,
		VariableScope scope,
		string? scopeRefId,
		double? declaredMax = null,
		int? decimals = 0,
		string? unit = null)
		=> new()
		{
			Id = Guid.NewGuid(),
			Name = Metric,
			Scope = scope,
			ScopeRefId = scopeRefId,
			Type = VariableType.Numeric,
			Classification = VariableClassification.User,
			Value = value.ToString(CultureInfo.InvariantCulture),
			DecimalPlaces = decimals,
			Unit = unit,
			Max = declaredMax,
			UpdatedAt = DateTime.UtcNow,
		};
}
