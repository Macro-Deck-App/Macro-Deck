using System.Net.WebSockets;
using MacroDeck.Sdk.Http;

namespace MacroDeckHost.Integrations;

internal static class IntegrationHttp
{
	private static readonly ForwardingHostHttp _live = new();

	// In process every context hands out the same live value; out of process each plugin binds its own.
	internal static void Use(IHostHttp http)
	{
		ArgumentNullException.ThrowIfNull(http);

		_live.Target = http;
	}

	internal static IHostHttp Live => _live;

	internal static HttpMessageHandler CreateHandler(
		Action<SocketsHttpHandler>? configure = null,
		IHostHttp? source = null)
	{
		var inner = new SocketsHttpHandler();
		configure?.Invoke(inner);

		return new UserAgentHandler(source ?? Live, inner);
	}

	internal static HttpClient CreateClient(
		TimeSpan? timeout = null,
		Action<SocketsHttpHandler>? configure = null,
		IHostHttp? source = null)
	{
		var client = new HttpClient(CreateHandler(configure, source), true);
		if (timeout is { } value)
		{
			client.Timeout = value;
		}

		return client;
	}

	internal static HttpClient CreateClient(HttpMessageHandler handler, bool disposeHandler, TimeSpan timeout)
	{
		return new HttpClient(handler, disposeHandler) { Timeout = timeout };
	}

	internal static ClientWebSocket CreateWebSocket(IHostHttp? source = null)
	{
		var socket = new ClientWebSocket();
		socket.Options.SetRequestHeader("User-Agent", (source ?? Live).UserAgent);
		return socket;
	}

	private sealed class ForwardingHostHttp : IHostHttp
	{
		private volatile IHostHttp _target = DefaultHostHttp.Instance;

		public IHostHttp Target
		{
			set => _target = value;
		}

		public string UserAgent => _target.UserAgent;
	}

	private sealed class UserAgentHandler(IHostHttp source, HttpMessageHandler inner) : DelegatingHandler(inner)
	{
		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			if (request.Headers.UserAgent.Count == 0)
			{
				request.Headers.TryAddWithoutValidation("User-Agent", source.UserAgent);
			}

			return base.SendAsync(request, cancellationToken);
		}
	}
}
