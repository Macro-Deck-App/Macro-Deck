using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SuchByte.MacroDeck.Utils;

public static class SelfSignedCertificateGenerator
{
	public static (string CertPem, string KeyPem) Generate()
	{
		using var rsa = RSA.Create(2048);

		var req = new CertificateRequest("CN=Macro Deck Self-Signed",
			rsa,
			HashAlgorithmName.SHA256,
			RSASignaturePadding.Pkcs1);

		// Required by SChannel for TLS server use
		req.CertificateExtensions.Add(new X509KeyUsageExtension(
			X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
			true));

		req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
			new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // serverAuth
			false));

		req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,
			false,
			0,
			true));

		req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

		var san = new SubjectAlternativeNameBuilder();
		san.AddDnsName("localhost");
		san.AddIpAddress(IPAddress.Loopback);
		san.AddIpAddress(IPAddress.IPv6Loopback);

		foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
		{
			if (iface.OperationalStatus != OperationalStatus.Up)
			{
				continue;
			}

			foreach (var addr in iface.GetIPProperties().UnicastAddresses)
			{
				if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
				{
					san.AddIpAddress(addr.Address);
				}
			}
		}

		req.CertificateExtensions.Add(san.Build());

		var cert = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(10));
		var certPem = cert.ExportCertificatePem();
		var keyPem = rsa.ExportPkcs8PrivateKeyPem();

		return (certPem, keyPem);
	}
}
