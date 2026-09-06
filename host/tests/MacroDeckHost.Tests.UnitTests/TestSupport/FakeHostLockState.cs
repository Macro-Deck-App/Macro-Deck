using MacroDeckHost.Application.HostLocking;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class FakeHostLockState : IHostLockState
{
	public bool IsSupported { get; set; } = true;

	public bool IsLocked { get; set; }
}
