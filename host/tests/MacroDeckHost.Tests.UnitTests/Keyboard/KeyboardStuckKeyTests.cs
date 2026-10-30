using MacroDeckHost.Integrations.Keyboard.Models;
using MacroDeckHost.Integrations.Keyboard.Native;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Keyboard;

// A key down that is held system wide after the action that pressed it stopped or failed leaves the user's
// whole desktop with a stuck Ctrl or Shift (#908). Whether a sequence that finishes normally may keep a key
// down on purpose is not decided, so only stopped and failed runs are covered here.
public class KeyboardStuckKeyTests
{
	private FakeKeyboardInputProvider _provider = null!;
	private KeyboardInputService _service = null!;
	private KeyboardSequenceExecutor _executor = null!;

	[SetUp]
	public void SetUp()
	{
		_provider = new FakeKeyboardInputProvider();
		_service = new KeyboardInputService(_provider, new KeyboardLayoutService());
		_executor = new KeyboardSequenceExecutor(_service, new KeyboardLayoutService(), Log.Logger);
	}

	[Test]
	public void A_sequence_cancelled_while_it_holds_a_key_releases_it()
	{
		using var cancellation = new CancellationTokenSource();
		_provider.AfterKeyDown = _ => cancellation.Cancel();

		Assert.That(async () => await _executor.ExecuteAsync(HoldCtrlThenWait(), default, cancellation.Token),
			Throws.InstanceOf<OperationCanceledException>());
		Assert.That(_provider.Pressed, Is.Empty);
	}

	[Test]
	public void A_sequence_whose_later_step_fails_releases_what_earlier_steps_held()
	{
		_provider.FailOnKeyDown = KeyCode.X;
		var sequence = new KeyboardSequence
		{
			Steps =
			[
				new KeyDownStep { Modifiers = ["Shift"], Key = "A" },
				new KeyComboStep { Key = "X" },
				new KeyUpStep { Modifiers = ["Shift"], Key = "A" }
			]
		};

		Assert.That(async () => await _executor.ExecuteAsync(sequence), Throws.InvalidOperationException);
		Assert.That(_provider.Pressed, Is.Empty);
	}

	[Test]
	public void A_combo_whose_key_fails_to_press_releases_its_modifiers()
	{
		_provider.FailOnKeyDown = KeyCode.K;

		Assert.That(async () => await _service.PressComboAsync(KeyModifier.Control | KeyModifier.Shift, KeyCode.K),
			Throws.InvalidOperationException);
		Assert.That(_provider.Pressed, Is.Empty);
	}

	[Test]
	public void A_cancelled_focus_then_send_sequence_releases_the_key_before_focus_goes_back()
	{
		var timeline = new List<string>();
		_provider.SupportsWindowTargeting = true;
		_provider.Timeline = timeline;
		_provider.Target = new FakeTargetWindow { Timeline = timeline };
		using var cancellation = new CancellationTokenSource();
		_provider.AfterKeyDown = _ => cancellation.Cancel();

		Assert.That(async () => await _executor.ExecuteAsync(HoldCtrlThenWait(),
				new KeyboardTarget("notepad", KeyboardTargetMode.FocusThenSend),
				cancellation.Token),
			Throws.InstanceOf<OperationCanceledException>());
		Assert.That(timeline, Is.EqualTo(new[] { "down:LeftControl", "up:LeftControl", "restore-focus" }));
	}

	[Test]
	public async Task A_key_another_sequence_holds_is_left_to_that_sequence()
	{
		await _service.KeyDownAsync(KeyModifier.None, KeyCode.LeftShift);
		using var cancellation = new CancellationTokenSource();
		_provider.AfterKeyDown = _ => cancellation.Cancel();

		var sequence = new KeyboardSequence
		{
			Steps =
			[
				new KeyDownStep { Modifiers = ["Shift"], Key = "B" },
				new DelayStep { Milliseconds = 60_000 }
			]
		};

		Assert.That(async () => await _executor.ExecuteAsync(sequence, default, cancellation.Token),
			Throws.InstanceOf<OperationCanceledException>());
		Assert.That(_provider.Pressed, Is.EquivalentTo(new[] { KeyCode.LeftShift }));
	}

	private static KeyboardSequence HoldCtrlThenWait()
	{
		return new KeyboardSequence
		{
			Steps =
			[
				new KeyDownStep { Modifiers = ["Ctrl"], Key = string.Empty },
				new DelayStep { Milliseconds = 60_000 },
				new KeyUpStep { Modifiers = ["Ctrl"], Key = string.Empty }
			]
		};
	}
}
