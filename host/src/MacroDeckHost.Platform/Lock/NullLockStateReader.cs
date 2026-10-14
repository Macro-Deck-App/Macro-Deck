namespace MacroDeckHost.Platform.Lock;

public sealed class NullLockStateReader : ILockStateReader
{
	public bool IsSupported => false;

	public string? UnsupportedReason => "Lock-state detection is not supported on this operating system.";

	public bool? IsLocked()
	{
		return null;
	}
}
