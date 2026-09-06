using MacroDeckHost.Application.Network.Tls;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Infrastructure.Network.Tls;

public static class PublicTlsServiceCollectionExtensions
{
	public static IServiceCollection AddPublicTlsCertificateStore(this IServiceCollection services)
	{
		services.AddSingleton<ILocalAddressProvider, LocalAddressProvider>();
		services.AddSingleton<IHostNameProvider, HostNameProvider>();
		services.AddSingleton<IPublicTlsCertificateStore, FilePublicTlsCertificateStore>();
		services.AddSingleton<PublicTlsBootstrapper>();
		return services;
	}
}
