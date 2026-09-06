using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

/// <summary>
/// Interactions a device reports: how the host synthesizes the client's triggers from them, what it
/// refuses, and that it is the host - never the provider - that runs the widget's actions.
/// </summary>
[TestFixture]
internal sealed class DeviceInteractionTests
{
	private const string ButtonId = "w1";
	private const string OtherButtonId = "w2";

	private static readonly string[] _shortPressSequence = ["onTouchStart", "onTouchEnd", "onShortPress"];

	private static readonly string[] _longPressSequence = ["onTouchStart", "onLongPress", "onTouchEnd"];

	private static readonly string[] _cancelledSequence = ["onTouchStart", "onTouchEnd"];

	private static readonly string[] _heldSequence = ["onTouchStart", "onLongPress"];

	private static readonly string[] _touchStartOnly = ["onTouchStart"];

	private static readonly string[] _shortPressOnly = ["onShortPress"];

	private static readonly string[] _longPressOnly = ["onLongPress"];

	private DeviceSurfaceFixture _fixture = null!;
	private Guid _deviceId;

	[SetUp]
	public async Task SetUp()
	{
		_fixture = new DeviceSurfaceFixture();
		_fixture.Home.Widgets.AddRange([
			DeviceSurfaceFixture.Button(ButtonId, 0, 0), DeviceSurfaceFixture.Button(OtherButtonId, 1, 0)
		]);
		_fixture.Audio.Widgets.Add(DeviceSurfaceFixture.Button("elsewhere", 0, 0));
		_deviceId = await _fixture.OpenDeviceAsync();
	}

	[TearDown]
	public void TearDown() => _fixture.Dispose();

	private static DeviceInteraction Interaction(DeviceInteractionKind kind, string widgetId)
		=> new() { Kind = kind, Target = new DeviceInteractionTarget { WidgetId = widgetId } };

	private Task<DeviceInteractionOutcome> SubmitAsync(DeviceInteractionKind kind, string widgetId = ButtonId)
		=> _fixture.Service.SubmitInteractionAsync(_deviceId, Interaction(kind, widgetId));

	private async Task AdvanceAsync(int milliseconds)
	{
		_fixture.Time.Advance(TimeSpan.FromMilliseconds(milliseconds));
		await DeviceSurfaceFixture.DrainAsync();
	}

	[Test]
	public async Task A_press_released_before_the_long_press_threshold_is_a_short_press()
	{
		await SubmitAsync(DeviceInteractionKind.Press);
		await AdvanceAsync(200);
		await SubmitAsync(DeviceInteractionKind.Release);

		Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(_shortPressSequence));
	}

	[Test]
	public async Task A_press_held_past_the_threshold_is_a_long_press_and_never_also_a_short_one()
	{
		await SubmitAsync(DeviceInteractionKind.Press);
		await AdvanceAsync(600);
		await AdvanceAsync(900);
		await SubmitAsync(DeviceInteractionKind.Release);

		Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(_longPressSequence));
	}

	[Test]
	public async Task The_threshold_is_exactly_six_hundred_milliseconds()
	{
		await SubmitAsync(DeviceInteractionKind.Press);
		await AdvanceAsync(599);
		await SubmitAsync(DeviceInteractionKind.Release);
		var atFiveNinetyNine = _fixture.Triggers.TriggerTypes;

		_fixture.Triggers.Clear();
		await SubmitAsync(DeviceInteractionKind.Press);
		await AdvanceAsync(600);
		await SubmitAsync(DeviceInteractionKind.Release);
		var atSixHundred = _fixture.Triggers.TriggerTypes;

		Assert.Multiple(() =>
		{
			Assert.That(atFiveNinetyNine, Is.EqualTo(_shortPressSequence));
			Assert.That(atSixHundred, Is.EqualTo(_longPressSequence));
		});
	}

	[Test]
	public async Task A_press_that_is_never_released_fires_its_long_press_exactly_once()
	{
		await SubmitAsync(DeviceInteractionKind.Press);
		await AdvanceAsync(5000);

		Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(_heldSequence));
	}

	[Test]
	public async Task A_device_going_offline_ends_an_open_press_without_firing_it()
	{
		await SubmitAsync(DeviceInteractionKind.Press);
		await AdvanceAsync(200);

		await _fixture.Service.SetPresenceAsync(_deviceId, online: false);
		var atOffline = _fixture.Triggers.TriggerTypes;

		await AdvanceAsync(5000);

		Assert.Multiple(() =>
		{
			Assert.That(atOffline, Is.EqualTo(_cancelledSequence));
			Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(_cancelledSequence));
		});
	}

	[Test]
	public async Task Navigating_away_ends_an_open_press_without_firing_it()
	{
		await SubmitAsync(DeviceInteractionKind.Press);
		await AdvanceAsync(200);

		await _fixture.ChangeToAsync(_deviceId, DeviceSurfaceFixture.LightsFolderId);
		var atNavigation = _fixture.Triggers.TriggerTypes;

		await AdvanceAsync(5000);

		Assert.Multiple(() =>
		{
			Assert.That(atNavigation, Is.EqualTo(_cancelledSequence));
			Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(_cancelledSequence));
		});
	}

	[Test]
	public async Task Closing_the_session_ends_an_open_press_without_firing_it()
	{
		await SubmitAsync(DeviceInteractionKind.Press);
		await AdvanceAsync(200);

		await _fixture.Service.CloseAsync(_deviceId, "closed");
		var atClose = _fixture.Triggers.TriggerTypes;

		await AdvanceAsync(5000);

		Assert.Multiple(() =>
		{
			Assert.That(atClose, Is.EqualTo(_cancelledSequence));
			Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(_cancelledSequence));
		});
	}

	[Test]
	public async Task An_explicit_short_press_is_the_only_trigger_it_produces()
	{
		await SubmitAsync(DeviceInteractionKind.ShortPress);
		var immediately = _fixture.Triggers.TriggerTypes;

		await AdvanceAsync(5000);

		Assert.Multiple(() =>
		{
			Assert.That(immediately, Is.EqualTo(_shortPressOnly));
			Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(_shortPressOnly));
		});
	}

	[Test]
	public async Task An_explicit_long_press_arms_nothing_a_later_release_could_end()
	{
		await SubmitAsync(DeviceInteractionKind.LongPress);
		await SubmitAsync(DeviceInteractionKind.Release);

		Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(_longPressOnly));
	}

	[Test]
	public async Task A_release_on_one_widget_leaves_another_widgets_press_in_flight()
	{
		await SubmitAsync(DeviceInteractionKind.Press);
		await SubmitAsync(DeviceInteractionKind.Release, OtherButtonId);
		var afterForeignRelease = _fixture.Triggers.Triggers;

		await AdvanceAsync(600);

		Assert.Multiple(() =>
		{
			Assert.That(afterForeignRelease, Is.EqualTo(new[] { (ButtonId, "onTouchStart") }));
			Assert.That(_fixture.Triggers.Triggers,
				Is.EqualTo(new[] { (ButtonId, "onTouchStart"), (ButtonId, "onLongPress") }));
		});
	}

	[Test]
	public async Task A_second_press_on_a_held_widget_is_ignored_and_does_not_restart_its_timer()
	{
		await SubmitAsync(DeviceInteractionKind.Press);
		await AdvanceAsync(300);
		await SubmitAsync(DeviceInteractionKind.Press);
		var afterSecondPress = _fixture.Triggers.TriggerTypes;

		await AdvanceAsync(300);

		Assert.Multiple(() =>
		{
			Assert.That(afterSecondPress, Is.EqualTo(_touchStartOnly));
			Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(_heldSequence));
		});
	}

	[Test]
	public async Task An_encoder_turn_is_taken_but_runs_nothing_and_leaves_the_session_open()
	{
		var outcome = await SubmitAsync(DeviceInteractionKind.EncoderTurn);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Unsupported, Is.True);
			Assert.That(outcome.ErrorCode, Is.Null);
			Assert.That(outcome.ToResult().Status, Is.EqualTo(DeviceInteractionStatus.NotSupported));
			Assert.That(_fixture.Triggers.Triggers, Is.Empty);
			Assert.That(_fixture.Service.IsOpen(_deviceId), Is.True);
		});
	}

	[Test]
	public async Task A_widget_from_another_folder_is_refused_without_ending_the_session()
	{
		var refused = await SubmitAsync(DeviceInteractionKind.Press, "elsewhere");
		var afterRefusal = _fixture.Triggers.Triggers;

		var accepted = await SubmitAsync(DeviceInteractionKind.Press);

		Assert.Multiple(() =>
		{
			Assert.That(refused.ErrorCode, Is.EqualTo(DeviceSessionReasons.WidgetNotOnSurface));
			Assert.That(afterRefusal, Is.Empty);
			Assert.That(accepted.ErrorCode, Is.Null);
			Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(_touchStartOnly));
			Assert.That(_fixture.Service.IsOpen(_deviceId), Is.True);
		});
	}

	[Test]
	public async Task A_widget_left_behind_by_a_navigation_is_refused()
	{
		await _fixture.ChangeToAsync(_deviceId, DeviceSurfaceFixture.LightsFolderId);

		var refused = await SubmitAsync(DeviceInteractionKind.Press);

		Assert.Multiple(() =>
		{
			Assert.That(refused.ErrorCode, Is.EqualTo(DeviceSessionReasons.WidgetNotOnSurface));
			Assert.That(_fixture.Triggers.Triggers, Is.Empty);
		});
	}

	[Test]
	public async Task A_widget_pinned_into_the_current_folder_can_be_pressed()
	{
		_fixture.Audio.Widgets.Add(DeviceSurfaceFixture.Button("pinned",
			2,
			1,
			isPinned: true,
			pinScope: PinScope.Profile));
		await _fixture.MutateAsync(() => { });

		var outcome = await SubmitAsync(DeviceInteractionKind.Press, "pinned");

		Assert.Multiple(() =>
		{
			Assert.That(outcome.ErrorCode, Is.Null);
			Assert.That(_fixture.Triggers.Triggers, Is.EqualTo(new[] { ("pinned", "onTouchStart") }));

			// Its own folder, not the one it is being displayed in: the trigger pipeline resolves a widget
			// under the folder that owns it, and addressing the rendered folder refuses the press outright.
			Assert.That(_fixture.Triggers.Requests.Single().FolderId,
				Is.EqualTo(DeviceSurfaceFixture.AudioFolderId));
		});
	}

	[Test]
	public async Task The_host_runs_the_trigger_itself_under_the_devices_own_origin()
	{
		await SubmitAsync(DeviceInteractionKind.ShortPress);

		var request = _fixture.Triggers.Requests.Single();
		Assert.Multiple(() =>
		{
			Assert.That(request.WidgetId, Is.EqualTo(ButtonId));
			Assert.That(request.FolderId, Is.EqualTo(DeviceSurfaceFixture.HomeFolderId));
			Assert.That(request.TriggerType, Is.EqualTo("onShortPress"));
			Assert.That(request.OriginDeviceId, Is.EqualTo(_deviceId));
			Assert.That(request.ClientId,
				Is.Null,
				"the device origin is minted from the device id, never carried as a client id");
		});
	}

	[Test]
	public async Task A_navigation_run_by_a_press_moves_only_the_pressing_device()
	{
		var deckB = await _fixture.OpenDeviceAsync("DeckB");
		var pushesToB = _fixture.Provider.PushCount(deckB);

		// What a "Change Folder" action does when it runs under this press: the origin names one device
		// session, and the navigation is applied to that session alone.
		_fixture.Triggers.Execute = async request =>
		{
			var origin = new FlowExecutionRequest
			{
				Trigger = TriggerSelector.ByType(request.TriggerType), OriginDeviceId = request.OriginDeviceId
			}.OriginClientId;

			Assert.That(DeviceOrigin.TryParse(origin, out var pressed), Is.True);
			await _fixture.ChangeToAsync(pressed, DeviceSurfaceFixture.LightsFolderId);
		};

		await SubmitAsync(DeviceInteractionKind.ShortPress);

		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Provider.Latest(_deviceId).Folder?.Id,
				Is.EqualTo(DeviceSurfaceFixture.LightsFolderId));
			Assert.That(_fixture.Provider.Latest(deckB).Folder?.Id, Is.EqualTo(DeviceSurfaceFixture.HomeFolderId));
			Assert.That(_fixture.Provider.PushCount(deckB), Is.EqualTo(pushesToB));
		});
	}

	[Test]
	public async Task A_trigger_the_host_did_not_run_is_reported_as_a_refusal_not_an_acceptance()
	{
		_fixture.Triggers.Response = new ExecuteActionButtonTriggerResponse
		{
			Success = false, Error = new TransportError { Code = "NOT_FOUND", Message = "gone" }
		};

		var outcome = await SubmitAsync(DeviceInteractionKind.ShortPress);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Handled, Is.False);
			Assert.That(outcome.ErrorCode, Is.EqualTo(DeviceSessionReasons.TriggerFailed));
			Assert.That(outcome.ToResult().Status, Is.EqualTo(DeviceInteractionStatus.Rejected));
		});
	}

	[Test]
	public async Task An_interaction_for_a_device_with_no_session_is_refused_by_id_alone()
	{
		var outcome = await _fixture.Service.SubmitInteractionAsync(Guid.NewGuid(),
			Interaction(DeviceInteractionKind.Press, ButtonId));

		Assert.Multiple(() =>
		{
			Assert.That(outcome.ErrorCode, Is.EqualTo(DeviceSessionReasons.SessionNotFound));
			Assert.That(_fixture.Triggers.Triggers, Is.Empty);
		});
	}

	[Test]
	public async Task A_locked_host_runs_nothing_a_device_reports()
	{
		_fixture.LockState.IsLocked = true;

		var outcome = await SubmitAsync(DeviceInteractionKind.Press);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.ErrorCode, Is.EqualTo(DeviceSessionReasons.HostLocked));
			Assert.That(_fixture.Triggers.Triggers, Is.Empty);
		});
	}
}
