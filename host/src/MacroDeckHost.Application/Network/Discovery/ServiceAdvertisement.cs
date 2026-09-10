using System.Net;

namespace MacroDeckHost.Application.Network.Discovery;

public readonly record struct TxtEntry(string Key, string Value);

public readonly record struct AdvertisedInterface(int Index, IPAddress Ipv4);

public sealed class ServiceAdvertisement : IEquatable<ServiceAdvertisement>
{
	public ServiceAdvertisement(string wireName,
		int port,
		IReadOnlyList<TxtEntry> txt,
		IReadOnlyList<AdvertisedInterface> interfaces)
	{
		WireName = wireName;
		Port = port;
		Txt = txt;
		Interfaces = interfaces;
	}

	public string WireName { get; }

	public int Port { get; }

	public IReadOnlyList<TxtEntry> Txt { get; }

	public IReadOnlyList<AdvertisedInterface> Interfaces { get; }

	public bool DescribesSameService(ServiceAdvertisement? other)
		=> other is not null && WireName == other.WireName && Port == other.Port && Txt.SequenceEqual(other.Txt);

	public bool Equals(ServiceAdvertisement? other)
		=> DescribesSameService(other) && Interfaces.SequenceEqual(other!.Interfaces);

	public override bool Equals(object? obj) => Equals(obj as ServiceAdvertisement);

	public override int GetHashCode() => HashCode.Combine(WireName, Port, Interfaces.Count);
}
