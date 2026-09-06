using System.Security.Cryptography.X509Certificates;

namespace MacroDeckHost.Application.Network.Tls;

public enum PublicTlsFailure
{
	None,

	NotConfigured,

	KeyUnreadable,

	CertificateInvalid
}

public readonly record struct PublicTlsCertificateResolution(X509Certificate2? Certificate, PublicTlsFailure Failure);

public readonly record struct PublicTlsAuthorityResolution(GeneratedCertificate? Material, PublicTlsFailure Failure);

public interface IPublicTlsCertificateStore
{
	PublicTlsCertificateInfo? ReadInfo();

	string? ReadCertificatePem();

	PublicTlsCertificateResolution LoadServerCertificate();

	PublicTlsCertificateInfo Save(string certificatePem, string privateKeyPem, PublicTlsCertificateSource source);

	PublicTlsCertificateInfo? ReadAuthorityInfo();

	string? ReadAuthorityCertificatePem();

	PublicTlsAuthorityResolution LoadAuthority();

	PublicTlsCertificateInfo SaveAuthority(string certificatePem, string privateKeyPem);
}
