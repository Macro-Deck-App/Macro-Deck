using System.Globalization;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Variables;

/// <summary>
/// The rolling window a graph is drawn from. Its whole reason to exist is that a variable's own change
/// notifications are the wrong clock: a flat metric still has to advance the graph, and a busy one must
/// not compress however many changes happened to arrive into one step.
/// </summary>
[TestFixture]
public class VariableHistoryTests
{
	private const string Name = "system_cpu_usage_percent";

	private static readonly double[] _fourSteady = [42d, 42d, 42d, 42d];

	private static readonly double[] _lastThree = [3d, 4d, 5d];

	private static readonly double[] _backfilled = [1d, 1d, 1d, 1d, 9d];

	[Test]
	public void The_window_advances_on_the_clock_even_while_the_value_stands_still()
	{
		var (history, time, _) = Create("42");

		using var window = history.Open(Name, capacity: 10);
		Advance(time, 3);

		Assert.That(window.Values,
			Is.EqualTo(_fourSteady).AsCollection,
			"the seed plus one sample per tick - a graph of a steady metric is a flat line, not a dot");
	}

	[Test]
	public void The_window_keeps_its_most_recent_values_and_no_more()
	{
		var (history, time, variables) = Create("0");

		using var window = history.Open(Name, capacity: 3);

		for (var value = 1; value <= 5; value++)
		{
			Set(variables, value.ToString(CultureInfo.InvariantCulture));
			Advance(time, 1);
		}

		Assert.That(window.Values, Is.EqualTo(_lastThree).AsCollection);
	}

	[Test]
	public void A_variable_that_is_not_reporting_contributes_nothing()
	{
		var (history, time, _) = Create("not a number");

		using var window = history.Open(Name, capacity: 10);
		Advance(time, 3);

		Assert.That(window.Values, Is.Empty, "a value that cannot be plotted must not be plotted as zero");
	}

	[Test]
	public void Sampling_stops_with_the_last_holder_and_the_gap_is_filled_when_one_returns()
	{
		var (history, time, variables) = Create("1");

		var first = history.Open(Name, capacity: 5);
		Advance(time, 1);
		first.Dispose();

		Set(variables, "9");
		Advance(time, 3);

		using var second = history.Open(Name, capacity: 5);

		Assert.That(second.Values,
			Is.EqualTo(_backfilled).AsCollection,
			"the value that was standing fills the gap on the original grid, then the live value lands now");
	}

	[Test]
	public void A_holder_hears_about_a_sample_that_changed_its_window()
	{
		var (history, time, _) = Create("42");

		using var window = history.Open(Name, capacity: 5);
		var raised = 0;
		window.Changed += (_, _) => raised++;

		Advance(time, 2);

		Assert.That(raised, Is.EqualTo(2));
	}

	[Test]
	public void Graphs_on_different_widgets_share_one_buffer_for_a_global_variable()
	{
		var (history, time, _) = Create("42");

		using var first = history.Open(Name, capacity: 10, scopeRefId: "widget-a");
		Advance(time, 2);
		using var second = history.Open(Name, capacity: 10, scopeRefId: "widget-b");

		Assert.That(second.Values,
			Is.EqualTo(first.Values).AsCollection,
			"a graph added later picks up the history the first one already built");
	}

	[Test]
	public void A_widgets_own_variable_gets_its_own_buffer_without_the_global_of_the_same_name()
	{
		var (history, time, variables) = Create("1");
		variables.Upsert(WidgetVariable("widget-a", "7"));

		using var own = history.Open(Name, capacity: 10, scopeRefId: "widget-a");
		using var global = history.Open(Name, capacity: 10);
		Advance(time, 2);

		Assert.Multiple(() =>
		{
			Assert.That(own.Values, Has.All.EqualTo(7d));
			Assert.That(global.Values, Has.All.EqualTo(1d));
			Assert.That(own.ScopeRefId, Is.EqualTo("widget-a"));
			Assert.That(global.ScopeRefId, Is.Null);
		});
	}

	[Test]
	public void A_deleted_widget_variable_never_feeds_the_global_into_that_widgets_buffer()
	{
		var (history, time, variables) = Create("1");
		var own = WidgetVariable("widget-a", "7");
		variables.Upsert(own);

		using var window = history.Open(Name, capacity: 10, scopeRefId: "widget-a");
		Advance(time, 1);
		variables.Remove(own.Id);
		Advance(time, 2);

		Assert.That(window.Values, Has.None.EqualTo(1d));
	}

	private static VariableEntity WidgetVariable(string widgetId, string value)
		=> new()
		{
			Id = Guid.NewGuid(),
			Name = Name,
			Scope = VariableScope.Widget,
			ScopeRefId = widgetId,
			Type = VariableType.Numeric,
			Classification = VariableClassification.User,
			Value = value,
			UpdatedAt = DateTime.UtcNow
		};

	private static (VariableHistory History, FakeTimeProvider Time, VariableRegistry Variables) Create(string value)
	{
		var variables = new VariableRegistry();
		var time = new FakeTimeProvider();

		Set(variables, value);

		return (new VariableHistory(variables, time), time, variables);
	}

	// One interval at a time: the fake timer fires once when it comes due rather than catching up on a
	// jump, so a single long advance would model three seconds passing with one sample taken.
	private static void Advance(FakeTimeProvider time, int ticks)
	{
		for (var tick = 0; tick < ticks; tick++)
		{
			time.Advance(VariableHistory.SampleInterval);
		}
	}

	private static void Set(VariableRegistry variables, string value)
	{
		var existing = variables.GetAll().FirstOrDefault(variable => variable.Name == Name);

		if (existing is not null)
		{
			existing.Value = value;
			variables.Upsert(existing);

			return;
		}

		variables.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = Name,
			Scope = VariableScope.Global,
			Type = VariableType.Numeric,
			Classification = VariableClassification.User,
			Value = value,
			UpdatedAt = DateTime.UtcNow,
		});
	}
}
