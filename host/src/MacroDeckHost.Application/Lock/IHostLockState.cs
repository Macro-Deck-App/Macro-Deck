namespace MacroDeckHost.Application.HostLocking;

public interface IHostLockState
{
	bool IsSupported { get; }

	bool IsLocked { get; }
}
