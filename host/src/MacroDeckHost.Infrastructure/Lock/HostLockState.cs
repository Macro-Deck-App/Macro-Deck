using MacroDeckHost.Application.HostLocking;

namespace MacroDeckHost.Infrastructure.HostLocking;

public sealed class HostLockState : IHostLockState
{
	public bool IsSupported { get; internal set; }

	public bool IsLocked { get; internal set; }
}
