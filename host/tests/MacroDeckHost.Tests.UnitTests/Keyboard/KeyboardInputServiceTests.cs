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
}
