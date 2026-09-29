using MacroDeckHost.Application.Timers;
using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Tests.UnitTests.Timers;

public class CountdownAskModeTests
{
	private TimerWidgetHarness _harness = null!;

	[SetUp]
	public void SetUp() => _harness = new TimerWidgetHarness();

	[TearDown]
	public async Task TearDown() => await _harness.DisposeAsync();

	[Test]
	public async Task A_tap_asks_the_pressing_client_for_the_duration_and_its_answer_starts_the_countdown()
	{
		var widget = await _harness.AddCountdownAsync(ask: true);

		await _harness.PressAsync(widget, clientId: "tablet");
		var prompt = _harness.Prompt.Calls.Single();
		prompt.Answer.SetResult(90);
		await TimerWidgetHarness.EventuallyAsync(() => _harness.Phase(widget) == TimerWidgetPhase.Running);

		Assert.Multiple(() =>
		{
			Assert.That(prompt.ClientId, Is.EqualTo("tablet"));
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Running));
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.CountdownRemainingSeconds), Is.EqualTo("90"));
			Assert.That(_harness.Triggers.Calls.Single().Trigger, Is.EqualTo(WidgetTriggerTypes.CountdownStarted));
		});
	}

	[Test]
	public async Task Before_a_duration_is_entered_the_countdown_offers_no_remaining_time()
	{
		var widget = await _harness.AddCountdownAsync(ask: true);

		Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.CountdownRemainingSeconds), Is.EqualTo("0"));
	}

	[Test]
	public async Task A_cancelled_dialog_leaves_the_countdown_idle()
	{
		var widget = await _harness.AddCountdownAsync(ask: true);

		await _harness.PressAsync(widget, clientId: "tablet");
		_harness.Prompt.Calls.Single().Answer.SetResult(null);
		await TimerWidgetHarness.QuietPeriodAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Idle));
			Assert.That(_harness.Triggers.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task After_a_reset_or_a_dismiss_the_next_tap_asks_again_prefilled_with_the_last_duration()
	{
		var widget = await _harness.AddCountdownAsync(ask: true);
		await _harness.PressAsync(widget, clientId: "tablet");
		_harness.Prompt.Calls[0].Answer.SetResult(30);
		await TimerWidgetHarness.EventuallyAsync(() => _harness.Phase(widget) == TimerWidgetPhase.Running);
		await _harness.HoldAsync(widget, clientId: "tablet");

		await _harness.PressAsync(widget, clientId: "tablet");
		_harness.Prompt.Calls[1].Answer.SetResult(5);
		await TimerWidgetHarness.EventuallyAsync(() => _harness.Phase(widget) == TimerWidgetPhase.Running);
		await _harness.AdvanceAsync(TimeSpan.FromSeconds(5));
		await _harness.PressAsync(widget, clientId: "tablet");
		await _harness.AdvanceAsync(TimeSpan.FromSeconds(1));
		await _harness.PressAsync(widget, clientId: "tablet");

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Prompt.Calls, Has.Count.EqualTo(3));
			Assert.That(_harness.Prompt.Calls[1].InitialSeconds, Is.EqualTo(30));
			Assert.That(_harness.Prompt.Calls[2].InitialSeconds, Is.EqualTo(5));
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Idle));
		});
	}

	[Test]
	public async Task A_press_without_a_client_reuses_the_last_entered_duration()
	{
		var widget = await _harness.AddCountdownAsync(ask: true);
		await _harness.PressAsync(widget, clientId: "tablet");
		_harness.Prompt.Calls[0].Answer.SetResult(45);
		await TimerWidgetHarness.EventuallyAsync(() => _harness.Phase(widget) == TimerWidgetPhase.Running);
		await _harness.HoldAsync(widget);

		await _harness.PressAsync(widget, deviceId: Guid.NewGuid());

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Prompt.Calls, Has.Count.EqualTo(1));
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Running));
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.CountdownRemainingSeconds), Is.EqualTo("45"));
		});
	}

	[Test]
	public async Task A_press_without_a_client_and_nothing_entered_yet_does_nothing()
	{
		var widget = await _harness.AddCountdownAsync(ask: true);

		var accepted = await _harness.PressAsync(widget, deviceId: Guid.NewGuid());

		Assert.Multiple(() =>
		{
			Assert.That(accepted, Is.True);
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Idle));
			Assert.That(_harness.Prompt.Calls, Is.Empty);
			Assert.That(_harness.Triggers.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task Another_client_gets_its_own_dialog_and_starting_from_it_retires_the_first_one()
	{
		var widget = await _harness.AddCountdownAsync(ask: true);

		await _harness.PressAsync(widget, clientId: "phone");
		await _harness.AdvanceAsync(TimeSpan.FromSeconds(1));
		await _harness.PressAsync(widget, clientId: "tablet");
		var phone = _harness.Prompt.Calls[0];
		var tablet = _harness.Prompt.Calls[1];
		tablet.Answer.SetResult(60);
		await TimerWidgetHarness.EventuallyAsync(() => _harness.Phase(widget) == TimerWidgetPhase.Running);
		phone.Answer.TrySetResult(600);
		await TimerWidgetHarness.QuietPeriodAsync();

		Assert.Multiple(() =>
		{
			Assert.That(tablet.ClientId, Is.EqualTo("tablet"));
			Assert.That(phone.Cancellation.IsCancellationRequested, Is.True);
			Assert.That(_harness.Variable(widget, TimerWidgetVariableWriter.CountdownRemainingSeconds), Is.EqualTo("60"));
			Assert.That(_harness.Triggers.Calls, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_repeated_tap_from_the_same_client_replaces_its_dialog()
	{
		var widget = await _harness.AddCountdownAsync(ask: true);

		await _harness.PressAsync(widget, clientId: "tablet");
		await _harness.AdvanceAsync(TimeSpan.FromSeconds(1));
		await _harness.PressAsync(widget, clientId: "tablet");

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Prompt.Calls, Has.Count.EqualTo(2));
			Assert.That(_harness.Prompt.Calls[0].Cancellation.IsCancellationRequested, Is.True);
			Assert.That(_harness.Prompt.Calls[1].Cancellation.IsCancellationRequested, Is.False);
		});
	}

	[Test]
	public async Task An_answer_arriving_after_the_host_was_locked_does_not_start_the_countdown()
	{
		var widget = await _harness.AddCountdownAsync(ask: true);
		await _harness.PressAsync(widget, clientId: "tablet");

		_harness.LockState.IsLocked = true;
		_harness.Prompt.Calls.Single().Answer.SetResult(30);
		await TimerWidgetHarness.QuietPeriodAsync();

		Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Idle));
	}
}
