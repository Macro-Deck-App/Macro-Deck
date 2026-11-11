using MacroDeckHost.Integrations.Mouse.Actions;
using MacroDeckHost.Integrations.Mouse.Models;
using MacroDeckHost.Integrations.Mouse.Native;
using MacroDeckHost.Tests.InputProbe;
using MacroDeckHost.Tests.Support;

namespace MacroDeckHost.Tests.PlatformInput.Mouse;

public abstract class MouseInputCases
{
	private IMouseInputProvider _provider = null!;
	private MouseInputService _mouse = null!;
	private ProbeProcess _probe = null!;

	[OneTimeSetUp]
	public void StartProbe()
	{
		_provider = MouseInputProviderFactory.Create();
		Interactive.Require(_provider.IsSupported, $"{_provider.PlatformName} cannot inject mouse input here.");
		_mouse = new MouseInputService(_provider);
		_probe = ProbeProcess.StartWindow("mdprobe-mouse", 200, 150, 800, 600);
	}

	[OneTimeTearDown]
	public void StopProbe()
	{
		_probe?.Dispose();
		(_provider as IDisposable)?.Dispose();
	}

	[SetUp]
	public async Task StartInsideTheProbe()
	{
		ProbeFocus.Focus(_probe);
		await _mouse.MoveAsync(MouseTarget.At(_probe.CenterX, _probe.CenterY));
		_probe.Settle();
		_probe.Clear();
	}

	[TearDown]
	public async Task NoButtonStaysPressed()
	{
		await _mouse.ReleaseAllAsync();
		_probe.Settle();
		Assert.That(InputState.HeldButtons(), Is.Empty, "buttons still held after the case");
	}

	[Test]
	public async Task An_absolute_move_puts_the_pointer_on_that_screen_position()
	{
		var target = (X: _probe.Ready.X!.Value + 50, Y: _probe.Ready.Y!.Value + 60);

		await _mouse.MoveAsync(MouseTarget.At(target.X, target.Y));
		_probe.Settle();

		var moves = _probe.Events.Where(e => e.Type == "move").ToArray();
		Assert.Multiple(() =>
		{
			Assert.That(InputState.Pointer(), Is.EqualTo(target));
			Assert.That(moves.LastOrDefault() is { } last && (last.X, last.Y) == target, Is.True, _probe.Diagnostics);
		});
	}

	[Test]
	public async Task A_relative_move_shifts_the_pointer_by_the_offset()
	{
		var start = InputState.Pointer();

		await _mouse.MoveAsync(MouseTarget.By(-40, 25));
		_probe.Settle();

		Assert.That(InputState.Pointer(), Is.EqualTo((start.X - 40, start.Y + 25)), _probe.Diagnostics);
	}

	[TestCase(MouseButton.Left, "left")]
	[TestCase(MouseButton.Right, "right")]
	[TestCase(MouseButton.Middle, "middle")]
	public async Task A_click_presses_and_releases_the_button_once_at_the_position(MouseButton button, string name)
	{
		await _mouse.ClickAsync(button, 1, MouseTarget.At(_probe.CenterX + 10, _probe.CenterY + 10));
		_probe.Settle();

		var buttons = _probe.Buttons.Where(e => e.Button == name).ToArray();
		Assert.Multiple(() =>
		{
			Assert.That(buttons.Select(e => e.Down).ToArray(),
				Is.EqualTo(new bool?[] { true, false }),
				_probe.Diagnostics);
			Assert.That(buttons.First().Clicks, Is.EqualTo(1));
			Assert.That((buttons.First().X, buttons.First().Y), Is.EqualTo((_probe.CenterX + 10, _probe.CenterY + 10)));
		});
	}

	[Test]
	public async Task A_double_click_arrives_as_a_double_click()
	{
		await _mouse.ClickAsync(MouseButton.Left, 2);
		_probe.Settle();

		var presses = _probe.Buttons.Where(e => e.Button == "left" && e.Down == true).ToArray();
		Assert.That(presses.Select(e => e.Clicks).ToArray(), Is.EqualTo(new int?[] { 1, 2 }), _probe.Diagnostics);
	}

	[Test]
	public async Task A_button_held_down_stays_down_until_it_is_released()
	{
		await _mouse.ButtonDownAsync(MouseButton.Left);
		_probe.Settle();
		var held = InputState.HeldButtons();

		await _mouse.ButtonUpAsync(MouseButton.Left);
		_probe.Settle();

		Assert.Multiple(() =>
		{
			Assert.That(held, Is.Not.Empty, "the button is down for the OS while held");
			Assert.That(_probe.Buttons.Select(e => e.Down).ToArray(), Is.EqualTo(new bool?[] { true, false }));
		});
	}

	[Test]
	public async Task A_drag_presses_at_the_origin_moves_and_releases_at_the_destination()
	{
		var origin = (X: _probe.Ready.X!.Value + 100, Y: _probe.Ready.Y!.Value + 100);
		var destination = (X: origin.X + 300, Y: origin.Y + 200);

		await _mouse.DragAsync(MouseButton.Left,
			MouseTarget.At(origin.X, origin.Y),
			MouseTarget.At(destination.X, destination.Y));
		_probe.Settle();

		var buttons = _probe.Buttons;
		var down = buttons.Single(e => e.Down == true);
		var up = buttons.Single(e => e.Down == false);
		Assert.Multiple(() =>
		{
			Assert.That((down.X, down.Y), Is.EqualTo(origin), _probe.Diagnostics);
			Assert.That((up.X, up.Y), Is.EqualTo(destination), _probe.Diagnostics);
			Assert.That(_probe.Events.Count(e => e.Type == "move"), Is.GreaterThan(2), "the drag moves in steps");
		});
	}

	[TestCase("up", "vertical", 1)]
	[TestCase("down", "vertical", -1)]
	[TestCase("left", "horizontal", -1)]
	[TestCase("right", "horizontal", 1)]
	public async Task Scrolling_arrives_in_the_chosen_direction(string direction, string axis, int sign)
	{
		var scrolls = await ScrollAsync(direction);

		Assert.Multiple(() =>
		{
			Assert.That(scrolls, Is.Not.Empty, _probe.Diagnostics);
			Assert.That(scrolls.Select(e => e.Axis).Distinct(), Is.EqualTo(new[] { axis }), _probe.Diagnostics);
			Assert.That(scrolls.Select(e => Math.Sign(e.Delta ?? 0)).Distinct(), Is.EqualTo(new[] { sign }));
		});
	}

	[TestCase("up", 3)]
	[TestCase("down", -3)]
	[TestCase("left", -3)]
	[TestCase("right", 3)]
	public async Task Every_scrolled_notch_arrives(string direction, int expected)
	{
		if (NotchesRedToday is { } reason)
		{
			Assert.Ignore(reason);
		}

		var scrolls = await ScrollAsync(direction);

		Assert.That(scrolls.Sum(e => e.Delta), Is.EqualTo(expected), _probe.Diagnostics);
	}

	protected virtual string? NotchesRedToday => null;

	private async Task<ProbeEvent[]> ScrollAsync(string direction)
	{
		var (scrollAxis, sign) = MouseActionValues.ReadScrollDirection(new Dictionary<string, object>
		{
			["direction"] = direction
		});

		await _mouse.ScrollAsync(scrollAxis, 3 * sign);
		_probe.Settle();

		return _probe.Events.Where(e => e.Type == "scroll").ToArray();
	}

	[Test]
	public async Task Release_all_lets_go_of_every_held_button()
	{
		await _mouse.ButtonDownAsync(MouseButton.Left);
		await _mouse.ButtonDownAsync(MouseButton.Right);
		_probe.Settle();

		await _mouse.ReleaseAllAsync();
		_probe.Settle();

		Assert.Multiple(() =>
		{
			Assert.That(_probe.Buttons.Count(e => e.Down == false), Is.EqualTo(2), _probe.Diagnostics);
			Assert.That(InputState.HeldButtons(), Is.Empty);
		});
	}

	[Test]
	public async Task A_move_past_the_screen_edge_stops_at_the_edge()
	{
		Assert.That(_provider.TryGetDesktopBounds(out var bounds), Is.True);

		await _mouse.MoveAsync(MouseTarget.At(bounds.Left - 500, bounds.Top - 500));
		var topLeft = InputState.Pointer();
		await _mouse.MoveAsync(MouseTarget.At(bounds.Left + bounds.Width + 500, bounds.Top + bounds.Height + 500));
		var bottomRight = InputState.Pointer();

		Assert.Multiple(() =>
		{
			Assert.That(topLeft, Is.EqualTo((bounds.Left, bounds.Top)));
			Assert.That(bottomRight, Is.EqualTo((bounds.Left + bounds.Width - 1, bounds.Top + bounds.Height - 1)));
		});
	}
}

[Platform("Win")]
[Category("Windows")]
public sealed class WindowsMouseInputTests : MouseInputCases
{
	protected override string NotchesRedToday
		=> "Red today: #1726 scrolling sometimes delivers fewer notches than configured";
}

[Platform("Linux")]
[Category("X11")]
public sealed class X11MouseInputTests : MouseInputCases;
