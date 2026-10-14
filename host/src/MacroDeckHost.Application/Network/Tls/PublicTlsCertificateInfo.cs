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
	public bool IsExpired(DateTimeOffset now)
	{
		return now > NotAfter;
	}

	public bool IsNotYetValid(DateTimeOffset now)
	{
		return now < NotBefore;
	}
}
