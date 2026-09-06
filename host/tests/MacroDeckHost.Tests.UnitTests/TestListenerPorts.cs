using MacroDeckHost.Application.Configuration;

namespace MacroDeckHost.Tests.UnitTests;

public static class TestListenerPorts
{
	public const int Loopback = 54321;
}

[SetUpFixture]
public class ResolvedListenerPortsSetup
{
	[OneTimeSetUp]
	public void PublishLoopbackPort() => ResolvedLoopbackPort.Set(TestListenerPorts.Loopback);
}
