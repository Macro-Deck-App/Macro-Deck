using MacroDeckHost.Integrations.Mouse;
using MacroDeckHost.Integrations.Mouse.Native;

namespace MacroDeckHost.Tests.UnitTests.Mouse;

public class MouseInputServiceTests
{
	private FakeMouseInputProvider _provider = null!;
	private MouseInputService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_provider = new FakeMouseInputProvider();
		_service = new MouseInputService(_provider);
	}

	private void AssertCalls(params string[] expected) => Assert.That(_provider.Calls, Is.EqualTo(expected));

	[Test]
	public async Task Click_at_the_current_position_does_not_move()
	{
		await _service.ClickAsync(MouseButton.Left);

		AssertCalls("click(Left,1)");
	}

	[Test]
	public async Task Click_at_absolute_coordinates_carries_the_position()
	{
		await _service.ClickAsync(MouseButton.Left, target: MouseTarget.At(400, 300));

		AssertCalls("click(Left,1@400,300)");
	}

	[Test]
	public async Task Click_resolves_a_relative_target_against_the_current_position()
	{
		_provider.CursorPosition = new MousePoint(100, 200);

		await _service.ClickAsync(MouseButton.Right, target: MouseTarget.By(25, -50));

		AssertCalls("click(Right,1@125,150)");
	}

	[Test]
	public async Task Click_does_nothing_when_a_relative_target_cannot_be_resolved()
	{
		_provider.CursorPosition = null;

		await _service.ClickAsync(MouseButton.Left, target: MouseTarget.By(10, 10));

		Assert.That(_provider.Calls, Is.Empty);
	}

	[Test]
	public async Task Click_clamps_an_absolute_target_to_the_desktop()
	{
		_provider.DesktopBounds = new MouseRect(0, 0, 1920, 1080);

		await _service.ClickAsync(MouseButton.Left, target: MouseTarget.At(5000, 5000));

		AssertCalls("click(Left,1@1919,1079)");
	}

	[Test]
	public async Task Click_allows_negative_coordinates_on_a_display_left_of_the_primary()
	{
		_provider.DesktopBounds = new MouseRect(-1920, 0, 3840, 1080);

		await _service.ClickAsync(MouseButton.Left, target: MouseTarget.At(-800, 400));

		AssertCalls("click(Left,1@-800,400)");
	}

	[Test]
	public async Task Click_repeats_but_only_the_first_click_moves()
	{
		await _service.ClickAsync(MouseButton.Left, target: MouseTarget.At(10, 20), repeat: 3);

		AssertCalls("click(Left,1@10,20)", "click(Left,1)", "click(Left,1)");
	}

	[Test]
	public async Task Click_passes_the_click_count_through_for_a_double_click()
	{
		await _service.ClickAsync(MouseButton.Left, clickCount: 2);

		AssertCalls("click(Left,2)");
	}

	[Test]
	public async Task Click_clamps_an_out_of_range_click_count()
	{
		await _service.ClickAsync(MouseButton.Left, clickCount: 9);

		AssertCalls("click(Left,3)");
	}

	[Test]
	public async Task Move_emits_an_absolute_move()
	{
		await _service.MoveAsync(MouseTarget.At(640, 480));

		AssertCalls("move(640,480)");
	}

	[Test]
	public async Task Move_at_the_current_position_is_a_no_op()
	{
		await _service.MoveAsync(MouseTarget.Current);

		Assert.That(_provider.Calls, Is.Empty);
	}

	[Test]
	public async Task ButtonDown_holds_the_button_until_ReleaseAll()
	{
		await _service.ButtonDownAsync(MouseButton.Left);

		Assert.Multiple(() =>
		{
			AssertCalls("down(Left)");
			Assert.That(_service.HasHeldButtons, Is.True);
		});

		await _service.ReleaseAllAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_provider.Calls, Does.Contain("up(Left)"));
			Assert.That(_service.HasHeldButtons, Is.False);
		});
	}

	[Test]
	public async Task ButtonUp_on_an_unheld_button_is_a_safe_no_op()
	{
		await _service.ButtonUpAsync(MouseButton.Left);

		Assert.That(_provider.Calls, Is.Empty);
	}

	[Test]
	public async Task ButtonUp_releases_a_held_button_exactly_once()
	{
		await _service.ButtonDownAsync(MouseButton.Left);
		await _service.ButtonUpAsync(MouseButton.Left);
		await _service.ButtonUpAsync(MouseButton.Left);

		Assert.That(_provider.Calls.Count(c => c == "up(Left)"), Is.EqualTo(1));
	}

	[Test]
	public async Task ReleaseAll_releases_every_held_button()
	{
		await _service.ButtonDownAsync(MouseButton.Left);
		await _service.ButtonDownAsync(MouseButton.Right);

		await _service.ReleaseAllAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_provider.Calls, Does.Contain("up(Left)"));
			Assert.That(_provider.Calls, Does.Contain("up(Right)"));
		});
	}

	[Test]
	public async Task Drag_presses_interpolates_and_releases()
	{
		await _service.DragAsync(MouseButton.Left,
			MouseTarget.At(0, 0),
			MouseTarget.At(100, 0),
			durationMs: 0,
			steps: 4);

		AssertCalls("down(Left@0,0)",
			"drag(Left,25,0)",
			"drag(Left,50,0)",
			"drag(Left,75,0)",
			"drag(Left,100,0)",
			"up(Left)");
	}

	[Test]
	public async Task Drag_resolves_a_relative_end_against_the_start_not_the_live_cursor()
	{
		await _service.DragAsync(MouseButton.Left,
			MouseTarget.At(200, 200),
			MouseTarget.By(40, 0),
			durationMs: 0,
			steps: 2);

		AssertCalls("down(Left@200,200)", "drag(Left,220,200)", "drag(Left,240,200)", "up(Left)");
	}

	[Test]
	public async Task Drag_starts_at_the_live_cursor_when_no_start_is_given()
	{
		_provider.CursorPosition = new MousePoint(300, 400);

		await _service.DragAsync(MouseButton.Left,
			MouseTarget.Current,
			MouseTarget.At(300, 500),
			durationMs: 0,
			steps: 1);

		AssertCalls("down(Left@300,400)", "drag(Left,300,500)", "up(Left)");
	}

	[Test]
	public async Task Drag_releases_the_button_even_when_cancelled()
	{
		using var cancellation = new CancellationTokenSource();

		var drag = _service.DragAsync(MouseButton.Left,
			MouseTarget.At(0, 0),
			MouseTarget.At(1000, 0),
			durationMs: 5_000,
			steps: 50,
			cancellationToken: cancellation.Token);

		await WaitForCall("down(Left@0,0)");
		await cancellation.CancelAsync();

		Assert.That(async () => await drag, Throws.InstanceOf<OperationCanceledException>());
		Assert.Multiple(() =>
		{
			Assert.That(_provider.Calls, Does.Contain("up(Left)"));
			Assert.That(_service.HasHeldButtons, Is.False);
		});
	}

	private async Task WaitForCall(string call)
	{
		for (var attempt = 0; attempt < 200; attempt++)
		{
			lock (_provider.Calls)
			{
				if (_provider.Calls.Contains(call))
				{
					return;
				}
			}

			await Task.Delay(10);
		}

		Assert.Fail($"The provider never recorded '{call}'.");
	}

	[Test]
	public async Task Scroll_emits_one_call_per_notch()
	{
		await _service.ScrollAsync(ScrollAxis.Vertical, -3);

		AssertCalls("scroll(Vertical,-1)", "scroll(Vertical,-1)", "scroll(Vertical,-1)");
	}

	[Test]
	public async Task Scroll_moves_to_the_target_first_because_the_wheel_follows_the_pointer()
	{
		await _service.ScrollAsync(ScrollAxis.Vertical, 1, MouseTarget.At(50, 60));

		AssertCalls("move(50,60)", "scroll(Vertical,1)");
	}

	[Test]
	public async Task Scroll_of_zero_notches_does_nothing()
	{
		await _service.ScrollAsync(ScrollAxis.Vertical, 0);

		Assert.That(_provider.Calls, Is.Empty);
	}

	[Test]
	public async Task Scroll_handles_the_horizontal_axis()
	{
		await _service.ScrollAsync(ScrollAxis.Horizontal, 2);

		AssertCalls("scroll(Horizontal,1)", "scroll(Horizontal,1)");
	}

	[Test]
	public void An_unsupported_platform_throws_rather_than_silently_doing_nothing()
	{
		_provider.IsSupported = false;

		Assert.That(async () => await _service.ClickAsync(MouseButton.Left),
			Throws.InstanceOf<PlatformNotSupportedException>());
	}

	[Test]
	public void ReleaseAll_stays_callable_on_an_unsupported_platform()
	{
		_provider.IsSupported = false;

		Assert.That(async () => await _service.ReleaseAllAsync(), Throws.Nothing);
	}

	[Test]
	public async Task Clamping_is_skipped_when_the_desktop_bounds_are_unknown()
	{
		_provider.DesktopBounds = null;

		await _service.ClickAsync(MouseButton.Left, target: MouseTarget.At(9999, 9999));

		AssertCalls("click(Left,1@9999,9999)");
	}
}
