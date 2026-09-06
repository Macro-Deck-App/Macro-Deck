using MacroDeckHost.Infrastructure.Integrations;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal static class TestDeviceProviders
{
	/// <summary>A device-provider host wired to a recording registry, for tests about something else.</summary>
	public static DeviceProviderHost Host()
		=> new(new FakePluginDeviceRegistry(), TimeProvider.System, Serilog.Core.Logger.None);
}
