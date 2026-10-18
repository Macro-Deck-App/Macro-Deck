using System.Net;
using System.Net.Sockets;

namespace MacroDeckHost.Integrations.Feeds.Protocol;

internal interface IFeedAddressResolver
{
	Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}

internal sealed class DnsFeedAddressResolver : IFeedAddressResolver
{
	public static DnsFeedAddressResolver Instance { get; } = new();

	public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
	{
		var bare = host.Trim('[', ']');
		if (IPAddress.TryParse(bare, out var literal))
		{
			return [literal];
		}

		return await Dns.GetHostAddressesAsync(bare, cancellationToken).ConfigureAwait(false);
	}
}

internal sealed class FeedAddressBlockedException : Exception
{
	public FeedAddressBlockedException()
		: base("The feed address points into a local or private network.")
	{
	}

	public FeedAddressBlockedException(string message)
		: base(message)
	{
	}

	public FeedAddressBlockedException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}

internal static class FeedHttp
{
	public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

	public static HttpMessageHandler CreateHandler(bool allowLocalNetwork)
	{
		return IntegrationHttp.CreateHandler(handler
			=> Configure(handler, allowLocalNetwork, DnsFeedAddressResolver.Instance));
	}

	// A proxy would connect on our behalf and hide the real target from the address check, and an automatic
	// redirect would skip it, so both stay off and every connection is checked where it is opened.
	internal static void Configure(SocketsHttpHandler handler, bool allowLocalNetwork, IFeedAddressResolver resolver)
	{
		ArgumentNullException.ThrowIfNull(handler);
		ArgumentNullException.ThrowIfNull(resolver);

		handler.UseProxy = false;
		handler.AllowAutoRedirect = false;
		handler.AutomaticDecompression = DecompressionMethods.All;
		handler.ConnectTimeout = ConnectTimeout;
		handler.PooledConnectionLifetime = TimeSpan.FromMinutes(5);
		handler.ConnectCallback = (context, cancellationToken)
			=> ConnectAsync(context.DnsEndPoint, allowLocalNetwork, resolver, cancellationToken);
	}

	private static async ValueTask<Stream> ConnectAsync(
		DnsEndPoint endPoint,
		bool allowLocalNetwork,
		IFeedAddressResolver resolver,
		CancellationToken cancellationToken)
	{
		var addresses = await resolver.ResolveAsync(endPoint.Host, cancellationToken).ConfigureAwait(false);
		if (!FeedAddressGuard.IsAllowed(addresses, allowLocalNetwork))
		{
			throw new FeedAddressBlockedException();
		}

		var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
		try
		{
			await socket.ConnectAsync([.. addresses], endPoint.Port, cancellationToken).ConfigureAwait(false);
			return new NetworkStream(socket, true);
		}
		catch
		{
			socket.Dispose();
			throw;
		}
	}
}
