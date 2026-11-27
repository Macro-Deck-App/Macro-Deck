using System.Text;
using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

[TestFixture]
internal sealed class DeviceKeyImagePressFeedbackTests
{
	private const string Pressed = "|pressed";

	private static readonly TimeSpan _poll = TimeSpan.FromSeconds(5);

	private DeviceSurfaceFixture _fixture = null!;

	[SetUp]
	public void SetUp()
	{
		_fixture = new DeviceSurfaceFixture();
	}

	[TearDown]
	public void TearDown()
	{
		_fixture.KeyImageRenderer.PressedRenders.Set();
		_fixture.Dispose();
	}

	private static string Snapshot()
	{
		return LayoutSnapshotSerializer.Serialize(new LayoutDescriptor("com.example.deck::3x5",
			"Deck",
			[
				new LayoutRegion
				{
					Id = "keys",
					Kind = LayoutRegionKinds.Grid,
					Grid = new LayoutGrid
					{
						Rows = 3,
						Columns = 5,
						KeySize = new LayoutKeySize(72, 72),
						KeyImages = new LayoutKeyImageFormat { Encoding = KeyImageEncodings.Jpeg }
					}
				}
			],
			new LayoutCapabilities { Visuals = LayoutVisualCapabilities.Full }));
	}

	private async Task<Guid> OpenAsync()
	{
		var deviceId = _fixture.AddDevice("DeckA", layoutSnapshot: Snapshot());
		await _fixture.Service.OpenAsync(deviceId, _fixture.Provider.ProviderId, "DeckA");
		await SettleUntilAsync(() => Batches(deviceId).Count > 0, "No key image was ever pushed.");
		_fixture.Time.Advance(TimeSpan.FromSeconds(1));
		await QuietAsync();
		return deviceId;
	}

	private Task<DeviceInteractionOutcome> SubmitAsync(Guid deviceId, DeviceInteractionKind kind, string widgetId)
	{
		return _fixture.Service.SubmitInteractionAsync(deviceId,
			new DeviceInteraction { Kind = kind, Target = new DeviceInteractionTarget { WidgetId = widgetId } });
	}

	private IReadOnlyList<IReadOnlyList<DeviceKeyImage>> Batches(Guid deviceId)
	{
		return _fixture.Provider.KeyImageBatchesFor(deviceId);
	}

	private string? Latest(Guid deviceId, int row, int column)
	{
		return Batches(deviceId)
			.SelectMany(batch => batch)
			.LastOrDefault(image => image.Row == row && image.Column == column) is { } image
			? Encoding.UTF8.GetString(image.Content.Span)
			: null;
	}

	private bool ShowsPressed(Guid deviceId, int row = 0, int column = 0)
	{
		return Latest(deviceId, row, column)?.EndsWith(Pressed, StringComparison.Ordinal) == true;
	}

	private async Task AdvanceAsync(int milliseconds)
	{
		_fixture.Time.Advance(TimeSpan.FromMilliseconds(milliseconds));
		await QuietAsync();
	}

	private async Task SettleUntilAsync(Func<bool> condition, string message)
	{
		var deadline = DateTime.UtcNow + _poll;
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail(message);
			}

			_fixture.Time.Advance(TimeSpan.FromMilliseconds(10));
			await Task.Delay(5);
		}
	}

	private static async Task QuietAsync()
	{
		await Task.Delay(100);
	}

	[Test]
	public async Task A_press_pushes_the_pressed_look_of_every_cell_of_that_widget_and_nothing_else()
	{
		_fixture.Home.Widgets.AddRange([
			DeviceSurfaceFixture.Button("w1", 0, 0, 2),
			DeviceSurfaceFixture.Button("w2", 0, 1)
		]);
		var deviceId = await OpenAsync();
		var before = Batches(deviceId).Count;

		var outcome = await SubmitAsync(deviceId, DeviceInteractionKind.Press, "w1");
		await SettleUntilAsync(() => Batches(deviceId).Count > before, "The press was never drawn.");

		var batch = Batches(deviceId)[before];
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(outcome, Is.EqualTo(DeviceInteractionOutcome.Accepted));
			Assert.That(batch.Select(image => (image.Row, image.Column)), Is.EquivalentTo(new[] { (0, 0), (0, 1) }));
			Assert.That(batch.Select(image => Encoding.UTF8.GetString(image.Content.Span)),
				Has.All.EndsWith(Pressed));
			Assert.That(ShowsPressed(deviceId, 1, 0), Is.False, "the other widget keeps its look");
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_release_after_the_minimum_pushes_the_resting_look()
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));
		var deviceId = await OpenAsync();

		await SubmitAsync(deviceId, DeviceInteractionKind.Press, "w1");
		await SettleUntilAsync(() => ShowsPressed(deviceId), "The press was never drawn.");
		await AdvanceAsync(200);
		await SubmitAsync(deviceId, DeviceInteractionKind.Release, "w1");
		await SettleUntilAsync(() => !ShowsPressed(deviceId), "The release was never drawn.");

		Assert.That(Latest(deviceId, 0, 0), Does.StartWith("Widget|ActionButton|"));
	}

	[Test]
	public async Task A_quick_tap_keeps_the_pressed_look_on_the_key_for_the_minimum_visible_time()
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));
		var deviceId = await OpenAsync();

		await SubmitAsync(deviceId, DeviceInteractionKind.Press, "w1");
		await SettleUntilAsync(() => ShowsPressed(deviceId), "The press was never drawn.");
		await SubmitAsync(deviceId, DeviceInteractionKind.Release, "w1");
		var held = new List<bool>();
		for (var step = 0; step < 4; step++)
		{
			await AdvanceAsync(10);
			held.Add(ShowsPressed(deviceId));
		}

		await SettleUntilAsync(() => !ShowsPressed(deviceId), "The key never returned to its resting look.");

		Assert.That(held, Is.All.True, "40 ms after the release, past the update interval, inside the minimum");
	}

	[TestCase(DeviceInteractionKind.ShortPress)]
	[TestCase(DeviceInteractionKind.LongPress)]
	public async Task A_whole_press_flashes_the_pressed_look_and_returns_to_rest(DeviceInteractionKind kind)
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));
		var deviceId = await OpenAsync();
		var before = Batches(deviceId).Count;

		await SubmitAsync(deviceId, kind, "w1");
		await SettleUntilAsync(() => ShowsPressed(deviceId), "The press was never drawn.");
		await SettleUntilAsync(() => !ShowsPressed(deviceId), "The key never returned to its resting look.");

		Assert.That(Batches(deviceId), Has.Count.EqualTo(before + 2), "one pressed and one resting picture");
	}

	[Test]
	public async Task A_press_returns_its_outcome_without_waiting_for_the_pressed_picture()
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));
		var deviceId = await OpenAsync();
		_fixture.KeyImageRenderer.PressedRenders.Reset();

		var outcome = await SubmitAsync(deviceId, DeviceInteractionKind.Press, "w1").WaitAsync(_poll);
		var drawnMeanwhile = ShowsPressed(deviceId);
		_fixture.KeyImageRenderer.PressedRenders.Set();
		await SettleUntilAsync(() => ShowsPressed(deviceId), "The press was never drawn.");

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(outcome, Is.EqualTo(DeviceInteractionOutcome.Accepted));
			Assert.That(drawnMeanwhile, Is.False);
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_press_the_locked_host_refuses_draws_nothing()
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));
		var deviceId = await OpenAsync();
		var before = Batches(deviceId).Count;
		_fixture.LockState.IsLocked = true;

		var outcome = await SubmitAsync(deviceId, DeviceInteractionKind.Press, "w1");
		await AdvanceAsync(100);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(outcome.ErrorCode, Is.EqualTo(DeviceSurfaceErrorCodes.HostLocked));
			Assert.That(Batches(deviceId), Has.Count.EqualTo(before));
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_release_while_the_host_is_locked_still_returns_the_key_to_rest()
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));
		var deviceId = await OpenAsync();

		await SubmitAsync(deviceId, DeviceInteractionKind.Press, "w1");
		await SettleUntilAsync(() => ShowsPressed(deviceId), "The press was never drawn.");
		_fixture.LockState.IsLocked = true;
		await SubmitAsync(deviceId, DeviceInteractionKind.Release, "w1");

		await SettleUntilAsync(() => !ShowsPressed(deviceId), "The key stayed pressed.");
	}

	[Test]
	public async Task A_tap_during_a_surface_rebuild_is_drawn_once_the_rebuild_is_pushed()
	{
		var widgetId = Guid.NewGuid();
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button(widgetId.ToString(), 0, 0));
		var deviceId = await OpenAsync();

		_fixture.Labels.Set(widgetId, string.Empty, "Renamed");
		await _fixture.Service.OnWidgetLabelChanged(widgetId, string.Empty);
		await SubmitAsync(deviceId, DeviceInteractionKind.Press, widgetId.ToString());
		await SubmitAsync(deviceId, DeviceInteractionKind.Release, widgetId.ToString());
		await QuietAsync();
		var pressedDuringTheHold = ShowsPressed(deviceId);

		await SettleUntilAsync(() => ShowsPressed(deviceId), "The tap was lost to the rebuild.");
		await SettleUntilAsync(() => !ShowsPressed(deviceId), "The key never returned to its resting look.");

		Assert.That(pressedDuringTheHold, Is.False, "nothing is pushed while the surface is rebuilt");
	}

	[Test]
	public async Task An_animated_key_returns_to_rest_once_the_minimum_passed()
	{
		_fixture.KeyImageRenderer.FramedTypes.Add(WidgetTypeIds.ActionButton);
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));
		var deviceId = await OpenAsync();

		await SubmitAsync(deviceId, DeviceInteractionKind.Press, "w1");
		await SettleUntilAsync(() => ShowsPressed(deviceId), "The press was never drawn.");
		await SubmitAsync(deviceId, DeviceInteractionKind.Release, "w1");

		await SettleUntilAsync(() => !ShowsPressed(deviceId), "The animation kept the key pressed.");
	}

	[Test]
	public async Task A_device_that_goes_offline_mid_press_comes_back_resting()
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));
		var deviceId = await OpenAsync();

		await SubmitAsync(deviceId, DeviceInteractionKind.Press, "w1");
		await SettleUntilAsync(() => ShowsPressed(deviceId), "The press was never drawn.");
		await _fixture.Service.SetPresenceAsync(deviceId, false);
		var before = Batches(deviceId).Count;
		await _fixture.Service.SetPresenceAsync(deviceId, true);
		await SettleUntilAsync(() => Batches(deviceId).Count > before, "The device got nothing on its return.");

		Assert.That(ShowsPressed(deviceId), Is.False);
	}

	[Test]
	public async Task A_widget_that_leaves_the_keys_while_held_comes_back_resting()
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));
		var deviceId = await OpenAsync();

		await SubmitAsync(deviceId, DeviceInteractionKind.Press, "w1");
		await SettleUntilAsync(() => ShowsPressed(deviceId), "The press was never drawn.");
		await _fixture.ChangeToAsync(deviceId, DeviceSurfaceFixture.AudioFolderId);
		await SettleUntilAsync(() => !ShowsPressed(deviceId), "The other folder was never drawn.");
		var away = Batches(deviceId).Count;
		await _fixture.ChangeToAsync(deviceId, DeviceSurfaceFixture.HomeFolderId);
		await SettleUntilAsync(() => Batches(deviceId).Count > away
				&& Latest(deviceId, 0, 0)?.StartsWith("Widget|", StringComparison.Ordinal) == true,
			"The home folder was never drawn again.");

		Assert.That(ShowsPressed(deviceId), Is.False);
	}
}
