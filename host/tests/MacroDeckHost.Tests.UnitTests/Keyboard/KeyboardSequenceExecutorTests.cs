using MacroDeckHost.Integrations.Keyboard.Models;
using MacroDeckHost.Integrations.Keyboard;
using MacroDeckHost.Integrations.Keyboard.Native;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Keyboard;

public class KeyboardSequenceExecutorTests
{
	private static readonly string[] _expectedComboAndTextCalls = ["combo:Control+Cx1", "text:Hello World"];
	private static readonly string[] _expectedDownUpCalls = ["down:None+W", "up:None+W"];

	private FakeKeyboardInputService _input = null!;
	private KeyboardSequenceExecutor _executor = null!;

	[SetUp]
	public void SetUp()
	{
		_input = new FakeKeyboardInputService();
		_executor = new KeyboardSequenceExecutor(_input, new KeyboardLayoutService(), Log.Logger);
	}

	[Test]
	public async Task Executes_steps_in_order()
	{
		var sequence = new KeyboardSequence
		{
			Steps =
			[
				new KeyComboStep { Modifiers = ["Ctrl"], Key = "C" },
				new DelayStep { Milliseconds = 0 },
				new TextStep { Text = "Hello World" }
			]
		};

		await _executor.ExecuteAsync(sequence);

		Assert.That(_input.Calls, Is.EqualTo(_expectedComboAndTextCalls));
	}

	[Test]
	public async Task Repeats_the_whole_sequence()
	{
		var sequence = new KeyboardSequence
		{
			Repeat = 3,
			Steps = [new KeyComboStep { Key = "Enter" }]
		};

		await _executor.ExecuteAsync(sequence);

		Assert.That(_input.Calls.Count(c => c == "combo:None+Enterx1"), Is.EqualTo(3));
	}

	[Test]
	public async Task Dispatches_key_down_and_up_steps()
	{
		var sequence = new KeyboardSequence
		{
			Steps =
			[
				new KeyDownStep { Key = "W" },
				new KeyUpStep { Key = "W" }
			]
		};

		await _executor.ExecuteAsync(sequence);

		Assert.That(_input.Calls, Is.EqualTo(_expectedDownUpCalls));
	}

	[Test]
	public void Honours_cancellation()
	{
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		var sequence = new KeyboardSequence { Steps = [new TextStep { Text = "x" }] };

		Assert.ThrowsAsync<OperationCanceledException>(async () =>
			await _executor.ExecuteAsync(sequence, cancellationToken: cts.Token));
		Assert.That(_input.Calls, Is.Empty);
	}

	[Test]
	public async Task Skips_unresolvable_keys()
	{
		var sequence = new KeyboardSequence { Steps = [new KeyComboStep { Key = "NotAKey" }] };
		await _executor.ExecuteAsync(sequence);
		Assert.That(_input.Calls, Is.Empty);
	}

	[Test]
	public async Task Skips_the_whole_sequence_when_the_target_cannot_be_honoured()
	{
		_input.OpenSessionReturnsNull = true;
		var sequence = new KeyboardSequence { Steps = [new KeyComboStep { Modifiers = ["Ctrl"], Key = "C" }] };

		await _executor.ExecuteAsync(sequence, new KeyboardTarget("code", KeyboardTargetMode.Background));

		Assert.That(_input.Calls, Is.Empty);
	}

	[Test]
	public async Task Opens_the_session_once_with_the_requested_target()
	{
		var sequence = new KeyboardSequence { Steps = [new KeyComboStep { Key = "Enter" }] };
		var target = new KeyboardTarget("code", KeyboardTargetMode.FocusThenSend);

		await _executor.ExecuteAsync(sequence, target);

		Assert.That(_input.LastSessionTarget, Is.EqualTo(target));
	}
}
