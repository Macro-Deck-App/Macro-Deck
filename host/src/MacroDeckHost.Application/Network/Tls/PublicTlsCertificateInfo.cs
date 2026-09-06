namespace MacroDeckHost.Application.Network.Tls;

public enum PublicTlsCertificateSource
{
	SelfSigned,
	Custom,
	LocalCa
}

public sealed record PublicTlsCertificateInfo(
	string Subject,
	string Fingerprint,
	DateTimeOffset NotBefore,
	DateTimeOffset NotAfter,
	PublicTlsCertificateSource Source)
{
	public bool IsExpired(DateTimeOffset now) => now > NotAfter;

	public bool IsNotYetValid(DateTimeOffset now) => now < NotBefore;
}
