using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.ClientTargets;
using MacroDeckHost.Infrastructure.ClientTargets.CarThing;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Infrastructure.ClientTargets;

public static class ClientTargetsServiceCollectionExtensions
{
	/// <summary>
	/// Registers the device provisioners for the packaged Web Client targets (issue #727). A new
	/// target's setup workflow joins by adding one line here; nothing else in the host changes.
	/// </summary>
	public static IServiceCollection AddWebClientTargets(this IServiceCollection services)
	{
		services.AddSingleton<IFileSystemScratchWriter, FileSystemScratchWriter>();
		// Scoped, not singleton: IAuthService reaches the database, which is scoped.
		services.AddScoped<IWebClientTargetProvisioner>(sp => new CarThingProvisioner(
			sp.GetRequiredService<IAdbManager>(),
			sp.GetRequiredService<IFileSystemScratchWriter>(),
			sp.GetRequiredService<IAuthService>(),
			sp.GetRequiredService<IServiceScopeFactory>()));
		services.AddScoped<WebClientTargetProvisioningRegistry>();
		return services;
	}
}
