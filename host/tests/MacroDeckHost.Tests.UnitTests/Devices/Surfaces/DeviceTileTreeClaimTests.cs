using System.Text;
using System.Text.Json;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.Ui.Sessions;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

[TestFixture]
internal sealed class DeviceTileTreeClaimTests
{
	private const string PluginTileId = "plugin-tile";
	private const string ActionButtonId = "action-button";

	private const string ClaimingTree = """
		{"id":"root","type":"ui.stack","children":[
			{"id":"label","type":"ui.text"},
			{"id":"mute","type":"ui.button","properties":{"events":["press","long-press","press-start","press-end"]}},
			{"id":"solo","type":"ui.stack","properties":{"modifiers":{"disabled":true}}}]}
		""";

	private static readonly string[] _shortPressTriggers = ["onTouchStart", "onTouchEnd", "onShortPress"];

	private DeviceSurfaceFixture _fixture = null!;
	private Guid _deviceId;

	[SetUp]
	public async Task SetUp()
	{
		_fixture = new DeviceSurfaceFixture();
		var pluginTile = DeviceSurfaceFixture.Button(PluginTileId, 0, 0);
		pluginTile.Type = "com.example.mixer.channel";
		_fixture.Home.Widgets.AddRange([pluginTile, DeviceSurfaceFixture.Button(ActionButtonId, 1, 0)]);
		_deviceId = await _fixture.OpenDeviceAsync();
	}

	[TearDown]
	public void TearDown() => _fixture.Dispose();

	private void TreeIs(string root)
		=> _fixture.UiBroker.Tree = new UiRawJson(Encoding.UTF8.GetBytes(
			$$"""{"revision":7,"surface":{"kind":"widget"},"root":{{root}}}"""));

	private Task<DeviceInteractionOutcome> SubmitAsync(DeviceInteractionKind kind, string widgetId = PluginTileId)
		=> _fixture.Service.SubmitInteractionAsync(_deviceId,
			new DeviceInteraction { Kind = kind, Target = new DeviceInteractionTarget { WidgetId = widgetId } });

	private async Task HoldAsync(int milliseconds, string widgetId = PluginTileId)
	{
		var press = SubmitAsync(DeviceInteractionKind.Press, widgetId);
		_fixture.Time.Advance(TimeSpan.FromMilliseconds(milliseconds));
		await DeviceSurfaceFixture.DrainAsync();
		await press;
		await SubmitAsync(DeviceInteractionKind.Release, widgetId);
		await DeviceSurfaceFixture.DrainAsync();
	}

	private string[] SentEvents => [.. _fixture.UiBroker.HostEvents.Select(sent => sent.Command.Name)];

	private static UiRawJson Snapshot(string root)
		=> new(Encoding.UTF8.GetBytes($$"""{"revision":7,"surface":{"kind":"widget"},"root":{{root}}}"""));

	private async Task PressSettledAsync()
	{
		for (var attempt = 0; attempt < 500 && _fixture.UiBroker.ClosedSessions.Count == 0; attempt++)
		{
			await Task.Delay(10);
		}

		await DeviceSurfaceFixture.DrainAsync();
	}

	[Test]
	public async Task A_disabled_region_absorbs_the_whole_press_so_neither_the_control_nor_the_tiles_flow_runs()
	{
		TreeIs("""
			{"id":"root","type":"ui.stack","children":[
				{"id":"controls","type":"ui.stack","properties":{"modifiers":{"disabled":true}},"children":[
					{"id":"mute","type":"ui.button","properties":{"events":["press"]}}]}]}
			""");

		await HoldAsync(200);

		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Triggers.TriggerTypes, Is.Empty);
			Assert.That(_fixture.UiBroker.HostEvents, Is.Empty);
			Assert.That(_fixture.UiBroker.ClosedSessions, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_claiming_control_receives_every_phase_of_one_press_in_one_session()
	{
		TreeIs(ClaimingTree);

		await HoldAsync(200);

		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Triggers.TriggerTypes, Is.Empty);
			Assert.That(SentEvents, Is.EqualTo(new[] { "press-start", "press-end", "press" }));
			Assert.That(_fixture.UiBroker.HostEvents.Select(sent => (sent.SessionId, sent.Command.NodeId, sent.Command.Revision)).Distinct(),
				Is.EqualTo(new[] { ("session-1", "mute", (int?)7) }));
			Assert.That(_fixture.UiOpener.OpenedWidgetIds, Has.Count.EqualTo(1));
			Assert.That(_fixture.UiBroker.ClosedSessions, Is.EqualTo(new[] { "session-1" }));
		});
	}

	[Test]
	public async Task A_held_press_sends_the_claiming_control_a_long_press_instead_of_a_press()
	{
		TreeIs(ClaimingTree);

		await HoldAsync(700);

		Assert.That(SentEvents, Is.EqualTo(new[] { "press-start", "long-press", "press-end" }));
	}

	[Test]
	public async Task A_tree_that_does_not_answer_within_a_second_absorbs_the_press()
	{
		await HoldAsync(1000);
		await PressSettledAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Triggers.TriggerTypes, Is.Empty);
			Assert.That(_fixture.UiBroker.ClosedSessions, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_plugin_that_is_not_running_leaves_the_press_to_the_tiles_flow()
	{
		_fixture.UiOpener.RefusalCode = UiSessionErrorCodes.ProviderUnavailable;

		await HoldAsync(200);

		Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(_shortPressTriggers));
	}

	[Test]
	public async Task A_tree_session_the_host_cannot_open_absorbs_the_press()
	{
		_fixture.UiOpener.RefusalCode = UiSessionErrorCodes.TooManySessions;

		await HoldAsync(200);

		Assert.That(_fixture.Triggers.TriggerTypes, Is.Empty);
	}

	[Test]
	public async Task A_whole_press_queued_behind_its_tree_runs_nothing_once_the_host_locks()
	{
		_fixture.UiBroker.PendingTree = new TaskCompletionSource<UiRawJson?>();

		await SubmitAsync(DeviceInteractionKind.ShortPress).WaitAsync(TimeSpan.FromSeconds(5));
		_fixture.LockState.IsLocked = true;
		_fixture.UiBroker.PendingTree.SetResult(Snapshot("""{"id":"label","type":"ui.text"}"""));
		await PressSettledAsync();

		Assert.That(_fixture.Triggers.TriggerTypes, Is.Empty);
	}

	[Test]
	public async Task A_claiming_control_receives_nothing_while_the_host_is_locked()
	{
		_fixture.UiBroker.PendingTree = new TaskCompletionSource<UiRawJson?>();

		await SubmitAsync(DeviceInteractionKind.Press).WaitAsync(TimeSpan.FromSeconds(5));
		_fixture.LockState.IsLocked = true;
		_fixture.UiBroker.PendingTree.SetResult(Snapshot(ClaimingTree));
		_fixture.Time.Advance(TimeSpan.FromMilliseconds(700));
		await DeviceSurfaceFixture.DrainAsync();
		await _fixture.ChangeToAsync(_deviceId, DeviceSurfaceFixture.LightsFolderId);
		await PressSettledAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_fixture.UiBroker.HostEvents, Is.Empty);
			Assert.That(_fixture.Triggers.TriggerTypes, Is.Empty);
		});
	}

	[Test]
	public async Task A_segment_of_a_segmented_control_that_claims_nothing_does_not_claim_the_press()
	{
		TreeIs("""
			{"id":"picker","type":"ui.segmented","children":[
				{"id":"day","type":"ui.stack","properties":{"events":["press"]}}]}
			""");

		await HoldAsync(200);

		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(_shortPressTriggers));
			Assert.That(_fixture.UiBroker.HostEvents, Is.Empty);
		});
	}

	[Test]
	public async Task A_tree_that_claims_nothing_leaves_the_press_to_the_tiles_flow()
	{
		TreeIs("""{"id":"root","type":"ui.stack","children":[{"id":"label","type":"ui.text"}]}""");

		await HoldAsync(200);

		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(_shortPressTriggers));
			Assert.That(_fixture.UiBroker.HostEvents, Is.Empty);
			Assert.That(_fixture.UiBroker.ClosedSessions, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_press_report_does_not_wait_for_the_tiles_tree_so_the_tree_can_arrive_behind_it()
	{
		_fixture.UiBroker.PendingTree = new TaskCompletionSource<UiRawJson?>();

		await SubmitAsync(DeviceInteractionKind.Press).WaitAsync(TimeSpan.FromSeconds(5));
		_fixture.UiBroker.PendingTree.SetResult(Snapshot(ClaimingTree));
		await DeviceSurfaceFixture.DrainAsync();
		await SubmitAsync(DeviceInteractionKind.Release);
		await PressSettledAsync();

		Assert.Multiple(() =>
		{
			Assert.That(SentEvents, Is.EqualTo(new[] { "press-start", "press-end", "press" }));
			Assert.That(_fixture.Triggers.TriggerTypes, Is.Empty);
		});
	}

	[Test]
	public async Task A_release_reported_before_the_tree_arrives_keeps_the_phases_in_order()
	{
		_fixture.UiBroker.PendingTree = new TaskCompletionSource<UiRawJson?>();

		await SubmitAsync(DeviceInteractionKind.Press).WaitAsync(TimeSpan.FromSeconds(5));
		await SubmitAsync(DeviceInteractionKind.Release).WaitAsync(TimeSpan.FromSeconds(5));
		_fixture.UiBroker.PendingTree.SetResult(Snapshot(ClaimingTree));
		await PressSettledAsync();

		Assert.That(SentEvents, Is.EqualTo(new[] { "press-start", "press-end", "press" }));
	}

	[Test]
	public async Task A_long_press_reached_while_the_tree_is_pending_still_follows_the_touch_start()
	{
		_fixture.UiBroker.PendingTree = new TaskCompletionSource<UiRawJson?>();

		await SubmitAsync(DeviceInteractionKind.Press).WaitAsync(TimeSpan.FromSeconds(5));
		_fixture.Time.Advance(TimeSpan.FromMilliseconds(700));
		await DeviceSurfaceFixture.DrainAsync();
		_fixture.UiBroker.PendingTree.SetResult(Snapshot(ClaimingTree));
		await SubmitAsync(DeviceInteractionKind.Release);
		await PressSettledAsync();

		Assert.That(SentEvents, Is.EqualTo(new[] { "press-start", "long-press", "press-end" }));
	}

	[Test]
	public async Task Navigating_away_while_the_tree_is_pending_neither_waits_for_it_nor_runs_the_flow()
	{
		_fixture.UiBroker.PendingTree = new TaskCompletionSource<UiRawJson?>();

		await SubmitAsync(DeviceInteractionKind.Press).WaitAsync(TimeSpan.FromSeconds(5));
		await _fixture.ChangeToAsync(_deviceId, DeviceSurfaceFixture.LightsFolderId).WaitAsync(TimeSpan.FromSeconds(5));
		_fixture.Time.Advance(TimeSpan.FromSeconds(1));
		await PressSettledAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Triggers.TriggerTypes, Is.Empty);
			Assert.That(_fixture.UiBroker.ClosedSessions, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_repeated_press_report_opens_one_tree_session()
	{
		TreeIs(ClaimingTree);

		await SubmitAsync(DeviceInteractionKind.Press);
		await HoldAsync(200);

		Assert.Multiple(() =>
		{
			Assert.That(_fixture.UiOpener.OpenedWidgetIds, Has.Count.EqualTo(1));
			Assert.That(SentEvents, Is.EqualTo(new[] { "press-start", "press-end", "press" }));
		});
	}

	[Test]
	public async Task A_whole_press_report_on_a_tree_still_pending_is_accepted_as_queued_and_its_flow_runs_later()
	{
		_fixture.UiBroker.PendingTree = new TaskCompletionSource<UiRawJson?>();

		var outcome = await SubmitAsync(DeviceInteractionKind.ShortPress).WaitAsync(TimeSpan.FromSeconds(5));
		var ranBeforeTheTree = _fixture.Triggers.TriggerTypes;
		_fixture.UiBroker.PendingTree.SetResult(Snapshot("""{"id":"label","type":"ui.text"}"""));
		await PressSettledAsync();

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(DeviceInteractionOutcome.Accepted));
			Assert.That(ranBeforeTheTree, Is.Empty);
			Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(new[] { "onShortPress" }));
		});
	}

	[Test]
	public async Task A_whole_press_reported_at_once_flips_a_claiming_toggle()
	{
		TreeIs("""{"id":"power","type":"ui.toggle","properties":{"on":true,"events":["change"]}}""");

		await SubmitAsync(DeviceInteractionKind.ShortPress);

		var sent = _fixture.UiBroker.HostEvents.Single().Command;
		Assert.Multiple(() =>
		{
			Assert.That(sent.Name, Is.EqualTo("change"));
			Assert.That(JsonDocument.Parse(sent.Data.Utf8).RootElement.GetBoolean(), Is.False);
			Assert.That(_fixture.Triggers.TriggerTypes, Is.Empty);
		});
	}

	[Test]
	public async Task A_built_in_tile_runs_its_flow_without_opening_a_tree()
	{
		await HoldAsync(200, ActionButtonId);

		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(_shortPressTriggers));
			Assert.That(_fixture.UiOpener.OpenedWidgetIds, Is.Empty);
		});
	}
}
