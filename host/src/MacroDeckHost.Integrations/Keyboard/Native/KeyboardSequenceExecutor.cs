using MacroDeckHost.Integrations.Keyboard.Models;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Integrations.Keyboard.Native;

public sealed class KeyboardSequenceExecutor : IKeyboardSequenceExecutor
{
	private const int MaxSequenceRepeat = 10_000;
	private const int MaxStepRepeat = 10_000;
	private const int MaxDelayMs = 600_000;

	private readonly IKeyboardInputService _input;
	private readonly IKeyboardLayoutService _layout;
	private readonly ILogger _logger;

	public KeyboardSequenceExecutor(
		IKeyboardInputService input,
		IKeyboardLayoutService layout,
		ILogger logger)
	{
		_input = input;
		_layout = layout;
		_logger = logger.ForContext<KeyboardSequenceExecutor>();
	}

	public async Task<KeyboardSessionUnavailableReason?> ExecuteAsync(
		KeyboardSequence sequence,
		KeyboardTarget target = default,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(sequence);

		var result = await _input.OpenSessionAsync(target, cancellationToken).ConfigureAwait(false);
		if (result.Session is not { } sessionValue)
		{
			_logger.Debug("Keyboard sequence skipped: target {Process} could not be honoured", target.ProcessName);
			return result.UnavailableReason;
		}

		using var session = sessionValue;

		var repeat = Math.Clamp(sequence.Repeat <= 0 ? 1 : sequence.Repeat, 1, MaxSequenceRepeat);
		var repeatDelay = Math.Clamp(sequence.RepeatDelayMs, 0, MaxDelayMs);

		for (var iteration = 0; iteration < repeat; iteration++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			foreach (var step in sequence.Steps)
			{
				cancellationToken.ThrowIfCancellationRequested();
				await ExecuteStepAsync(session, step, cancellationToken).ConfigureAwait(false);
			}

			if (iteration < repeat - 1 && repeatDelay > 0)
			{
				await Task.Delay(repeatDelay, cancellationToken).ConfigureAwait(false);
			}
		}

		return null;
	}

	private async Task ExecuteStepAsync(
		IKeyboardInputSession session,
		KeyboardStep step,
		CancellationToken cancellationToken)
	{
		switch (step)
		{
			case KeyComboStep combo:
				await ExecuteComboAsync(session, combo, cancellationToken).ConfigureAwait(false);
				break;
			case TextStep text:
				await session.TypeTextAsync(text.Text, cancellationToken).ConfigureAwait(false);
				break;
			case DelayStep delay:
				var ms = Math.Clamp(delay.Milliseconds, 0, MaxDelayMs);
				if (ms > 0)
				{
					await Task.Delay(ms, cancellationToken).ConfigureAwait(false);
				}

				break;
			case KeyDownStep keyDown:
				var (downMods, downKey) = Resolve(keyDown.Modifiers, keyDown.Key);
				await _input.KeyDownAsync(downMods, downKey, cancellationToken).ConfigureAwait(false);
				break;
			case KeyUpStep keyUp:
				var (upMods, upKey) = Resolve(keyUp.Modifiers, keyUp.Key);
				await _input.KeyUpAsync(upMods, upKey, cancellationToken).ConfigureAwait(false);
				break;
			default:
				_logger.Warning("Skipping unsupported keyboard step {StepType}", step.GetType().Name);
				break;
		}
	}

	private async Task ExecuteComboAsync(
		IKeyboardInputSession session,
		KeyComboStep combo,
		CancellationToken cancellationToken)
	{
		var (modifiers, key) = Resolve(combo.Modifiers, combo.Key);
		if (key == KeyCode.None && modifiers == KeyModifier.None)
		{
			_logger.Warning("Skipping key combo step with no resolvable key: {Key}", combo.Key);
			return;
		}

		var repeat = Math.Clamp(combo.Repeat <= 0 ? 1 : combo.Repeat, 1, MaxStepRepeat);
		var repeatDelay = Math.Clamp(combo.RepeatDelayMs, 0, MaxDelayMs);
		await session.PressComboAsync(modifiers, key, repeat, repeatDelay, cancellationToken).ConfigureAwait(false);
	}

	private (KeyModifier Modifiers, KeyCode Key) Resolve(IReadOnlyList<string> modifierNames, string keyName)
	{
		var modifiers = _layout.ResolveModifiers(modifierNames);
		var key = KeyCode.None;
		if (!string.IsNullOrWhiteSpace(keyName) && !_layout.TryResolveKey(keyName, out key))
		{
			_logger.Warning("Could not resolve key '{Key}' in keyboard sequence", keyName);
		}

		return (modifiers, key);
	}
}
