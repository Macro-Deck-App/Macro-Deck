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
