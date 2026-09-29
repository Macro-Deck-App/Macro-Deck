using MacroDeckHost.Application.Timers;
using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Tests.UnitTests.Timers;

public class StopwatchWidgetTests
{
	private TimerWidgetHarness _harness = null!;

	[SetUp]
	public void SetUp() => _harness = new TimerWidgetHarness();

	[TearDown]
	public async Task TearDown() => await _harness.DisposeAsync();

	[Test]
	public async Task Elapsed_time_counts_only_while_running_and_carries_across_pauses()
	{
		var widget = await _harness.AddStopwatchAsync();

		await _harness.PressAsync(widget);
		await _harness.AdvanceAsync(TimeSpan.FromSeconds(12));
		await _harness.PressAsync(widget);
		await _harness.AdvanceAsync(TimeSpan.FromMinutes(5));
		var whilePaused = _harness.Variable(widget, TimerWidgetVariableWriter.StopwatchElapsedSeconds);
		await _harness.PressAsync(widget);
		await _harness.AdvanceAsync(TimeSpan.FromMilliseconds(3_900));

		Assert.Multiple(() =>
		{
			Assert.That(whilePaused, Is.EqualTo("12"));
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.StopwatchElapsedSeconds), Is.EqualTo("15"));
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.StopwatchRunning), Is.EqualTo("true"));
			Assert.That(_harness.Triggers.Calls.Select(call => call.Trigger), Is.EqualTo(new[]
			{
				WidgetTriggerTypes.StopwatchStarted, WidgetTriggerTypes.StopwatchPaused, WidgetTriggerTypes.StopwatchStarted,
			}));
		});
	}

	[Test]
	public async Task The_paused_flow_sees_the_stopwatch_stopped()
	{
		var widget = await _harness.AddStopwatchAsync();
		await _harness.PressAsync(widget);
		await _harness.AdvanceAsync(TimeSpan.FromSeconds(1));

		await _harness.PressAsync(widget);

		Assert.That(_harness.Triggers.Calls.Last().VariablesSeen[TimerWidgetVariableWriter.StopwatchRunning],
			Is.EqualTo("false"));
	}

	[Test]
	public async Task A_long_press_resets_it_to_zero()
	{
		var widget = await _harness.AddStopwatchAsync();
		await _harness.PressAsync(widget);
		await _harness.AdvanceAsync(TimeSpan.FromSeconds(42));

		await _harness.HoldAsync(widget);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Idle));
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.StopwatchElapsedSeconds), Is.EqualTo("0"));
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.StopwatchRunning), Is.EqualTo("false"));
			Assert.That(_harness.Triggers.Calls.Last().Trigger, Is.EqualTo(WidgetTriggerTypes.StopwatchReset));
		});
	}

	[Test]
	public async Task A_long_press_on_a_stopwatch_at_zero_does_nothing()
	{
		var widget = await _harness.AddStopwatchAsync();

		await _harness.HoldAsync(widget);

		Assert.That(_harness.Triggers.Calls, Is.Empty);
	}
}
