using MacroDeckHost.Integrations.Keyboard;
using MacroDeckHost.Integrations.Keyboard.Native;

namespace MacroDeckHost.Tests.UnitTests.Keyboard;

public class KeyboardInputServiceTests
{
	private static readonly string[] _expectedTypedText = ["Hello World"];

	private FakeKeyboardInputProvider _provider = null!;
	private KeyboardInputService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_provider = new FakeKeyboardInputProvider();
		_service = new KeyboardInputService(_provider, new KeyboardLayoutService());
	}

	[Test]
	public async Task PressCombo_presses_modifiers_then_key_and_releases_in_reverse()
	{
		await _service.PressComboAsync(KeyModifier.Control, KeyCode.C);

		Assert.That(_provider.Events,
			Is.EqualTo(new[]
			{
				(KeyCode.LeftControl, true),
				(KeyCode.C, true),
				(KeyCode.C, false),
				(KeyCode.LeftControl, false)
			}));
	}

	[Test]
	public async Task PressCombo_with_right_alt_presses_the_right_hand_key_not_the_left()
	{
		await _service.PressComboAsync(KeyModifier.RightAlt, KeyCode.F1);

		Assert.That(_provider.Events,
			Is.EqualTo(new[]
			{
				(KeyCode.RightAlt, true),
				(KeyCode.F1, true),
				(KeyCode.F1, false),
				(KeyCode.RightAlt, false)
			}));
	}

	[Test]
	public async Task PressCombo_orders_multiple_modifiers_and_reverses_on_release()
	{
		await _service.PressComboAsync(KeyModifier.Control | KeyModifier.Shift, KeyCode.S);

		Assert.That(_provider.Events,
			Is.EqualTo(new[]
			{
				(KeyCode.LeftControl, true),
				(KeyCode.LeftShift, true),
				(KeyCode.S, true),
				(KeyCode.S, false),
				(KeyCode.LeftShift, false),
				(KeyCode.LeftControl, false)
			}));
	}

	[Test]
	public async Task PressCombo_repeats_the_requested_number_of_times()
	{
		await _service.PressComboAsync(KeyModifier.None, KeyCode.Enter, repeat: 3);

		var enterPresses = _provider.Events.Count(e => e is { Key: KeyCode.Enter, Down: true });
		Assert.That(enterPresses, Is.EqualTo(3));
	}

	[Test]
	public async Task TypeText_forwards_to_provider()
	{
		await _service.TypeTextAsync("Hello World");
		Assert.That(_provider.TypedText, Is.EqualTo(_expectedTypedText));
	}

	[Test]
	public async Task KeyDown_holds_keys_until_ReleaseAll()
	{
		await _service.KeyDownAsync(KeyModifier.None, KeyCode.W);
		await _service.KeyDownAsync(KeyModifier.Shift, KeyCode.A);

		Assert.That(_provider.Events, Does.Not.Contain((KeyCode.W, false)));

		await _service.ReleaseAllAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_provider.Events, Contains.Item((KeyCode.W, false)));
			Assert.That(_provider.Events, Contains.Item((KeyCode.A, false)));
			Assert.That(_provider.Events, Contains.Item((KeyCode.LeftShift, false)));
		});
	}

	[Test]
	public async Task KeyUp_on_unheld_key_is_a_safe_no_op()
	{
		await _service.KeyUpAsync(KeyModifier.None, KeyCode.W);
		Assert.That(_provider.Events, Is.Empty);
	}

	[Test]
	public async Task KeyUp_releases_a_previously_held_key_once()
	{
		await _service.KeyDownAsync(KeyModifier.None, KeyCode.W);
		await _service.KeyUpAsync(KeyModifier.None, KeyCode.W);
		await _service.KeyUpAsync(KeyModifier.None, KeyCode.W); // second release is a no-op

		Assert.That(_provider.Events.Count(e => e == (KeyCode.W, false)), Is.EqualTo(1));
	}

	private static readonly KeyboardTarget _backgroundCode = new("code", KeyboardTargetMode.Background);

	private FakeTargetWindow BackgroundTarget()
	{
		_provider.SupportsWindowTargeting = true;
		_provider.SupportsBackgroundSend = true;
		_provider.BackgroundModifiers = KeyModifier.Control | KeyModifier.Shift | KeyModifier.Alt | KeyModifier.Meta;
		var target = new FakeTargetWindow();
		_provider.Target = target;
		return target;
	}

	[Test]
	public async Task A_background_hold_stays_in_the_target_until_a_later_release()
	{
		var target = BackgroundTarget();

		var unavailable = await _service.KeyDownAsync(KeyModifier.None, KeyCode.W, _backgroundCode);
		Assert.That(target.Events, Is.EqualTo(new[] { (KeyCode.W, true) }));

		await _service.KeyUpAsync(KeyModifier.None, KeyCode.W);

		Assert.Multiple(() =>
		{
			Assert.That(unavailable, Is.Null);
			Assert.That(target.Events, Is.EqualTo(new[] { (KeyCode.W, true), (KeyCode.W, false) }));
			Assert.That(_provider.Events, Is.Empty);
			Assert.That(target.Disposed, Is.True);
		});
	}

	[Test]
	public async Task Background_holds_on_the_same_app_share_one_window()
	{
		var target = BackgroundTarget();

		await _service.KeyDownAsync(KeyModifier.Shift, KeyCode.None, _backgroundCode);
		await _service.KeyDownAsync(KeyModifier.None, KeyCode.A, new KeyboardTarget("Code.exe", KeyboardTargetMode.Background));
		await _service.KeyUpAsync(KeyModifier.None, KeyCode.A);

		Assert.Multiple(() =>
		{
			Assert.That(_provider.ResolveCount, Is.EqualTo(1));
			Assert.That(target.Disposed, Is.False);
		});

		await _service.KeyUpAsync(KeyModifier.Shift, KeyCode.None);

		Assert.Multiple(() =>
		{
			Assert.That(target.Events,
				Is.EqualTo(new[]
				{
					(KeyCode.LeftShift, true),
					(KeyCode.A, true),
					(KeyCode.A, false),
					(KeyCode.LeftShift, false)
				}));
			Assert.That(target.Disposed, Is.True);
		});
	}

	[Test]
	public async Task ReleaseAll_releases_background_holds()
	{
		var target = BackgroundTarget();
		await _service.KeyDownAsync(KeyModifier.None, KeyCode.W, _backgroundCode);

		await _service.ReleaseAllAsync();

		Assert.That(target.Events, Is.EqualTo(new[] { (KeyCode.W, true), (KeyCode.W, false) }));
	}

	[Test]
	public async Task A_background_hold_fails_when_the_target_is_not_running()
	{
		BackgroundTarget();
		_provider.Target = null;

		var unavailable = await _service.KeyDownAsync(KeyModifier.None, KeyCode.W, _backgroundCode);

		Assert.Multiple(() =>
		{
			Assert.That(unavailable, Is.EqualTo(KeyboardSessionUnavailableReason.TargetNotFound));
			Assert.That(_provider.Events, Is.Empty);
		});
	}

	[Test]
	public async Task A_background_hold_without_platform_support_sends_nothing()
	{
		var target = BackgroundTarget();
		_provider.SupportsBackgroundSend = false;

		var unavailable = await _service.KeyDownAsync(KeyModifier.None, KeyCode.W, _backgroundCode);

		Assert.Multiple(() =>
		{
			Assert.That(unavailable, Is.EqualTo(KeyboardSessionUnavailableReason.Unsupported));
			Assert.That(target.Events, Is.Empty);
			Assert.That(_provider.Events, Is.Empty);
		});
	}

	[Test]
	public async Task A_background_hold_of_a_modifier_the_platform_cannot_deliver_sends_nothing()
	{
		var target = BackgroundTarget();
		_provider.BackgroundModifiers = KeyModifier.None;

		var unavailable = await _service.KeyDownAsync(KeyModifier.None, KeyCode.LeftControl, _backgroundCode);

		Assert.Multiple(() =>
		{
			Assert.That(unavailable, Is.EqualTo(KeyboardSessionUnavailableReason.BackgroundModifiersUnsupported));
			Assert.That(target.Events, Is.Empty);
			Assert.That(_provider.Events, Is.Empty);
		});
	}

	[Test]
	public async Task An_only_when_focused_hold_is_skipped_while_another_app_is_focused()
	{
		_provider.SupportsWindowTargeting = true;
		_provider.ForegroundProcessName = "notepad";

		var unavailable = await _service.KeyDownAsync(KeyModifier.None, KeyCode.W,
			new KeyboardTarget("code", KeyboardTargetMode.WhenFocused));

		Assert.Multiple(() =>
		{
			Assert.That(unavailable, Is.EqualTo(KeyboardSessionUnavailableReason.NotFocused));
			Assert.That(_provider.Events, Is.Empty);
		});
	}

	[Test]
	public async Task An_only_when_focused_hold_is_released_after_focus_moves_away()
	{
		_provider.SupportsWindowTargeting = true;
		_provider.ForegroundProcessName = "code";
		await _service.KeyDownAsync(KeyModifier.None, KeyCode.W, new KeyboardTarget("code", KeyboardTargetMode.WhenFocused));

		_provider.ForegroundProcessName = "notepad";
		await _service.KeyUpAsync(KeyModifier.None, KeyCode.W);

		Assert.That(_provider.Events, Is.EqualTo(new[] { (KeyCode.W, true), (KeyCode.W, false) }));
	}

	[Test]
	public async Task A_focus_then_send_hold_is_unsupported_and_sends_nothing()
	{
		var target = BackgroundTarget();

		var unavailable = await _service.KeyDownAsync(KeyModifier.None, KeyCode.W,
			new KeyboardTarget("code", KeyboardTargetMode.FocusThenSend));

		Assert.Multiple(() =>
		{
			Assert.That(unavailable, Is.EqualTo(KeyboardSessionUnavailableReason.Unsupported));
			Assert.That(target.Events, Is.Empty);
			Assert.That(target.FocusCalls, Is.EqualTo(0));
			Assert.That(_provider.Events, Is.Empty);
		});
	}
}
