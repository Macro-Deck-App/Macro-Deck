using MacroDeckHost.Application.Network.Tls;

namespace MacroDeckHost.Application.Configuration;

public interface IHostListenerState
{
	PublicEndpointSet PublicEndpoints { get; }

	int PublicPort { get; }

	bool PublicPortOverriddenByEnvironment { get; }

	int? RefusedConfiguredPublicPort { get; }

	int? LoopbackPort { get; }

	bool PublicListenerAvailable { get; }

	PublicTlsFailure TlsFailure { get; }

	PublicTlsRejection TlsRejection { get; }

	string? ActiveCertificateFingerprint { get; }

	void SetLoopbackPort(int port);
}

public sealed class HostListenerState : IHostListenerState
{
	private int _loopbackPort;

	public HostListenerState(PublicEndpointSet publicEndpoints,
		bool publicPortOverriddenByEnvironment,
		int? refusedConfiguredPublicPort = null,
		PublicTlsFailure tlsFailure = PublicTlsFailure.None,
		PublicTlsRejection tlsRejection = PublicTlsRejection.None,
		string? activeCertificateFingerprint = null)
	{
		PublicEndpoints = publicEndpoints;
		PublicPortOverriddenByEnvironment = publicPortOverriddenByEnvironment;
		RefusedConfiguredPublicPort = refusedConfiguredPublicPort;
		TlsFailure = tlsFailure;
		TlsRejection = tlsRejection;
		ActiveCertificateFingerprint = activeCertificateFingerprint;
	}

	public PublicEndpointSet PublicEndpoints { get; }

	public int PublicPort => PublicEndpoints.PublicPort;

	public bool PublicPortOverriddenByEnvironment { get; }

	public int? RefusedConfiguredPublicPort { get; }

	public int? LoopbackPort => Volatile.Read(ref _loopbackPort) is var port and > 0 ? port : null;

	public bool PublicListenerAvailable => PublicEndpoints.HasPublicListener;

	public PublicTlsFailure TlsFailure { get; }

	public PublicTlsRejection TlsRejection { get; }

	public string? ActiveCertificateFingerprint { get; }

	public void SetLoopbackPort(int port) => Volatile.Write(ref _loopbackPort, port);
}
