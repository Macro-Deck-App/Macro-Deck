using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using static MacroDeckHost.Tests.UnitTests.Widgets.Ui.HistoryGraphTestSupport;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// What the card has to end up showing, from the configuration and the variables it names. The scale is
/// the substance here: a graph whose height does not mean what the user configured is worse than no
/// graph, because it still looks like one.
/// </summary>
[TestFixture]
public class HistoryGraphViewStateResolverTests
{
	private static readonly object _percent = new { valueVariable = Metric, maxValue = 100 };

	private static readonly double[] _rising = [10d, 20d, 30d];

	private static readonly double[] _flat = [42d, 42d, 42d];

	private static readonly double[] _overflowing = [50d, 140d];

	private static readonly double[] _nothing = [];

	private static readonly double[] _lowerHalf = [0.1, 0.2, 0.3];

	private static readonly double[] _wholeBand = [0d, 0.5, 1d];

	private static readonly double[] _midBand = [0.5, 0.5, 0.5];

	private static readonly double[] _clampedToCeiling = [0.5, 1d];

	[Test]
	public void A_configured_maximum_pins_the_scale_rather_than_the_window_deciding_it()
	{
		var resolver = Resolver(_percent);

		// Every sample is well inside 0..100, so a resolver that auto-scaled would spread these three
		// across the whole band instead of leaving them in its lower half.
		var points = resolver.Resolve(_rising).Points;

		Assert.That(points, Is.EqualTo(_lowerHalf).AsCollection);
	}

	[Test]
	public void Without_a_maximum_the_window_decides_the_scale()
	{
		var resolver = Resolver(new { valueVariable = Metric });

		var points = resolver.Resolve(_rising).Points;

		Assert.That(points,
			Is.EqualTo(_wholeBand).AsCollection,
			"an open-ended metric is only readable when the window fills the band");
	}

	[Test]
	public void A_flat_window_sits_at_half_height_rather_than_at_either_edge()
	{
		var resolver = Resolver(new { valueVariable = Metric });

		var points = resolver.Resolve(_flat).Points;

		Assert.That(points,
			Is.EqualTo(_midBand).AsCollection,
			"a value that has not moved is no nearer the top of its range than the bottom");
	}

	[Test]
	public void A_sample_beyond_a_configured_maximum_flattens_against_the_ceiling()
	{
		var resolver = Resolver(_percent);

		var points = resolver.Resolve(_overflowing).Points;

		Assert.That(points,
			Is.EqualTo(_clampedToCeiling).AsCollection,
			"a sample that outgrows a fixed scale must not be dropped, nor drawn outside the band");
	}

	[Test]
	public void An_empty_window_produces_no_points_at_all()
	{
		Assert.That(Resolver(_percent).Resolve(_nothing).Points,
			Is.Empty,
			"a graph with no history draws nothing - a flat line along the foot would read as a real zero");
	}

	[TestCase("73", 0, "73")]
	[TestCase("73.456", 2, "73.46")]
	[TestCase("73.4", 0, "73")]
	public void The_value_is_formatted_to_the_variables_own_decimal_places(string stored, int places, string expected)
	{
		var resolver = Resolver(_percent, Registry(stored, places));

		Assert.That(resolver.Resolve(_nothing).Value, Is.EqualTo(expected));
	}

	[Test]
	public void A_variable_that_is_not_reporting_reads_as_a_dash_rather_than_a_zero()
	{
		var resolver = Resolver(new { valueVariable = "nothing_publishes_this", maxValue = 100 });

		Assert.That(resolver.Resolve(_nothing).Value, Is.EqualTo("-"));
	}

	[Test]
	public void A_configured_maximum_reserves_room_for_its_widest_reading()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Resolver(_percent).Resolve(_nothing).Digits,
				Is.EqualTo(3),
				"100 needs three digits, so 99 must not sit where 100 would not fit");
			Assert.That(Resolver(new { valueVariable = Metric, maxValue = 100 }, Registry("7.25", 2))
					.Resolve(_nothing).Digits,
				Is.EqualTo(3 + 2 + 0.5),
				"a separator takes room too, but materially less than a digit");
		});
	}

	[Test]
	public void The_reservation_never_shrinks_once_a_wider_reading_has_been_seen()
	{
		var variables = Registry("100");
		var resolver = Resolver(new { valueVariable = Metric }, variables);

		var wide = resolver.Resolve(_nothing).Digits;

		var metric = variables.GetAll().Single(variable => variable.Name == Metric);
		metric.Value = "7";
		variables.Upsert(metric);

		Assert.Multiple(() =>
		{
			Assert.That(wide, Is.EqualTo(3), "an unbounded metric reserves what it has actually shown");
			Assert.That(resolver.Resolve(_nothing).Digits,
				Is.EqualTo(3),
				"the width must not be given back, or the value shuffles sideways as the metric moves");
			Assert.That(Resolver(new { valueVariable = Metric }, variables).Resolve(_nothing).Digits,
				Is.EqualTo(1),
				"a card that has only ever shown one digit reserves one");
		});
	}

	[Test]
	public void The_subtitle_is_freetext_that_can_carry_a_variable_of_any_type()
	{
		var variables = Registry().WithVariable("room", "Living Room");

		Assert.Multiple(() =>
		{
			Assert.That(Resolver(new
							{ valueVariable = Metric, subtitle = "Current value: {{ vars.system_cpu_usage_percent }}" },
						variables)
					.Resolve(_nothing).Subtitle,
				Is.EqualTo("Current value: 73"),
				"a numeric variable is what the issue asks the subtitle to be able to show");
			Assert.That(Resolver(new
							{ valueVariable = Metric, subtitle = "{{ vars.room }} / {{ vars.system_cpu_name }}" },
						variables)
					.Resolve(_nothing).Subtitle,
				Is.EqualTo("Living Room / Apple M3 Max"));
			Assert.That(Resolver(new { valueVariable = Metric, subtitle = "Plain words" }).Resolve(_nothing).Subtitle,
				Is.EqualTo("Plain words"));
		});
	}

	[Test]
	public void A_numeric_subtitle_variable_keeps_the_decimal_places_it_declares()
	{
		var variables = Registry().WithVariable("load", "7.25", VariableType.Numeric, decimalPlaces: 1);

		Assert.That(Resolver(new { valueVariable = Metric, subtitle = "{{ vars.load }}" }, variables)
				.Resolve(_nothing).Subtitle,
			Is.EqualTo("7.3"));
	}

	[Test]
	public void A_subtitle_resolves_the_variables_scoped_to_the_widget_it_is_drawn_for()
	{
		const string widgetId = "11111111-1111-1111-1111-111111111111";
		var variables = Registry()
			.WithVariable("brightness",
				"80",
				VariableType.Numeric,
				scope: VariableScope.Widget,
				scopeRefId: widgetId);
		var config = new { valueVariable = Metric, subtitle = "{{ vars.brightness }}" };

		Assert.Multiple(() =>
		{
			Assert.That(Resolver(config, variables, widgetId).Resolve(_nothing).Subtitle, Is.EqualTo("80"));
			Assert.That(Resolver(config, variables).Resolve(_nothing).Subtitle,
				Is.Empty,
				"a widget-scoped variable is nobody else's to read");
		});
	}

	[Test]
	public void A_profile_that_predates_the_freetext_subtitle_still_shows_its_variable()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Resolver(new { valueVariable = Metric, subtitleVariable = Caption }).Resolve(_nothing).Subtitle,
				Is.EqualTo("Apple M3 Max"));
			Assert.That(Resolver(new { valueVariable = Metric, subtitleVariable = Caption, subtitle = "" })
					.Resolve(_nothing).Subtitle,
				Is.Empty,
				"a subtitle the user cleared stays cleared, rather than the older key resurrecting it");
			Assert.That(Resolver(new
						{ valueVariable = Metric, subtitleVariable = "not a name }} {{", subtitle = (string?)null })
					.Resolve(_nothing).Subtitle,
				Is.Empty,
				"a legacy key holding something that is not a variable name is not interpolated");
			Assert.That(Resolver(new { valueVariable = Metric, subtitleVariable = Caption, subtitle = "Mine" })
					.Resolve(_nothing).Subtitle,
				Is.EqualTo("Mine"),
				"the freetext field is the one the editor writes, so it decides over the key it replaced");
		});
	}

	[Test]
	public void A_subtitle_naming_a_variable_that_has_gone_stale_says_so_rather_than_going_blank()
	{
		var variables = Registry();
		var caption = variables.GetAll().First(variable => variable.Name == Caption);
		variables.SetAvailable(caption.Id, false);

		Assert.That(Resolver(new { valueVariable = Metric, subtitle = "on {{ vars.system_cpu_name }}" }, variables)
				.Resolve(_nothing).Subtitle,
			Is.EqualTo($"on {VariableTemplateRenderer.UnavailablePlaceholder}"),
			"a provider that stopped reporting is worth showing, the way every other template shows it");
	}

	[Test]
	public void A_subtitle_naming_a_variable_that_is_not_reporting_says_so_rather_than_emptying_the_line()
	{
		var variables = Registry(caption: null);

		Assert.That(Resolver(new { valueVariable = Metric, subtitle = "on {{ vars.system_cpu_name }}" }, variables)
				.Resolve(_nothing).Subtitle,
			Is.EqualTo("on "),
			"a name the registry does not know renders as nothing, the way every other template reads it");
	}

	[Test]
	public void A_subtitle_that_is_not_a_valid_template_shows_itself_instead_of_faulting_the_sample_tick()
	{
		var subtitle = "100% {{ of max";
		var resolver = Resolver(new { valueVariable = Metric, subtitle });

		Assert.That(() => resolver.Resolve(_nothing), Throws.Nothing);
		Assert.That(resolver.Resolve(_nothing).Subtitle, Is.EqualTo(subtitle));
	}

	[Test]
	public void A_subtitle_switched_off_stays_off_however_well_its_variable_resolves()
	{
		Assert.That(Resolver(new
					{ valueVariable = Metric, subtitle = "{{ vars.system_cpu_name }}", showSubtitle = false })
				.Resolve(_nothing).Subtitle,
			Is.Empty);
	}

	/// <summary>
	/// ADR 0081 retired <c>HistoryGraphWidgetData.Unit</c>: the unit is a property of the value, so the card
	/// reads it from whichever variable it is bound to and follows a provider that revises it - a plugin
	/// update or a reconnect - without the widget being reconfigured. A per-widget copy would have gone on
	/// showing the old symbol beside the new number.
	/// </summary>
	[Test]
	public void The_unit_follows_the_bound_variable_rather_than_the_widget()
	{
		var variables = Registry(unit: "%");
		var resolver = Resolver(new { valueVariable = Metric }, variables);

		var before = resolver.Resolve(_nothing).Unit;

		var metric = variables.GetAll().Single(variable => variable.Name == Metric);
		metric.Unit = "°C";
		variables.Upsert(metric);

		Assert.Multiple(() =>
		{
			Assert.That(before.Literal, Is.EqualTo("%"));
			Assert.That(resolver.Resolve(_nothing).Unit.Literal, Is.EqualTo("°C"));
			Assert.That(Resolver(new { valueVariable = Metric }, Registry()).Resolve(_nothing).Unit.IsEmpty,
				Is.True,
				"a variable that declares no unit renders none, rather than the last one any graph showed");
		});
	}

	/// <summary>
	/// ADR 0081 confines attribute paths to template text: everywhere a variable is <em>stored</em> as a
	/// reference the stored key is a plain name, and the graph's <c>valueVariable</c> is one of those
	/// places. Teaching this resolver to split on a dot would make an attribute path silently plottable
	/// here and nowhere else, and would turn <c>unit</c> from a property of the variable back into a
	/// per-widget setting.
	/// </summary>
	[Test]
	public void An_attribute_path_is_not_a_variable_reference()
	{
		var state = Resolver(new { valueVariable = Metric + ".unit", maxValue = 100 }, Registry(unit: "%"))
			.Resolve(_nothing);

		Assert.Multiple(() =>
		{
			Assert.That(state.Value, Is.EqualTo("-"));
			Assert.That(state.Unit.IsEmpty, Is.True);
		});
	}
}
