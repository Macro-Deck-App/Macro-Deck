using System.Net.WebSockets;
using MacroDeckHost.Application.Network.Http;

namespace MacroDeckHost.Integrations.Http;

public static class IntegrationHttp
{
	private static readonly ForwardingSource _live = new();

	public static void Use(IHttpUserAgentSource source)
	{
		ArgumentNullException.ThrowIfNull(source);

		_live.Target = source;
	}

	internal static IHttpUserAgentSource Live => _live;

	internal static HttpMessageHandler CreateHandler(
		Action<SocketsHttpHandler>? configure = null,
		IHttpUserAgentSource? source = null)
	{
		var inner = new SocketsHttpHandler();
		configure?.Invoke(inner);

		return new UserAgentHandler(source ?? Live, inner);
	}

	internal static HttpClient CreateClient(
		TimeSpan? timeout = null,
		Action<SocketsHttpHandler>? configure = null,
		IHttpUserAgentSource? source = null)
	{
		var client = new HttpClient(CreateHandler(configure, source), disposeHandler: true);
		if (timeout is { } value)
		{
			client.Timeout = value;
		}

		return client;
	}

	internal static ClientWebSocket CreateWebSocket(IHttpUserAgentSource? source = null)
	{
		var socket = new ClientWebSocket();
		UserAgentWebSocket.Apply(socket.Options, source ?? Live);
		return socket;
	}

	private sealed class ForwardingSource : IHttpUserAgentSource
	{
		private volatile IHttpUserAgentSource? _target;

		public IHttpUserAgentSource? Target
		{
			set => _target = value;
		}

		public string Current => _target?.Current ?? HttpUserAgent.Default;
	}
}
