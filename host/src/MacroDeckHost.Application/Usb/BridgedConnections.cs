using System.Collections.Concurrent;
using System.Net;

namespace MacroDeckHost.Application.Usb;

public interface IBridgedConnectionFeature
{
	string DeviceKey { get; }
}

public sealed record BridgedConnectionFeature(string DeviceKey) : IBridgedConnectionFeature;

public sealed class BridgedConnectionRegistration
{
	internal BridgedConnectionRegistration(IPEndPoint endpoint, string deviceKey)
	{
		Endpoint = endpoint;
		DeviceKey = deviceKey;
	}

	public IPEndPoint Endpoint { get; }

	public string DeviceKey { get; }

	internal DateTimeOffset? ClosedAt { get; set; }
}

public sealed class BridgedConnections
{
	public static readonly TimeSpan AcceptGrace = TimeSpan.FromSeconds(30);

	private readonly ConcurrentDictionary<IPEndPoint, BridgedConnectionRegistration> _pending = new();
	private readonly TimeProvider _timeProvider;

	public BridgedConnections(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
	}

	internal int PendingCount => _pending.Count;

	public BridgedConnectionRegistration Register(IPEndPoint localEndpoint, string deviceKey)
	{
		PruneClosed();
		var registration = new BridgedConnectionRegistration(Normalize(localEndpoint), deviceKey);
		_pending[registration.Endpoint] = registration;
		return registration;
	}

	public string? Take(IPEndPoint? remoteEndpoint)
	{
		if (remoteEndpoint is null)
		{
			return null;
		}

		return _pending.TryRemove(Normalize(remoteEndpoint), out var registration) ? registration.DeviceKey : null;
	}

	public void Remove(BridgedConnectionRegistration registration)
		=> _pending.TryRemove(new KeyValuePair<IPEndPoint, BridgedConnectionRegistration>(registration.Endpoint,
			registration));

	// Kestrel can accept a connection after the dialler already closed it, so an entry nobody took yet
	// outlives its socket for a grace period instead of vanishing and leaving that connection unstamped.
	public void ReleaseAfterClose(BridgedConnectionRegistration registration)
	{
		registration.ClosedAt = _timeProvider.GetUtcNow();
		PruneClosed();
	}

	public static IPEndPoint Normalize(IPEndPoint endpoint)
		=> endpoint.Address.IsIPv4MappedToIPv6
			? new IPEndPoint(endpoint.Address.MapToIPv4(), endpoint.Port)
			: endpoint;

	private void PruneClosed()
	{
		var cutoff = _timeProvider.GetUtcNow() - AcceptGrace;
		foreach (var (endpoint, registration) in _pending)
		{
			if (registration.ClosedAt is { } closedAt && closedAt <= cutoff)
			{
				_pending.TryRemove(new KeyValuePair<IPEndPoint, BridgedConnectionRegistration>(endpoint, registration));
			}
		}
	}
}
