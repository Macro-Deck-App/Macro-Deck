using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Network.Tls;
using MacroDeckHost.Application.Usb;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Usb.Native;

public static class NativeUsbServiceCollectionExtensions
{
	public static IServiceCollection AddNativeUsb(this IServiceCollection services)
	{
		services.TryAddSingleton(sp => new BridgedConnections(sp.GetRequiredService<TimeProvider>()));
		services.AddSingleton<LibUsbHost>();
		services.AddSingleton<ILinkStreamDialer>(sp => new LoopbackBridgeDialer(
			sp.GetRequiredService<IHostListenerState>(),
			sp.GetRequiredService<BridgedConnections>()));
		services.AddSingleton(sp => new AccessoryCoordinator(sp.GetRequiredService<LibUsbHost>(),
			(pipe, deviceKey) => StartSession(sp,
				new AccessoryLinkCarrier(pipe, sp.GetRequiredService<TimeProvider>()),
				deviceKey),
			AccessoryReconnectPolicy.Default,
			AccessoryProtocol.IdentificationStrings(sp.GetRequiredService<IHostNameProvider>().MachineName),
			sp.GetRequiredService<ILogger>()));
		services.AddSingleton<INativeUsbSerials>(sp => sp.GetRequiredService<AccessoryCoordinator>());
		services.AddSingleton(sp => new UsbmuxCoordinator(
			new UsbmuxConnector(new UsbmuxClient(UsbmuxClient.DefaultEndpoint)),
			(carrier, deviceKey) => StartSession(sp, carrier, deviceKey),
			sp.GetRequiredService<ILogger>()));
		services.AddSingleton<INativeUsbManager>(sp => new NativeUsbManager(
			sp.GetRequiredService<IServiceScopeFactory>(),
			sp.GetRequiredService<IAdbManager>(),
			sp.GetRequiredService<IHostListenerState>(),
			sp.GetRequiredService<AccessoryCoordinator>(),
			sp.GetRequiredService<UsbmuxCoordinator>(),
			sp.GetRequiredService<TimeProvider>()));
		return services;
	}

	private static NativeLinkSession StartSession(IServiceProvider services, ILinkCarrier carrier, string deviceKey)
		=> new(carrier,
			deviceKey,
			services.GetRequiredService<ILinkStreamDialer>(),
			services.GetRequiredService<TimeProvider>(),
			services.GetRequiredService<ILogger>());
}
