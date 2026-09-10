using System.Net.NetworkInformation;
using System.Net.Sockets;
using MacroDeckHost.Application.Network.Discovery;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Infrastructure.Network.Discovery;

public static class ServiceAdvertisementServiceCollectionExtensions
{
	public static IServiceCollection AddServiceAdvertisement(this IServiceCollection services)
	{
		services.AddSingleton<INetworkInterfaceSnapshotProvider, NetworkInterfaceSnapshotProvider>();
		services.AddSingleton(CreateAdvertiser);
		services.AddSingleton<ServiceAdvertisementBackgroundService>();
		services.AddHostedService(provider => provider.GetRequiredService<ServiceAdvertisementBackgroundService>());
		services.AddSingleton<IDiscoveryAdvertisementRefresher>(provider =>
			provider.GetRequiredService<ServiceAdvertisementBackgroundService>());
		return services;
	}

	private static IServiceAdvertiser CreateAdvertiser(IServiceProvider _)
	{
		if (OperatingSystem.IsMacOS())
		{
			return new MacOsDnsSdServiceAdvertiser();
		}

		if (OperatingSystem.IsWindows())
		{
			return new WindowsDnsServiceAdvertiser();
		}

		return OperatingSystem.IsLinux() ? new LinuxAvahiServiceAdvertiser() : new UnavailableServiceAdvertiser();
	}
}

internal sealed class UnavailableServiceAdvertiser : IServiceAdvertiser
{
	public event Action? StateChanged
	{
		add { }
		remove { }
	}

	public bool IsAvailable => false;

	public bool NeedsRepublish => false;

	public void CheckLiveness()
	{
	}

	public void Publish(ServiceAdvertisement advertisement)
	{
	}

	public void Withdraw()
	{
	}

	public void Dispose()
	{
	}
}

internal sealed class NetworkInterfaceSnapshotProvider : INetworkInterfaceSnapshotProvider
{
	public IReadOnlyList<NetworkInterfaceSnapshot> GetInterfaces()
	{
		var snapshots = new List<NetworkInterfaceSnapshot>();
		foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
		{
			try
			{
				var properties = nic.GetIPProperties();
				snapshots.Add(new NetworkInterfaceSnapshot(properties.GetIPv4Properties().Index,
					nic.Name,
					nic.Description,
					nic.NetworkInterfaceType,
					nic.OperationalStatus == OperationalStatus.Up,
					properties.UnicastAddresses
						.Select(address => address.Address)
						.Where(address => address.AddressFamily == AddressFamily.InterNetwork)
						.ToList()));
			}
			catch (NetworkInformationException)
			{
			}
		}

		return snapshots;
	}
}
