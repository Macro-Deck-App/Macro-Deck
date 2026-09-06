using System.Security.Cryptography.X509Certificates;

namespace MacroDeckHost.Application.Network.Tls;

public interface IPublicTlsCertificateHolder
{
	X509Certificate2? Current { get; }

	void Replace(X509Certificate2 certificate);
}

public sealed class PublicTlsCertificateHolder : IPublicTlsCertificateHolder
{
	private X509Certificate2? _current;

	public PublicTlsCertificateHolder(X509Certificate2? certificate) => _current = certificate;

	public X509Certificate2? Current => Volatile.Read(ref _current);

	// The replaced certificate is deliberately not disposed. Handshakes already in flight still hold it,
	// and a swap happens at most once a year or when the machine's addresses change - leaking one small
	// object then is cheaper than racing Kestrel for the handle.
	public void Replace(X509Certificate2 certificate) => Volatile.Write(ref _current, certificate);
}
