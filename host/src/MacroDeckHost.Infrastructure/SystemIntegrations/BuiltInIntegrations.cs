using System.Reflection;
using MacroDeck.Sdk;
using MacroDeckHost.Integrations;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.SystemIntegrations;

public static class BuiltInIntegrations
{
	public static IReadOnlyList<Assembly> Assemblies { get; } =
	[
		typeof(IntegrationDiscovery).Assembly,
		typeof(BuiltInIntegrations).Assembly
	];

	public static IReadOnlyList<IIntegration> Discover(ILogger logger)
	{
		return IntegrationDiscovery.DiscoverIntegrations(Assemblies, logger);
	}
}
