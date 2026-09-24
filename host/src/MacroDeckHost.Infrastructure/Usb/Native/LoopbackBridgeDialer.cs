using System.Net;
using System.Net.Sockets;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Usb;

namespace MacroDeckHost.Infrastructure.Usb.Native;

internal interface ILinkStreamDialer
{
	ValueTask<Stream?> DialAsync(string deviceKey, CancellationToken cancellationToken);
}

internal sealed class LoopbackBridgeDialer : ILinkStreamDialer
{
	private readonly IHostListenerState _listenerState;
	private readonly BridgedConnections _bridgedConnections;

	public LoopbackBridgeDialer(IHostListenerState listenerState, BridgedConnections bridgedConnections)
	{
		_listenerState = listenerState;
		_bridgedConnections = bridgedConnections;
	}

	// Always a public listener, never the loopback listener: a bridged phone must log in like any client
	// (ADR 0030, ADR 0095). The companion app speaks plain HTTP over the link, hence no HTTPS target.
	public static IPEndPoint? Target(PublicEndpointSet endpoints)
		=> endpoints.HasPublicListener && endpoints.LocalClientEndpoint is { Ssl: false } endpoint
			? new IPEndPoint(IPAddress.Loopback, endpoint.Port)
			: null;

	public async ValueTask<Stream?> DialAsync(string deviceKey, CancellationToken cancellationToken)
	{
		if (Target(_listenerState.PublicEndpoints) is not { } target)
		{
			return null;
		}

		var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
		BridgedConnectionRegistration? registration = null;
		try
		{
			// Bound and registered before connecting, so the entry exists before Kestrel can accept.
			socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
			registration = _bridgedConnections.Register((IPEndPoint)socket.LocalEndPoint!, deviceKey);
			await socket.ConnectAsync(target, cancellationToken);
			return new BridgedStream(socket, registration, _bridgedConnections);
		}
		catch
		{
			if (registration is not null)
			{
				_bridgedConnections.Remove(registration);
			}

			socket.Dispose();
			throw;
		}
	}

	private sealed class BridgedStream : NetworkStream
	{
		private readonly BridgedConnectionRegistration _registration;
		private readonly BridgedConnections _bridgedConnections;
		private int _released;

		public BridgedStream(Socket socket,
			BridgedConnectionRegistration registration,
			BridgedConnections bridgedConnections)
			: base(socket, ownsSocket: true)
		{
			_registration = registration;
			_bridgedConnections = bridgedConnections;
		}

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
			if (Interlocked.Exchange(ref _released, 1) == 0)
			{
				_bridgedConnections.ReleaseAfterClose(_registration);
			}
		}
	}
}
