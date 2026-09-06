namespace MacroDeckHost.Integrations.System.Lock;

// A process-wide holder rather than a second ILockStateReader/ILockStateWatcher subscriber: it exists
// so the system_locked plugin variable can read the state the host already established without opening
// a second native subscription (mirrors FocusedApplicationSnapshot).
public sealed class LockStateSnapshot
{
	public static LockStateSnapshot Current { get; } = new();

	// bool? cannot itself be volatile (it is not one of the types the CLR allows for a volatile field),
	// so the tri-state is encoded into a plain volatile int instead.
	private const int UnknownState = -1;
	private const int UnlockedState = 0;
	private const int LockedState = 1;

	private volatile int _state = UnknownState;

	private LockStateSnapshot()
	{
	}

	public bool? Value => _state switch
	{
		LockedState => true,
		UnlockedState => false,
		_ => null
	};

	public void Set(bool? value) => _state = value switch
	{
		true => LockedState,
		false => UnlockedState,
		null => UnknownState
	};
}
