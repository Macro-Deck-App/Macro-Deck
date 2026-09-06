using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MacroDeckHost.Application.Network.Tls;

public readonly record struct GeneratedCertificate(string CertificatePem, string PrivateKeyPem);

public static class LocalCertificateAuthority
{
	private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
	private const int KeySizeBits = 2048;

	public static TimeSpan AuthorityLifetime { get; } = TimeSpan.FromDays(3650);

	public static TimeSpan HostCertificateLifetime { get; } = TimeSpan.FromDays(365);

	public static TimeSpan HostCertificateRenewalWindow { get; } = TimeSpan.FromDays(30);

	public static GeneratedCertificate CreateAuthority(string machineName, DateTimeOffset now)
	{
		using var rsa = RSA.Create(KeySizeBits);
		var request = new CertificateRequest(BuildAuthoritySubject(machineName),
			rsa,
			HashAlgorithmName.SHA256,
			RSASignaturePadding.Pkcs1);

		request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true,
			hasPathLengthConstraint: true,
			pathLengthConstraint: 0,
			critical: true));
		request.CertificateExtensions.Add(new X509KeyUsageExtension(
			X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
			critical: true));
		request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

		// Deliberately no extended key usage on the root. An EKU here switches chain building into
		// EKU-nesting mode, which is well defined on Windows but inconsistent for a manually installed
		// root on iOS - the platform this authority exists to satisfy. The root's reach is bounded by
		// the path length constraint above instead.
		using var certificate = request.CreateSelfSigned(now.AddDays(-1), now.Add(AuthorityLifetime));

		return new GeneratedCertificate(certificate.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem());
	}

	public static GeneratedCertificate IssueHostCertificate(GeneratedCertificate authority,
		IReadOnlyList<IPAddress> addresses,
		IReadOnlyList<string> hostNames,
		DateTimeOffset now)
	{
		using var authorityCertificate = X509Certificate2.CreateFromPem(authority.CertificatePem);
		using var authorityKey = RSA.Create();
		authorityKey.ImportFromPem(authority.PrivateKeyPem);

		using var rsa = RSA.Create(KeySizeBits);
		var request = new CertificateRequest(BuildHostSubject(),
			rsa,
			HashAlgorithmName.SHA256,
			RSASignaturePadding.Pkcs1);

		request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: false,
			hasPathLengthConstraint: false,
			pathLengthConstraint: 0,
			critical: true));
		request.CertificateExtensions.Add(new X509KeyUsageExtension(
			X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
			critical: true));
		request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(ServerAuthenticationOid)],
			critical: false));
		request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));
		request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
			authorityCertificate,
			includeKeyIdentifier: true,
			includeIssuerAndSerial: false));
		request.CertificateExtensions.Add(BuildSubjectAlternativeNames(addresses, hostNames));

		var notAfter = now.Add(HostCertificateLifetime);
		var authorityNotAfter = new DateTimeOffset(authorityCertificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
		if (notAfter > authorityNotAfter)
		{
			notAfter = authorityNotAfter;
		}

		// Signs through the raw key rather than the CertificateRequest.Create(X509Certificate2, ...)
		// overload, which requires the issuer to carry a usable platform key handle - the same key
		// storage fragility PublicTlsServerCertificate already documents for macOS and Linux.
		using var certificate = request.Create(authorityCertificate.SubjectName,
			X509SignatureGenerator.CreateForRSA(authorityKey, RSASignaturePadding.Pkcs1),
			now.AddDays(-1),
			notAfter,
			CreateSerialNumber());

		return new GeneratedCertificate(certificate.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem());
	}

	public static bool IsIssuedBy(X509Certificate2 certificate, X509Certificate2 authority)
	{
		using var chain = new X509Chain();
		chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
		chain.ChainPolicy.CustomTrustStore.Add(authority);
		chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

		// Ignoring the validity window is what keeps an expired but correctly issued certificate from
		// reading as "not ours". Without it the bootstrap planner would treat it as a legacy self-signed
		// certificate and mint a second authority, silently invalidating the trust every paired device
		// already granted.
		chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreNotTimeValid |
			X509VerificationFlags.IgnoreCtlNotTimeValid;

		return chain.Build(certificate) &&
			chain.ChainElements.Count > 0 &&
			string.Equals(chain.ChainElements[^1].Certificate.Thumbprint,
				authority.Thumbprint,
				StringComparison.OrdinalIgnoreCase);
	}

	public static IReadOnlyList<IPAddress> FindUncoveredAddresses(X509Certificate2 certificate,
		IReadOnlyList<IPAddress> addresses)
	{
		var covered = ReadSubjectAlternativeIpAddresses(certificate);

		return addresses.Where(address => !covered.Contains(address)).ToList();
	}

	public static IReadOnlyList<string> ReadSubjectAlternativeNames(X509Certificate2 certificate)
	{
		var extension = FindSubjectAlternativeNameExtension(certificate);
		if (extension is null)
		{
			return [];
		}

		return
		[
			.. extension.EnumerateDnsNames(),
			.. extension.EnumerateIPAddresses().Select(address => address.ToString())
		];
	}

	private static HashSet<IPAddress> ReadSubjectAlternativeIpAddresses(X509Certificate2 certificate)
	{
		var extension = FindSubjectAlternativeNameExtension(certificate);

		return extension is null ? [] : [.. extension.EnumerateIPAddresses()];
	}

	private static X509SubjectAlternativeNameExtension? FindSubjectAlternativeNameExtension(
		X509Certificate2 certificate)
	{
		var raw = certificate.Extensions.FirstOrDefault(extension => extension.Oid?.Value == "2.5.29.17");

		try
		{
			return raw is null ? null : new X509SubjectAlternativeNameExtension(raw.RawData, raw.Critical);
		}
		catch (CryptographicException)
		{
			return null;
		}
	}

	private static X500DistinguishedName BuildAuthoritySubject(string machineName)
	{
		var builder = new X500DistinguishedNameBuilder();
		builder.AddOrganizationName("Macro Deck");

		// The machine name is carried so two Macro Deck installations on one network are distinguishable
		// in a device's trust store. It is escaped by the builder rather than concatenated into a DN.
		builder.AddCommonName(string.IsNullOrWhiteSpace(machineName)
			? "Macro Deck Local CA"
			: $"Macro Deck Local CA ({machineName.Trim()})");

		return builder.Build();
	}

	private static X500DistinguishedName BuildHostSubject()
	{
		var builder = new X500DistinguishedNameBuilder();
		builder.AddOrganizationName("Macro Deck");
		builder.AddCommonName("Macro Deck Host");

		return builder.Build();
	}

	private static X509Extension BuildSubjectAlternativeNames(IReadOnlyList<IPAddress> addresses,
		IReadOnlyList<string> hostNames)
	{
		var builder = new SubjectAlternativeNameBuilder();
		var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var seenAddresses = new HashSet<IPAddress>();

		AddDnsName(builder, seenNames, "localhost");
		AddIpAddress(builder, seenAddresses, IPAddress.Loopback);
		AddIpAddress(builder, seenAddresses, IPAddress.IPv6Loopback);

		foreach (var hostName in hostNames)
		{
			AddDnsName(builder, seenNames, hostName);
		}

		foreach (var address in addresses)
		{
			AddIpAddress(builder, seenAddresses, address);
		}

		return builder.Build();
	}

	private static void AddDnsName(SubjectAlternativeNameBuilder builder, HashSet<string> seen, string name)
	{
		if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
		{
			builder.AddDnsName(name);
		}
	}

	private static void AddIpAddress(SubjectAlternativeNameBuilder builder,
		HashSet<IPAddress> seen,
		IPAddress address)
	{
		if (seen.Add(address))
		{
			builder.AddIpAddress(address);
		}
	}

	private static byte[] CreateSerialNumber()
	{
		var serial = RandomNumberGenerator.GetBytes(16);

		// A serial number is a signed integer on the wire: clearing the top bit keeps it positive, and a
		// leading zero byte would encode a shorter value than the 16 bytes callers expect.
		serial[0] &= 0x7F;
		if (serial[0] == 0)
		{
			serial[0] = 1;
		}

		return serial;
	}
}
