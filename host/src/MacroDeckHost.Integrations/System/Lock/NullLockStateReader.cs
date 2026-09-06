namespace MacroDeckHost.Integrations.System.Lock;

public sealed class NullLockStateReader : ILockStateReader
{
	public bool IsSupported => false;

	public string? UnsupportedReason => "Lock-state detection is not supported on this operating system.";

	public bool? IsLocked() => null;
}
