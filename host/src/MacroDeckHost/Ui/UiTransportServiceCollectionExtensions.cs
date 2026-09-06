using System.Reflection;
using MacroDeckHost.Application.Ui.Transport;

namespace MacroDeckHost.Ui;

public static class UiTransportServiceCollectionExtensions
{
	public static IServiceCollection AddUiTransport(this IServiceCollection services,
		params Assembly[] markerAssemblies)
	{
		var handlerInterfaceType = typeof(IUiTransportMessageHandler<,>);

		foreach (var assembly in markerAssemblies.Distinct())
		{
			var handlerTypes = assembly.GetTypes()
				.Where(t => t is { IsClass: true, IsAbstract: false });

			foreach (var handlerType in handlerTypes)
			{
				var implementedInterfaces = handlerType.GetInterfaces()
					.Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == handlerInterfaceType);

				foreach (var implementedInterface in implementedInterfaces)
				{
					services.AddScoped(implementedInterface, handlerType);
				}
			}
		}

		services.AddSingleton<WebSocketUiTransport>();
		services.AddSingleton<IUiTransport>(provider => provider.GetRequiredService<WebSocketUiTransport>());
		services.AddSingleton<IUiWebSocketTickets, UiWebSocketTickets>();
		services.AddSingleton<UiWebSocketEndpoint>();

		return services;
	}
}
