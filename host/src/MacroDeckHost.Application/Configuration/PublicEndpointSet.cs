namespace MacroDeckHost.Application.Configuration;

public enum PublicTlsMode
{
	Disabled,

	Replace,

	Additional
}

public readonly record struct PublicEndpoint(int Port, bool Ssl);

public sealed record PublicEndpointSet
{
	private PublicEndpointSet(int publicPort, PublicTlsMode tlsMode, int? httpPort, int? httpsPort)
	{
		PublicPort = publicPort;
		TlsMode = tlsMode;
		HttpPort = httpPort;
		HttpsPort = httpsPort;
	}

	public int PublicPort { get; }

	public PublicTlsMode TlsMode { get; }

	public int? HttpPort { get; }

	public int? HttpsPort { get; }

	public IReadOnlyList<PublicEndpoint> Endpoints
	{
		get
		{
			// Plain HTTP is advertised first while both listeners are up. A client that takes the first
			// entry rather than trying them in order would otherwise be sent to an HTTPS origin whose
			// certificate authority it has not installed yet, which is every device on the upgrade that
			// turns HTTPS on by default. The setup wizard moves a device to HTTPS once it can verify it.
			var endpoints = new List<PublicEndpoint>(2);
			if (HttpPort is { } http)
			{
				endpoints.Add(new PublicEndpoint(http, false));
			}

			if (HttpsPort is { } https)
			{
				endpoints.Add(new PublicEndpoint(https, true));
			}

			return endpoints;
		}
	}

	public bool HasPublicListener => HttpPort is not null || HttpsPort is not null;

	public PublicEndpoint? LocalClientEndpoint => HttpPort is { } http
		? new PublicEndpoint(http, false)
		: HttpsPort is { } https
			? new PublicEndpoint(https, true)
			: null;

	public bool IsPublicPort(int port)
	{
		return port == HttpPort || port == HttpsPort;
	}

	public static PublicEndpointSet HttpOnly(int publicPort)
	{
		return new PublicEndpointSet(publicPort, PublicTlsMode.Disabled, publicPort, null);
	}

	public static PublicEndpointSet HttpsReplacingHttp(int publicPort)
	{
		return new PublicEndpointSet(publicPort, PublicTlsMode.Replace, null, publicPort);
	}

	public static PublicEndpointSet HttpAndHttps(int publicPort, int httpsPort)
	{
		return new PublicEndpointSet(publicPort, PublicTlsMode.Additional, publicPort, httpsPort);
	}

	public PublicEndpointSet WithoutHttp()
	{
		return new PublicEndpointSet(PublicPort, TlsMode, null, HttpsPort);
	}

	public PublicEndpointSet WithoutHttps()
	{
		return new PublicEndpointSet(PublicPort, TlsMode, HttpPort, null);
	}
}
