using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace MacroDeckHost.Application.Network.Tls;

public static class PublicTlsServerCertificate
{
	// PersistKeySet is what Windows needs to hand the key to SslStream, but on macOS and Linux it
	// means the key pair is imported into the user's default keychain and stays there after Dispose -
	// one permanent entry per load, and this path runs on every validation and every start. Everywhere
	// but Windows the default flags use a temporary store that is cleaned up with the certificate.
	public static X509KeyStorageFlags KeyStorageFlags { get; } =
		RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
			? X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet
			: X509KeyStorageFlags.Exportable;

	public static X509Certificate2 Create(string certificatePem, string privateKeyPem)
	{
		using var certificate = X509Certificate2.CreateFromPem(certificatePem, privateKeyPem);

		var pkcs12 = certificate.Export(X509ContentType.Pkcs12);

		return X509CertificateLoader.LoadPkcs12(pkcs12, password: null, KeyStorageFlags);
	}
}
