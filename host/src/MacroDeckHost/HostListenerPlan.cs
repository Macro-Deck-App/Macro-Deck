using System.Net;
using System.Security.Cryptography.X509Certificates;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Network.Tls;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Serilog;

namespace MacroDeckHost;

internal sealed class HostListenerPlan
{
	private HostListenerPlan(PublicEndpointSet endpoints, int loopbackPort, X509Certificate2? certificate)
	{
		Endpoints = endpoints;
		LoopbackPort = loopbackPort;
		CertificateHolder = new PublicTlsCertificateHolder(certificate);
	}

	internal PublicEndpointSet Endpoints { get; }

	internal IPublicTlsCertificateHolder CertificateHolder { get; }

	internal int LoopbackPort { get; }

	internal bool PublicListenerAvailable => Endpoints.HasPublicListener;

	internal static HostListenerPlan Create(PublicEndpointSet requested,
		int loopbackPort,
		X509Certificate2? certificate = null,
		Func<int, PublicListenerProbeResult>? probe = null)
	{
		var effectiveProbe = probe ?? PublicListenerProbe.Probe;
		var effective = requested;

		if (requested.HttpsPort is { } httpsPort)
		{
			if (certificate is null)
			{
				Log.Warning("HTTPS is enabled but no usable certificate is available, so the HTTPS listener on " +
					"port {HttpsPort} is not opened. Configure a certificate in Settings > Network.",
					httpsPort);
				effective = effective.WithoutHttps();
			}
			else if (!TryOpen(httpsPort, "HTTPS", effectiveProbe))
			{
				effective = effective.WithoutHttps();
			}
		}

		if (requested.HttpPort is { } httpPort && !TryOpen(httpPort, "HTTP", effectiveProbe))
		{
			effective = effective.WithoutHttp();
		}

		return new HostListenerPlan(effective, loopbackPort, certificate);
	}

	internal void Apply(KestrelServerOptions options)
	{
		if (Endpoints.HttpPort is { } httpPort)
		{
			options.ListenAnyIP(httpPort);
		}

		if (Endpoints.HttpsPort is { } httpsPort)
		{
			// Whether the listener opens at all is still decided eagerly in Create, so a certificate that
			// cannot be loaded costs the listener rather than every handshake after the port bound. The
			// selector exists only so a renewed certificate replaces a working one without a restart.
			options.ListenAnyIP(httpsPort,
				listen =>
					listen.UseHttps(https => https.ServerCertificateSelector = (_, _) => CertificateHolder.Current));
		}

		options.Listen(IPAddress.Loopback, LoopbackPort);
	}

	private static bool TryOpen(int port, string protocol, Func<int, PublicListenerProbeResult> probe)
	{
		var result = probe(port);
		if (result.CanBind)
		{
			return true;
		}

		Log.Warning("The public {Protocol} listener on port {Port} could not be opened " +
			"({SocketError}, native error {NativeErrorCode}). Macro Deck continues without it; " +
			"choose a different port in Settings > Network.",
			protocol,
			port,
			result.Error,
			result.NativeErrorCode);

		return false;
	}
}
