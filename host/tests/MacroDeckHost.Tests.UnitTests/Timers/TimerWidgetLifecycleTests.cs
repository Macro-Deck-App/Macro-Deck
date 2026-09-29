using MacroDeckHost.Application.Timers;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Timers;

public class TimerWidgetLifecycleTests
{
	private TimerWidgetHarness _harness = null!;

	[SetUp]
	public void SetUp() => _harness = new TimerWidgetHarness();

	[TearDown]
	public async Task TearDown() => await _harness.DisposeAsync();

	[Test]
	public async Task A_new_stopwatch_has_its_variables_as_soon_as_it_is_placed()
	{
		var widget = await _harness.AddStopwatchAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.StopwatchElapsedSeconds), Is.EqualTo("0"));
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.StopwatchRunning), Is.EqualTo("false"));
		});
	}

	[Test]
	public async Task Every_timer_already_on_a_deck_is_picked_up_when_the_host_starts()
	{
		var countdown = _harness.Folders.Add(WidgetTypeIds.Countdown, """{"durationMinutes":1}""");
		var stopwatch = _harness.Folders.Add(WidgetTypeIds.Stopwatch, "{}");

		await _harness.Coordinator.SyncAllAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Variable(countdown, TimerWidgetVariableWriter.CountdownRemainingSeconds), Is.EqualTo("60"));
			Assert.That(_harness.Variable(stopwatch, TimerWidgetVariableWriter.StopwatchRunning), Is.EqualTo("false"));
		});
	}

	[Test]
	public async Task A_deleted_countdown_stops_ticking_and_never_brings_its_variables_back()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 3);
		await _harness.PressAsync(widget);
		var lateWrite = _harness.Store.Get(widget.Id)! with { Version = long.MaxValue };

		_harness.Folders.Remove(widget);
		await _harness.Coordinator.ForgetAsync(widget.Id);
		await _harness.Variables.WriteAsync(lateWrite);
		await _harness.AdvanceAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Store.Contains(widget.Id), Is.False);
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.CountdownRemainingSeconds), Is.Null);
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.CountdownRunning), Is.Null);
			Assert.That(_harness.FlowExecutor.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task Turning_a_countdown_into_a_stopwatch_swaps_its_variables()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 3);

		widget.Type = WidgetTypeIds.Stopwatch;
		await _harness.Coordinator.SyncAsync(widget);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.CountdownRemainingSeconds), Is.Null);
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.StopwatchElapsedSeconds), Is.EqualTo("0"));
		});
	}

	[Test]
	public async Task A_countdown_in_a_deleted_profile_is_dropped_with_its_variables_and_never_finishes()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 3);
		await _harness.PressAsync(widget);

		_harness.Folders.Remove(widget);
		await _harness.Coordinator.PruneMissingAsync();
		await _harness.AdvanceAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Store.Contains(widget.Id), Is.False);
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.CountdownRemainingSeconds), Is.Null);
			Assert.That(_harness.FlowExecutor.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task A_timer_whose_widget_stops_being_a_timer_takes_its_variables_with_it()
	{
		var widget = await _harness.AddStopwatchAsync();

		widget.Type = WidgetTypeIds.ActionButton;
		await _harness.Coordinator.SyncAsync(widget);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Store.Contains(widget.Id), Is.False);
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.StopwatchElapsedSeconds), Is.Null);
		});
	}
}
