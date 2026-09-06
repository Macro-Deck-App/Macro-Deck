using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Integrations.Adb;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Adb;

public static class AdbServiceCollectionExtensions
{
	public static IServiceCollection AddAdbManager(this IServiceCollection services)
	{
		services.AddSingleton<AdbProcessRunner>();
		services.AddSingleton<IAdbProcessRunner>(sp => sp.GetRequiredService<AdbProcessRunner>());
		// AdbManager's constructor is internal (by design: construction stays confined to this
		// assembly), so the built-in container's reflection-based AddSingleton<AdbManager>() cannot
		// see it - it only ever looks at public constructors. Calling the constructor directly from
		// this factory works because this file lives in the same assembly.
		services.AddSingleton(sp => new AdbManager(sp.GetRequiredService<IServiceScopeFactory>(),
			sp.GetRequiredService<IAdbProcessRunner>(),
			sp.GetRequiredService<IMacroDeckPaths>(),
			sp.GetRequiredService<IHostListenerState>(),
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<ILogger>()));
		services.AddSingleton<IAdbManager>(sp => sp.GetRequiredService<AdbManager>());
		services.AddSingleton<IAdbGateway>(sp => new AdbGateway(sp.GetRequiredService<IAdbManager>(),
			sp.GetRequiredService<ILogger>()));
		// AdbPlatformToolsInstaller's constructor is internal for the same reason as the two above; the
		// bare (unnamed) HttpClient is enough since the download timeout is enforced in code, not by the
		// client itself - see AdbPlatformToolsInstaller.InstallAsync.
		services.AddSingleton<IAdbPlatformToolsInstaller>(sp => new AdbPlatformToolsInstaller(
			sp.GetRequiredService<IHttpClientFactory>(),
			sp.GetRequiredService<IMacroDeckPaths>(),
			sp.GetRequiredService<ILogger>()));
		return services;
	}
}
