using System.Text.Json;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

[TestFixture]
internal sealed class DeviceDoubleTapTests
{
	private const string ButtonId = "w1";
	private const string PlainButtonId = "w2";

	private static readonly string[] _doubleTap = ["onTouchStart", "onTouchEnd", "onTouchStart", "onTouchEnd", "onDoublePress"];

	private static readonly string[] _heldTap = ["onTouchStart", "onTouchEnd"];

	private static readonly string[] _singleTap = ["onTouchStart", "onTouchEnd", "onShortPress"];

	private static readonly string[] _tapThenLongPress =
		["onTouchStart", "onTouchEnd", "onTouchStart", "onShortPress", "onLongPress", "onTouchEnd"];

	private static readonly string[] _twoSingleTaps =
		["onTouchStart", "onTouchEnd", "onShortPress", "onTouchStart", "onTouchEnd", "onShortPress"];

	private DeviceSurfaceFixture _fixture = null!;
	private Guid _deviceId;

	[SetUp]
	public async Task SetUp()
	{
		_fixture = new DeviceSurfaceFixture();
		_fixture.Home.Widgets.AddRange([
			WithFlows(ButtonId, 0, WidgetTypeIds.ActionButton, Flow("onShortPress"), Flow("onDoublePress")),
			WithFlows(PlainButtonId, 1, WidgetTypeIds.ActionButton, Flow("onShortPress"))
		]);
		_deviceId = await _fixture.OpenDeviceAsync();
	}

	[TearDown]
	public void TearDown() => _fixture.Dispose();

	private static Dictionary<string, object?> Flow(string triggerType, bool disabled = false, bool empty = false)
		=> new(StringComparer.Ordinal)
		{
			["triggerType"] = triggerType,
			["children"] = empty
				? Array.Empty<object>()
				: new object[] { new Dictionary<string, object?> { ["id"] = "b1", ["disabled"] = disabled } }
		};

	private static Widget WithFlows(string id, int x, string type, params Dictionary<string, object?>[] flows)
		=> new()
		{
			Id = id,
			Type = type,
			PositionX = x,
			PositionY = 0,
			Width = 1,
			Height = 1,
			Data = JsonSerializer.Serialize(new Dictionary<string, object?> { ["flows"] = flows })
		};

	private Task<DeviceInteractionOutcome> SubmitAsync(DeviceInteractionKind kind, string widgetId = ButtonId)
		=> _fixture.Service.SubmitInteractionAsync(_deviceId,
			new DeviceInteraction { Kind = kind, Target = new DeviceInteractionTarget { WidgetId = widgetId } });

	private async Task AdvanceAsync(int milliseconds)
	{
		_fixture.Time.Advance(TimeSpan.FromMilliseconds(milliseconds));
		await DeviceSurfaceFixture.DrainAsync();
	}

	private async Task TapAsync(string widgetId = ButtonId)
	{
		await SubmitAsync(DeviceInteractionKind.Press, widgetId);
		await AdvanceAsync(50);
		await SubmitAsync(DeviceInteractionKind.Release, widgetId);
		await DeviceSurfaceFixture.DrainAsync();
	}

	[Test]
	public async Task Two_quick_taps_run_only_the_double_tap_flow()
	{
		await TapAsync();
		await AdvanceAsync(200);
		await TapAsync();
		await AdvanceAsync(5000);

		Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(_doubleTap));
	}

	[Test]
	public async Task A_single_tap_runs_its_short_press_once_the_window_has_passed()
	{
		await TapAsync();
		await AdvanceAsync(399);
		var beforeWindowEnds = _fixture.Triggers.TriggerTypes;

		await AdvanceAsync(1);

		Assert.Multiple(() =>
		{
			Assert.That(beforeWindowEnds, Is.EqualTo(_heldTap));
			Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(_singleTap));
		});
	}

	[Test]
	public async Task A_long_press_after_a_tap_runs_the_held_short_press_before_its_own_long_press()
	{
		await TapAsync();
		await AdvanceAsync(100);
		await SubmitAsync(DeviceInteractionKind.Press);
		await AdvanceAsync(600);
		await SubmitAsync(DeviceInteractionKind.Release);
		await AdvanceAsync(5000);

		Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(_tapThenLongPress));
	}

	[Test]
	public async Task A_widget_without_a_double_tap_flow_keeps_its_short_press_immediate()
	{
		await TapAsync(PlainButtonId);
		await TapAsync(PlainButtonId);

		Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(_twoSingleTaps));
	}

	[TestCase(true, false)]
	[TestCase(false, true)]
	public async Task An_empty_or_disabled_double_tap_flow_keeps_the_short_press_immediate(bool disabled, bool empty)
	{
		_fixture.Home.Widgets[0] = WithFlows(ButtonId,
			0,
			WidgetTypeIds.ActionButton,
			Flow("onShortPress"),
			Flow("onDoublePress", disabled, empty));

		await TapAsync();
		await TapAsync();

		Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(_twoSingleTaps));
	}

	[Test]
	public async Task A_double_tap_flow_added_without_a_visible_change_applies_on_the_next_tap()
	{
		_fixture.Home.Widgets[1] = WithFlows(PlainButtonId,
			1,
			WidgetTypeIds.ActionButton,
			Flow("onShortPress"),
			Flow("onDoublePress"));

		await TapAsync(PlainButtonId);
		await AdvanceAsync(100);
		await TapAsync(PlainButtonId);

		Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(_doubleTap));
	}

	[Test]
	public async Task Navigating_away_drops_a_held_short_press()
	{
		await TapAsync();
		await _fixture.ChangeToAsync(_deviceId, DeviceSurfaceFixture.LightsFolderId);
		await AdvanceAsync(5000);

		Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(_heldTap));
	}

	[Test]
	public async Task An_explicit_short_press_report_is_never_held_and_keeps_its_verdict()
	{
		var first = await SubmitAsync(DeviceInteractionKind.ShortPress);
		var second = await SubmitAsync(DeviceInteractionKind.ShortPress);

		Assert.Multiple(() =>
		{
			Assert.That(first.ToResult().Status, Is.EqualTo(DeviceInteractionStatus.Accepted));
			Assert.That(second.ToResult().Status, Is.EqualTo(DeviceInteractionStatus.Accepted));
			Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(new[] { "onShortPress", "onShortPress" }));
		});
	}

	[Test]
	public async Task A_widget_whose_only_press_flow_is_a_double_tap_offers_press_and_release_but_a_slider_or_plugin_widget_offers_nothing()
	{
		_fixture.Home.Widgets.AddRange([
			WithFlows("double-only", 2, WidgetTypeIds.ActionButton, Flow("onDoublePress")),
			WithFlows("slider", 3, WidgetTypeIds.Slider, Flow("onDoublePress")),
			WithFlows("plugin", 4, "com.example.meter", Flow("onDoublePress"))
		]);
		await _fixture.MutateAsync(() => { });

		var widgets = _fixture.Provider.Latest(_deviceId).Widgets;

		Assert.Multiple(() =>
		{
			Assert.That(widgets.Single(widget => widget.Id == "double-only").SupportedInteractions,
				Is.EqualTo(new[] { DeviceInteractionKind.Press, DeviceInteractionKind.Release }));
			Assert.That(widgets.Single(widget => widget.Id == "slider").SupportedInteractions, Is.Empty);
			Assert.That(widgets.Single(widget => widget.Id == "plugin").SupportedInteractions, Is.Empty);
		});
	}
}
