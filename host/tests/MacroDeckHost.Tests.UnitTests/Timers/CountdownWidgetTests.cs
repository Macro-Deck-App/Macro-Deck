using MacroDeckHost.Application.Timers;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Timers;

public class CountdownWidgetTests
{
	private TimerWidgetHarness _harness = null!;

	[SetUp]
	public void SetUp() => _harness = new TimerWidgetHarness();

	[TearDown]
	public async Task TearDown() => await _harness.DisposeAsync();

	[Test]
	public async Task A_placed_countdown_offers_its_duration_as_remaining_seconds_before_it_runs()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 90);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.CountdownRemainingSeconds), Is.EqualTo("90"));
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.CountdownRunning), Is.EqualTo("false"));
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.CountdownFinished), Is.EqualTo("false"));
		});
	}

	[Test]
	public async Task A_duration_set_as_hours_minutes_and_seconds_adds_up()
	{
		var widget = _harness.Folders.Add(Domain.Widgets.WidgetTypeIds.Countdown,
			"""{"durationHours":1,"durationMinutes":2,"durationSeconds":3}""");

		await _harness.Coordinator.SyncAsync(widget);

		Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.CountdownRemainingSeconds), Is.EqualTo("3723"));
	}

	[Test]
	public async Task A_short_press_starts_it_and_the_started_flow_already_sees_it_running()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 10);

		await _harness.PressAsync(widget, clientId: "tablet");

		var call = _harness.Triggers.Calls.Single();
		Assert.Multiple(() =>
		{
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Running));
			Assert.That(call.Trigger, Is.EqualTo(WidgetTriggerTypes.CountdownStarted));
			Assert.That(call.ClientId, Is.EqualTo("tablet"));
			Assert.That(call.VariablesSeen[TimerWidgetVariableWriter.CountdownRunning], Is.EqualTo("true"));
		});
	}

	[Test]
	public async Task Remaining_seconds_round_up_so_the_last_second_still_reads_one()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 10);
		await _harness.PressAsync(widget);

		await _harness.AdvanceAsync(TimeSpan.FromMilliseconds(300));
		var early = _harness.Variable(widget, TimerWidgetVariableWriter.CountdownRemainingSeconds);
		await _harness.AdvanceAsync(TimeSpan.FromMilliseconds(700));
		var afterOneSecond = _harness.Variable(widget, TimerWidgetVariableWriter.CountdownRemainingSeconds);
		await _harness.AdvanceAsync(TimeSpan.FromMilliseconds(8_500));
		var lastSecond = _harness.Variable(widget, TimerWidgetVariableWriter.CountdownRemainingSeconds);

		Assert.Multiple(() =>
		{
			Assert.That(early, Is.EqualTo("10"));
			Assert.That(afterOneSecond, Is.EqualTo("9"));
			Assert.That(lastSecond, Is.EqualTo("1"));
		});
	}

	[Test]
	public async Task It_finishes_exactly_at_its_duration_and_raises_the_finished_flow_once_as_the_host()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 10);
		await _harness.PressAsync(widget);

		await _harness.AdvanceAsync(TimeSpan.FromMilliseconds(9_999));
		var beforeEnd = _harness.Phase(widget);
		await _harness.AdvanceAsync(TimeSpan.FromMilliseconds(1));
		await _harness.AdvanceAsync(TimeSpan.FromSeconds(5));

		var finished = _harness.FlowExecutor.Calls.Where(call => call.Trigger == WidgetTriggerTypes.CountdownFinished)
			.ToList();
		Assert.Multiple(() =>
		{
			Assert.That(beforeEnd, Is.EqualTo(TimerWidgetPhase.Running));
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Finished));
			Assert.That(finished, Has.Count.EqualTo(1));
			Assert.That(_harness.FlowExecutor.Requests.Single().Origin, Is.EqualTo(ExecutionOrigin.Host));
			Assert.That(finished[0].VariablesSeen[TimerWidgetVariableWriter.CountdownFinished], Is.EqualTo("true"));
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.CountdownRemainingSeconds), Is.EqualTo("0"));
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.CountdownRunning), Is.EqualTo("false"));
		});
	}

	[Test]
	public async Task A_short_press_while_running_pauses_it_and_the_remaining_time_holds_until_resumed()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 10);
		await _harness.PressAsync(widget);
		await _harness.AdvanceAsync(TimeSpan.FromSeconds(3));

		await _harness.PressAsync(widget);
		await _harness.AdvanceAsync(TimeSpan.FromMinutes(1));
		var whilePaused = _harness.Variable(widget, TimerWidgetVariableWriter.CountdownRemainingSeconds);
		var runningWhilePaused = _harness.Variable(widget, TimerWidgetVariableWriter.CountdownRunning);

		await _harness.PressAsync(widget);
		await _harness.AdvanceAsync(TimeSpan.FromSeconds(2));

		Assert.Multiple(() =>
		{
			Assert.That(whilePaused, Is.EqualTo("7"));
			Assert.That(runningWhilePaused, Is.EqualTo("false"));
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.CountdownRemainingSeconds), Is.EqualTo("5"));
			Assert.That(_harness.Triggers.Calls.Select(call => call.Trigger), Is.EqualTo(new[]
			{
				WidgetTriggerTypes.CountdownStarted, WidgetTriggerTypes.CountdownPaused, WidgetTriggerTypes.CountdownStarted,
			}));
		});
	}

	[Test]
	public async Task A_long_press_resets_a_running_or_paused_countdown_to_its_full_duration()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 10);
		await _harness.PressAsync(widget);
		await _harness.AdvanceAsync(TimeSpan.FromSeconds(4));

		await _harness.HoldAsync(widget);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Idle));
			Assert.That(_harness.Triggers.Calls.Last().Trigger, Is.EqualTo(WidgetTriggerTypes.CountdownReset));
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.CountdownRemainingSeconds), Is.EqualTo("10"));
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.CountdownRunning), Is.EqualTo("false"));
		});
	}

	[Test]
	public async Task A_long_press_on_an_idle_countdown_does_nothing()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 10);

		await _harness.HoldAsync(widget);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Idle));
			Assert.That(_harness.Triggers.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task Tapping_a_finished_countdown_dismisses_it_back_to_its_full_duration()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 5);
		await _harness.PressAsync(widget);
		await _harness.AdvanceAsync(TimeSpan.FromSeconds(5));

		await _harness.PressAsync(widget);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Idle));
			Assert.That(_harness.Triggers.Calls.Last().Trigger, Is.EqualTo(WidgetTriggerTypes.CountdownDismissed));
			Assert.That(_harness.Triggers.Calls.Last().VariablesSeen[TimerWidgetVariableWriter.CountdownFinished],
				Is.EqualTo("false"));
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.CountdownRemainingSeconds), Is.EqualTo("5"));
		});
	}

	[Test]
	public async Task A_press_arriving_after_zero_but_before_the_next_tick_finishes_it_once_and_then_dismisses_it()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 5);
		await _harness.PressAsync(widget);
		_harness.Time.Now += TimeSpan.FromSeconds(6);

		await _harness.PressAsync(widget);
		await _harness.AdvanceAsync(TimeSpan.FromSeconds(1));

		Assert.Multiple(() =>
		{
			Assert.That(_harness.FlowExecutor.Calls.Count(call => call.Trigger == WidgetTriggerTypes.CountdownFinished),
				Is.EqualTo(1));
			Assert.That(_harness.Triggers.Calls.Last().Trigger, Is.EqualTo(WidgetTriggerTypes.CountdownDismissed));
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Idle));
		});
	}

	[Test]
	public async Task One_tap_that_reaches_the_host_twice_counts_once()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 10);

		await _harness.PressAsync(widget, clientId: "phone");
		_harness.Time.Now += TimeSpan.FromMilliseconds(120);
		await _harness.PressAsync(widget, clientId: "phone");

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Running));
			Assert.That(_harness.Triggers.Calls, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Taps_from_two_devices_within_the_repeat_window_both_count()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 10);

		await _harness.PressAsync(widget, clientId: "phone");
		_harness.Time.Now += TimeSpan.FromMilliseconds(120);
		await _harness.PressAsync(widget, clientId: "tablet");

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Paused));
			Assert.That(_harness.Triggers.Calls, Has.Count.EqualTo(2));
		});
	}

	[Test]
	public async Task A_tap_whose_origin_is_unknown_still_counts_as_the_same_tap()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 10);

		await _harness.PressAsync(widget, clientId: "phone");
		_harness.Time.Now += TimeSpan.FromMilliseconds(120);
		await _harness.PressAsync(widget);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Running));
			Assert.That(_harness.Triggers.Calls, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_second_tap_after_the_first_one_has_settled_pauses_the_countdown()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 10);

		await _harness.PressAsync(widget);
		_harness.Time.Now += TimerWidgetStore.RepeatWindow;
		await _harness.PressAsync(widget);

		Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Paused));
	}

	[Test]
	public async Task A_press_from_a_hardware_deck_carries_its_device_to_the_started_flow()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 10);
		var deviceId = Guid.NewGuid();

		await _harness.PressAsync(widget, deviceId: deviceId);

		Assert.That(_harness.Triggers.Calls.Single().DeviceId, Is.EqualTo(deviceId));
	}

	[Test]
	public async Task A_press_while_the_host_is_locked_changes_nothing_but_a_countdown_still_finishes()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 5);
		await _harness.PressAsync(widget);
		_harness.LockState.IsLocked = true;

		var accepted = await _harness.PressAsync(widget);
		await _harness.AdvanceAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(accepted, Is.False);
			Assert.That(_harness.Triggers.Calls.Select(call => call.Trigger),
				Is.EqualTo(new[] { WidgetTriggerTypes.CountdownStarted }));
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Finished));
			Assert.That(_harness.FlowExecutor.Calls.Single().Trigger, Is.EqualTo(WidgetTriggerTypes.CountdownFinished));
		});
	}

	[Test]
	public async Task Changing_the_duration_of_an_idle_countdown_updates_its_remaining_seconds_at_once()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 10);

		widget.Data = """{"mode":"fixed","durationMinutes":2}""";
		await _harness.Coordinator.SyncAsync(widget);

		Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.CountdownRemainingSeconds), Is.EqualTo("120"));
	}

	[Test]
	public async Task A_running_countdown_keeps_its_duration_when_the_widget_is_edited_until_it_is_reset()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 10);
		await _harness.PressAsync(widget);

		widget.Data = """{"mode":"fixed","durationMinutes":2}""";
		await _harness.Coordinator.SyncAsync(widget);
		await _harness.AdvanceAsync(TimeSpan.FromSeconds(1));
		var whileRunning = _harness.Variable(widget, TimerWidgetVariableWriter.CountdownRemainingSeconds);
		await _harness.HoldAsync(widget);

		Assert.Multiple(() =>
		{
			Assert.That(whileRunning, Is.EqualTo("9"));
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.CountdownRemainingSeconds), Is.EqualTo("120"));
		});
	}
}
