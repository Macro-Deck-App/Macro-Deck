namespace MacroDeckHost.Application.Lifecycle;

public sealed class UserSessionEnd
{
	// The bootstrapper sends this as the shutdown reason; SESSION_END_REASON in ui/bootstrapper/src/host.rs.
	public const string ShutdownReason = "session-end";

	private readonly Func<bool> _operatingSystemShuttingDown;
	private int _marked;

	public UserSessionEnd()
		: this(static () => false)
	{
	}

	public UserSessionEnd(Func<bool> operatingSystemShuttingDown)
	{
		_operatingSystemShuttingDown = operatingSystemShuttingDown;
	}

	public bool IsEnding => Volatile.Read(ref _marked) == 1 || _operatingSystemShuttingDown();

	public void Mark() => Volatile.Write(ref _marked, 1);
}
