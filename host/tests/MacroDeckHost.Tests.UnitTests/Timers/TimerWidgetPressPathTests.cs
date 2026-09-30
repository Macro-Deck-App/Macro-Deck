using MacroDeck.Sdk.Profiles;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Timers;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Timers;

public class TimerWidgetPressPathTests
{
	private TimerWidgetHarness _harness = null!;
	private ExecuteActionButtonTriggerRequestMessageHandler _handler = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new TimerWidgetHarness();
		_handler = new ExecuteActionButtonTriggerRequestMessageHandler(_harness.Folders,
			new NoProfiles(),
			_harness.LockState,
			_harness.Triggers,
			new WidgetTypeRegistry(new RecordingMediator()),
			new NoDefaultShortPress(),
			_harness.Coordinator);
	}

	[TearDown]
	public async Task TearDown() => await _harness.DisposeAsync();

	[Test]
	public async Task A_hardware_or_rest_short_press_starts_a_fixed_countdown_and_is_accepted()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 30);
		var deviceId = Guid.NewGuid();

		var response = await _handler.Handle(Request(widget, WidgetTriggerTypes.ShortPress, deviceId: deviceId),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(response.Status, Is.EqualTo(ActionExecutionStatus.Accepted));
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Running));
			Assert.That(_harness.Triggers.Calls.Single().DeviceId, Is.EqualTo(deviceId));
		});
	}

	[Test]
	public async Task A_hardware_long_press_resets_a_running_stopwatch()
	{
		var widget = await _harness.AddStopwatchAsync();
		await _harness.PressAsync(widget);

		var response = await _handler.Handle(Request(widget, WidgetTriggerTypes.LongPress), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Idle));
		});
	}

	[Test]
	public async Task The_touch_phases_of_a_hardware_press_are_accepted_without_moving_the_timer()
	{
		var widget = await _harness.AddCountdownAsync();

		var start = await _handler.Handle(Request(widget, WidgetTriggerTypes.TouchStart), CancellationToken.None);
		var end = await _handler.Handle(Request(widget, WidgetTriggerTypes.TouchEnd), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(start.Success && end.Success, Is.True);
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Idle));
		});
	}

	[Test]
	public async Task An_ask_mode_press_from_hardware_with_nothing_entered_yet_is_accepted_and_changes_nothing()
	{
		var widget = await _harness.AddCountdownAsync(ask: true);

		var response = await _handler.Handle(Request(widget, WidgetTriggerTypes.ShortPress, clientId: null,
			deviceId: Guid.NewGuid()), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Idle));
		});
	}

	[Test]
	public async Task A_press_while_the_host_is_locked_is_refused_as_locked()
	{
		var widget = await _harness.AddCountdownAsync();
		_harness.LockState.IsLocked = true;

		var response = await _handler.Handle(Request(widget, WidgetTriggerTypes.ShortPress), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error?.Code, Is.EqualTo(ActionExecutionErrorCodes.HostLocked));
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Idle));
		});
	}

	private static ExecuteActionButtonTriggerRequest Request(WidgetEntity widget,
		string triggerType,
		string? clientId = "client-1",
		Guid? deviceId = null)
		=> new()
		{
			TriggerType = triggerType,
			WidgetId = widget.Id.ToString(),
			FolderId = widget.FolderId.ToString(),
			ClientId = clientId,
			OriginDeviceId = deviceId,
		};

	private sealed class NoProfiles : IProfileRegistry
	{
		public IReadOnlyList<Profile> GetProfiles() => [];

		public IReadOnlyList<Folder> GetFoldersForProfile(string profileId) => [];

		public bool IsVirtual(string profileId) => false;

		public Task<bool> RouteWidgetInteraction(string folderId, string widgetId, WidgetInteraction interaction)
			=> Task.FromResult(false);
	}
}
