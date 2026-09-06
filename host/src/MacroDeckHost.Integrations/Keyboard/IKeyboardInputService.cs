namespace MacroDeckHost.Integrations.Keyboard;

public enum KeyboardSessionUnavailableReason
{
	NotFocused,

	TargetNotFound,

	FocusFailed,

	Unsupported
}

public readonly struct KeyboardSessionResult
{
	private KeyboardSessionResult(IKeyboardInputSession? session, KeyboardSessionUnavailableReason? unavailableReason)
	{
		Session = session;
		UnavailableReason = unavailableReason;
	}

	public IKeyboardInputSession? Session { get; }

	public KeyboardSessionUnavailableReason? UnavailableReason { get; }

	public static KeyboardSessionResult Opened(IKeyboardInputSession session) => new(session, unavailableReason: null);

	public static KeyboardSessionResult Unavailable(KeyboardSessionUnavailableReason reason) => new(null, reason);
}

public interface IKeyboardInputService
{
	bool IsSupported { get; }

	bool RequiresPermission { get; }

	bool HasPermission { get; }

	Task RequestPermissionAsync(CancellationToken cancellationToken = default);

	Task PressComboAsync(
		KeyModifier modifiers,
		KeyCode key,
		int repeat = 1,
		int repeatDelayMs = 0,
		CancellationToken cancellationToken = default);

	Task TypeTextAsync(string text, CancellationToken cancellationToken = default);

	Task KeyDownAsync(KeyModifier modifiers, KeyCode key, CancellationToken cancellationToken = default);

	Task KeyUpAsync(KeyModifier modifiers, KeyCode key, CancellationToken cancellationToken = default);

	Task ReleaseAllAsync(CancellationToken cancellationToken = default);

	Task<KeyboardSessionResult> OpenSessionAsync(KeyboardTarget target, CancellationToken cancellationToken = default);
}
