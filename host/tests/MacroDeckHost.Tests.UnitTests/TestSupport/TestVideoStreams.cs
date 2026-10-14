using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Tests.UnitTests.VideoStreams;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal static class TestVideoStreams
{
	public static VideoStreamProviderHost Host()
	{
		return Host(new VideoStreamWorld());
	}

	public static VideoStreamProviderHost Host(VideoStreamWorld world)
	{
		return new VideoStreamProviderHost(world.Registry, world.Broker, TimeProvider.System, Serilog.Core.Logger.None);
	}
}
