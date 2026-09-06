using MacroDeckHost.Integrations.System.Notifications;
using MacroDeckHost.Tests.UnitTests.Delegation;

namespace MacroDeckHost.Tests.UnitTests.System;

public class ShellNotificationBridgeTests
{
	private static readonly TimeSpan Hold = TimeSpan.FromSeconds(20);

	private FakeTimeProvider _time = null!;
	private ShellNotificationBridge _bridge = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new FakeTimeProvider();
		_bridge = new ShellNotificationBridge(_time);
	}

	[Test]
	public async Task Without_a_shell_the_caller_is_told_so_before_any_time_passes()
	{
		var dispatch = _bridge.TryDispatchAsync("Title", "Message");

		Assert.That(dispatch.IsCompleted, Is.True);
		Assert.That(await dispatch, Is.False);
		Assert.That(_bridge.IsAttached, Is.False);
	}

	[Test]
	public async Task A_shell_that_stopped_polling_is_no_longer_used()
	{
		await Attach();
		Assert.That(_bridge.IsAttached, Is.True);

		_time.Advance(ShellNotificationBridge.AttachmentWindow - TimeSpan.FromSeconds(1));
		Assert.That(_bridge.IsAttached, Is.True);

		_time.Advance(TimeSpan.FromSeconds(2));

		Assert.That(_bridge.IsAttached, Is.False);
		Assert.That(await _bridge.TryDispatchAsync("Title", "Message"), Is.False);
	}

	[Test]
	public async Task A_dispatched_notification_reaches_the_shell_and_the_shell_decides_the_outcome()
	{
		await Attach();

		var dispatch = _bridge.TryDispatchAsync("A plugin wants to pair", "Example Plugin");
		var polled = await _bridge.WaitAsync(Hold);

		Assert.That(polled, Has.Count.EqualTo(1));
		Assert.That(polled[0].Title, Is.EqualTo("A plugin wants to pair"));
		Assert.That(polled[0].Message, Is.EqualTo("Example Plugin"));

		_bridge.Report(polled[0].Id, shown: true);

		Assert.That(await dispatch, Is.True);
	}

	[Test]
	public async Task A_shell_that_could_not_show_it_sends_the_caller_to_the_fallback()
	{
		await Attach();

		var dispatch = _bridge.TryDispatchAsync("Title", "Message");
		var polled = await _bridge.WaitAsync(Hold);
		_bridge.Report(polled[0].Id, shown: false);

		Assert.That(await dispatch, Is.False);
	}

	[Test]
	public async Task A_silent_shell_sends_the_caller_to_the_fallback_once_the_lease_expires()
	{
		await Attach();

		var dispatch = _bridge.TryDispatchAsync("Title", "Message");
		await _bridge.WaitAsync(Hold);

		_time.Advance(ShellNotificationBridge.Lease - TimeSpan.FromSeconds(1));
		Assert.That(dispatch.IsCompleted, Is.False);

		_time.Advance(TimeSpan.FromSeconds(2));

		Assert.That(await dispatch, Is.False);
	}

	[Test]
	public async Task The_shell_gets_its_full_lease_from_the_moment_it_receives_a_notification()
	{
		await Attach();

		var dispatch = _bridge.TryDispatchAsync("Title", "Message");
		_time.Advance(ShellNotificationBridge.Lease - TimeSpan.FromSeconds(1));

		var polled = await _bridge.WaitAsync(Hold);
		Assert.That(polled, Has.Count.EqualTo(1));

		_time.Advance(ShellNotificationBridge.Lease - TimeSpan.FromSeconds(1));
		_bridge.Report(polled[0].Id, shown: true);

		Assert.That(await dispatch, Is.True);
	}

	[Test]
	public async Task A_notification_the_caller_gave_up_on_is_not_shown_by_the_shell_afterwards()
	{
		await Attach();

		var dispatch = _bridge.TryDispatchAsync("Title", "Message");
		_time.Advance(ShellNotificationBridge.Lease + TimeSpan.FromSeconds(1));
		Assert.That(await dispatch, Is.False);

		var poll = _bridge.WaitAsync(TimeSpan.FromSeconds(2));
		_time.Advance(TimeSpan.FromSeconds(3));

		Assert.That(await poll, Is.Empty);
	}

	[Test]
	public async Task A_second_caller_cannot_take_notifications_away_from_the_shell()
	{
		await Attach();

		var poll = _bridge.WaitAsync(Hold);

		Assert.ThrowsAsync<InvalidOperationException>(async () => await _bridge.WaitAsync(Hold));

		var dispatch = _bridge.TryDispatchAsync("Title", "Message");
		var polled = await poll;

		Assert.That(polled, Has.Count.EqualTo(1));
		_bridge.Report(polled[0].Id, shown: true);
		Assert.That(await dispatch, Is.True);
	}

	[Test]
	public async Task An_empty_poll_still_counts_as_a_shell_being_there()
	{
		var poll = _bridge.WaitAsync(TimeSpan.FromSeconds(2));
		_time.Advance(TimeSpan.FromSeconds(3));

		Assert.That(await poll, Is.Empty);
		Assert.That(_bridge.IsAttached, Is.True);
	}

	[Test]
	public async Task Past_the_queue_bound_the_newest_caller_falls_back_and_the_queued_ones_survive()
	{
		await Attach();

		var dispatches = new List<Task<bool>>();
		for (var i = 0; i < 33; i++)
		{
			dispatches.Add(_bridge.TryDispatchAsync($"Title {i}", $"Message {i}"));
		}

		Assert.That(await dispatches[32], Is.False);

		var polled = await _bridge.WaitAsync(Hold);

		Assert.That(polled, Has.Count.EqualTo(32));
		Assert.That(polled[0].Title, Is.EqualTo("Title 0"));
	}

	private async Task Attach()
	{
		using var abandoned = new CancellationTokenSource();
		await abandoned.CancelAsync();
		await _bridge.WaitAsync(Hold, abandoned.Token);
	}
}
