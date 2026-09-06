using MacroDeckHost.Integrations.Mouse;
using MacroDeckHost.Integrations.Mouse.Actions;
using MacroDeckHost.Integrations.Mouse.Native;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Mouse;

public class MouseActionExecutionTests
{
	private FakeMouseInputProvider _provider = null!;
	private MouseInputService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_provider = new FakeMouseInputProvider();
		_service = new MouseInputService(_provider);
	}

	private static ActionExecutionContext Context(Dictionary<string, object> parameters)
		=> new() { Parameters = parameters };

	private static Task<ActionResult> Execute(IActionDefinition action, Dictionary<string, object> parameters)
		=> action.CreateExecutor().ExecuteAsync(Context(parameters));

	private void AssertCalls(params string[] expected) => Assert.That(_provider.Calls, Is.EqualTo(expected));

	private IActionDefinition Action(string id) => id switch
	{
		"click" => new ClickActionDefinition(_service),
		"move-cursor" => new MoveCursorActionDefinition(_service),
		"drag" => new DragActionDefinition(_service),
		"scroll" => new ScrollActionDefinition(_service),
		"button-down" => new ButtonDownActionDefinition(_service),
		"button-up" => new ButtonUpActionDefinition(_service),
		_ => new ReleaseAllButtonsActionDefinition(_service)
	};

	[TestCase("click")]
	[TestCase("move-cursor")]
	[TestCase("drag")]
	[TestCase("scroll")]
	[TestCase("button-down")]
	[TestCase("button-up")]
	[TestCase("release-all-buttons")]
	public void An_action_with_no_parameters_at_all_does_not_throw(string actionId)
	{
		Assert.That(async () => await Execute(Action(actionId), []), Throws.Nothing);
	}

	[Test]
	public async Task Click_reads_button_position_and_click_count()
	{
		await Execute(Action("click"),
			new Dictionary<string, object>
			{
				["button"] = "right",
				["clickCount"] = "2",
				["positionMode"] = "absolute",
				["x"] = 640,
				["y"] = 480
			});

		AssertCalls("click(Right,2@640,480)");
	}

	[TestCase(300, 200)]
	[TestCase(300L, 200L)]
	[TestCase(300.0, 200.0)]
	[TestCase("300", "200")]
	[TestCase("300.4", "199.7")]
	public async Task Coordinates_survive_every_CLR_type_the_flow_executor_produces(object x, object y)
	{
		await Execute(Action("move-cursor"),
			new Dictionary<string, object> { ["positionMode"] = "absolute", ["x"] = x, ["y"] = y });

		AssertCalls("move(300,200)");
	}

	[Test]
	public async Task Negative_coordinates_survive_as_negatives()
	{
		_provider.DesktopBounds = new MouseRect(-1920, 0, 3840, 1080);

		await Execute(Action("move-cursor"),
			new Dictionary<string, object> { ["positionMode"] = "absolute", ["x"] = "-500", ["y"] = 300 });

		AssertCalls("move(-500,300)");
	}

	[Test]
	public async Task An_unknown_button_falls_back_to_left_rather_than_throwing()
	{
		await Execute(Action("click"), new Dictionary<string, object> { ["button"] = "nonsense" });

		AssertCalls("click(Left,1)");
	}

	[Test]
	public async Task Move_cursor_defaults_to_absolute_when_the_mode_is_missing()
	{
		await Execute(Action("move-cursor"), new Dictionary<string, object> { ["x"] = 12, ["y"] = 34 });

		AssertCalls("move(12,34)");
	}

	[Test]
	public async Task Scroll_maps_direction_to_an_axis_and_a_sign()
	{
		await Execute(Action("scroll"),
			new Dictionary<string, object> { ["direction"] = "up", ["amount"] = 2 });

		AssertCalls("scroll(Vertical,1)", "scroll(Vertical,1)");
	}

	[Test]
	public async Task Scroll_left_uses_the_horizontal_axis_with_a_negative_sign()
	{
		await Execute(Action("scroll"),
			new Dictionary<string, object> { ["direction"] = "left", ["amount"] = 1 });

		AssertCalls("scroll(Horizontal,-1)");
	}

	[Test]
	public async Task Drag_reads_both_ends_and_always_releases()
	{
		await Execute(Action("drag"),
			new Dictionary<string, object>
			{
				["button"] = "left",
				["fromMode"] = "absolute",
				["fromX"] = 10,
				["fromY"] = 10,
				["toMode"] = "absolute",
				["toX"] = 30,
				["toY"] = 10,
				["duration"] = 0,
				["steps"] = 2
			});

		AssertCalls("down(Left@10,10)", "drag(Left,20,10)", "drag(Left,30,10)", "up(Left)");
	}

	[Test]
	public async Task Hold_then_release_all_recovers_a_stuck_button()
	{
		await Execute(Action("button-down"), new Dictionary<string, object> { ["button"] = "left" });
		Assert.That(_service.HasHeldButtons, Is.True);

		await Execute(Action("release-all-buttons"), []);

		Assert.Multiple(() =>
		{
			Assert.That(_service.HasHeldButtons, Is.False);
			Assert.That(_provider.Calls, Does.Contain("up(Left)"));
		});
	}

	[Test]
	public async Task Shutdown_releases_a_button_the_integration_is_still_holding()
	{
		await Execute(Action("button-down"), new Dictionary<string, object> { ["button"] = "middle" });

		await _service.ReleaseAllAsync();

		Assert.That(_provider.Calls, Does.Contain("up(Middle)"));
	}

	[Test]
	public void The_integration_shuts_down_cleanly_with_nothing_held()
	{
		Assert.That(async () => await new MouseInputIntegration().ShutdownAsync(), Throws.Nothing);
	}
}
