using System.Reflection;
using MacroDeck.Sdk;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Integrations;

public static class IntegrationDiscovery
{
	public static IReadOnlyList<IIntegration> DiscoverIntegrations(
		ILogger logger)
	{
		var integrations = new List<IIntegration>();
		var assembly = Assembly.GetExecutingAssembly();

		try
		{
			var types = assembly.GetTypes()
				.Where(t => t is { IsClass: true, IsAbstract: false } &&
					t.GetCustomAttribute<MacroDeckIntegrationAttribute>() is not null &&
					typeof(IIntegration).IsAssignableFrom(t));

			foreach (var type in types)
			{
				var attribute = type.GetCustomAttribute<MacroDeckIntegrationAttribute>()!;
				if (!attribute.RunsHere())
				{
					logger.Debug("Skipping integration type '{TypeName}': it does not run on {Platform}",
						type.FullName,
						MacroDeckIntegrationAttribute.Current);
					continue;
				}

				try
				{
					if (Activator.CreateInstance(type) is IIntegration integration)
					{
						integrations.Add(integration);
						logger.Information("Discovered integration '{IntegrationId}' from {Assembly}",
							integration.Id,
							assembly.GetName().Name);
					}
				}
				catch (Exception ex)
				{
					logger.Error(ex, "Failed to instantiate integration type '{TypeName}'", type.FullName);
				}
			}
		}
		catch (ReflectionTypeLoadException ex)
		{
			logger.Error(ex, "Failed to load types from assembly '{Assembly}'", assembly.GetName().Name);
		}

		return integrations.AsReadOnly();
	}
}
