using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using MacroDeckHost.Application.Network.Tls;
using MacroDeckHost.Application.Paths;
using Microsoft.AspNetCore.DataProtection;
using Serilog;

namespace MacroDeckHost.Infrastructure.Network.Tls;

public sealed class FilePublicTlsCertificateStore : IPublicTlsCertificateStore
{
	private const string CertificateFileName = "public-tls.crt.pem";
	private const string KeyFileName = "public-tls.key";
	private const string SourceFileName = "public-tls.source";

	// The dotted prefix is load bearing: BackupComponentGroups matches "keys/public-tls." so the
	// authority travels with a backup. A dash instead of the dot would silently leave it behind, and a
	// restore would hand back a host certificate nothing can verify.
	private const string AuthorityCertificateFileName = "public-tls.ca.crt.pem";
	private const string AuthorityKeyFileName = "public-tls.ca.key";

	private readonly IDataProtector _protector;
	private readonly IDataProtector _authorityProtector;
	private readonly string _certificateFilePath;
	private readonly string _keyFilePath;
	private readonly string _sourceFilePath;
	private readonly string _authorityCertificateFilePath;
	private readonly string _authorityKeyFilePath;
	private readonly ILogger _logger;

	public FilePublicTlsCertificateStore(
		IDataProtectionProvider dataProtectionProvider,
		IMacroDeckPaths paths,
		ILogger logger)
	{
		_protector = dataProtectionProvider.CreateProtector("MacroDeck.Network.PublicTlsKey");
		_authorityProtector = dataProtectionProvider.CreateProtector("MacroDeck.Network.PublicTlsCaKey");
		_certificateFilePath = Path.Combine(paths.KeysDirectory, CertificateFileName);
		_keyFilePath = Path.Combine(paths.KeysDirectory, KeyFileName);
		_sourceFilePath = Path.Combine(paths.KeysDirectory, SourceFileName);
		_authorityCertificateFilePath = Path.Combine(paths.KeysDirectory, AuthorityCertificateFileName);
		_authorityKeyFilePath = Path.Combine(paths.KeysDirectory, AuthorityKeyFileName);
		_logger = logger;
	}

	public PublicTlsCertificateInfo? ReadInfo()
	{
		if (!File.Exists(_certificateFilePath) || !File.Exists(_keyFilePath))
		{
			return null;
		}

		return ReadCertificateInfo(_certificateFilePath, ReadSource());
	}

	public string? ReadCertificatePem() => ReadPemIfPresent(_certificateFilePath);

	public PublicTlsCertificateResolution LoadServerCertificate()
	{
		if (!File.Exists(_certificateFilePath) || !File.Exists(_keyFilePath))
		{
			return new PublicTlsCertificateResolution(null, PublicTlsFailure.NotConfigured);
		}

		string keyPem;
		try
		{
			keyPem = Encoding.UTF8.GetString(_protector.Unprotect(File.ReadAllBytes(_keyFilePath)));
		}
		catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
		{
			// Deliberately does not regenerate anything here, unlike FileSigningKeyProvider's
			// LoadOrCreateKey. A signing key can be replaced for free - the only cost is that
			// existing sessions are invalidated. A certificate cannot: silently minting a new one
			// would change the identity that every device which already trusts this host's
			// certificate is relying on, and nothing would tell the user that happened until TLS
			// handshakes start failing everywhere. Report the failure and leave the stored files
			// exactly as they are so a working backup restore or a manual fix still has something
			// to work with.
			_logger.Warning(ex, "The stored public TLS private key could not be unprotected");
			return new PublicTlsCertificateResolution(null, PublicTlsFailure.KeyUnreadable);
		}

		try
		{
			var certificatePem = File.ReadAllText(_certificateFilePath);
			var serverCertificate = PublicTlsServerCertificate.Create(certificatePem, keyPem);

			_logger.Information(
				"Loaded public TLS certificate {Subject} ({Fingerprint}), valid {NotBefore:u} to {NotAfter:u}",
				serverCertificate.Subject,
				serverCertificate.GetCertHashString(HashAlgorithmName.SHA256),
				serverCertificate.NotBefore,
				serverCertificate.NotAfter);

			return new PublicTlsCertificateResolution(serverCertificate, PublicTlsFailure.None);
		}
		catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
		{
			_logger.Warning(ex, "The stored public TLS certificate material could not be loaded");
			return new PublicTlsCertificateResolution(null, PublicTlsFailure.CertificateInvalid);
		}
	}

	public PublicTlsCertificateInfo Save(string certificatePem, string privateKeyPem, PublicTlsCertificateSource source)
	{
		var result = PublicTlsCertificateValidator.Validate(certificatePem, privateKeyPem, source);
		if (!result.Valid || result.Certificate is null)
		{
			throw new ArgumentException(result.Error ?? "The certificate/key pair is invalid.");
		}

		Directory.CreateDirectory(Path.GetDirectoryName(_keyFilePath)!);

		WriteAtomically(_keyFilePath, _protector.Protect(Encoding.UTF8.GetBytes(privateKeyPem)));
		WriteAtomically(_certificateFilePath, Encoding.UTF8.GetBytes(certificatePem));
		WriteAtomically(_sourceFilePath, Encoding.UTF8.GetBytes(source.ToString()));

		_logger.Information(
			"Stored public TLS certificate {Subject} ({Fingerprint}), valid {NotBefore:u} to {NotAfter:u}",
			result.Certificate.Subject,
			result.Certificate.Fingerprint,
			result.Certificate.NotBefore,
			result.Certificate.NotAfter);

		return result.Certificate;
	}

	public PublicTlsCertificateInfo? ReadAuthorityInfo()
	{
		if (!File.Exists(_authorityCertificateFilePath) || !File.Exists(_authorityKeyFilePath))
		{
			return null;
		}

		return ReadCertificateInfo(_authorityCertificateFilePath, PublicTlsCertificateSource.LocalCa);
	}

	// Reads only the public half, so the certificate a device has to install stays available even when
	// the key ring cannot open the authority's private key.
	public string? ReadAuthorityCertificatePem() => ReadPemIfPresent(_authorityCertificateFilePath);

	public PublicTlsAuthorityResolution LoadAuthority()
	{
		if (!File.Exists(_authorityCertificateFilePath) || !File.Exists(_authorityKeyFilePath))
		{
			return new PublicTlsAuthorityResolution(null, PublicTlsFailure.NotConfigured);
		}

		try
		{
			var keyPem = Encoding.UTF8.GetString(
				_authorityProtector.Unprotect(File.ReadAllBytes(_authorityKeyFilePath)));
			var certificatePem = File.ReadAllText(_authorityCertificateFilePath);

			return new PublicTlsAuthorityResolution(new GeneratedCertificate(certificatePem, keyPem),
				PublicTlsFailure.None);
		}
		catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
		{
			_logger.Warning(ex, "The stored local certificate authority key could not be unprotected");
			return new PublicTlsAuthorityResolution(null, PublicTlsFailure.KeyUnreadable);
		}
	}

	public PublicTlsCertificateInfo SaveAuthority(string certificatePem, string privateKeyPem)
	{
		PublicTlsCertificateInfo info;
		try
		{
			using var certificate = X509Certificate2.CreateFromPem(certificatePem, privateKeyPem);
			info = ToInfo(certificate, PublicTlsCertificateSource.LocalCa);
		}
		catch (Exception)
		{
			// Fixed text for the same reason PublicTlsCertificateValidator uses one: a parser's message
			// can echo back fragments of the key it was given.
			throw new ArgumentException("The certificate authority material could not be loaded.");
		}

		Directory.CreateDirectory(Path.GetDirectoryName(_authorityKeyFilePath)!);

		WriteAtomically(_authorityKeyFilePath, _authorityProtector.Protect(Encoding.UTF8.GetBytes(privateKeyPem)));
		WriteAtomically(_authorityCertificateFilePath, Encoding.UTF8.GetBytes(certificatePem));

		_logger.Information(
			"Stored local certificate authority {Subject} ({Fingerprint}), valid {NotBefore:u} to {NotAfter:u}",
			info.Subject,
			info.Fingerprint,
			info.NotBefore,
			info.NotAfter);

		return info;
	}

	private PublicTlsCertificateInfo? ReadCertificateInfo(string path, PublicTlsCertificateSource source)
	{
		try
		{
			using var certificate = X509Certificate2.CreateFromPem(File.ReadAllText(path));
			return ToInfo(certificate, source);
		}
		catch (CryptographicException ex)
		{
			_logger.Warning(ex, "The stored certificate at {Path} could not be read", path);
			return null;
		}
	}

	private static PublicTlsCertificateInfo ToInfo(X509Certificate2 certificate, PublicTlsCertificateSource source)
		=> new(certificate.Subject,
			certificate.GetCertHashString(HashAlgorithmName.SHA256),
			certificate.NotBefore,
			certificate.NotAfter,
			source);

	private static string? ReadPemIfPresent(string path)
	{
		try
		{
			return File.Exists(path) ? File.ReadAllText(path) : null;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private PublicTlsCertificateSource ReadSource()
	{
		if (File.Exists(_sourceFilePath) &&
			Enum.TryParse<PublicTlsCertificateSource>(File.ReadAllText(_sourceFilePath).Trim(), out var source))
		{
			return source;
		}

		// An unreadable marker resolves to Custom on purpose: the bootstrap planner never replaces custom
		// material, so a lost marker leaves a certificate alone rather than overwriting one the user
		// supplied.
		return PublicTlsCertificateSource.Custom;
	}

	private static void WriteAtomically(string path, byte[] content)
	{
		var tempPath = $"{path}.tmp";
		File.WriteAllBytes(tempPath, content);
		File.Move(tempPath, path, overwrite: true);
	}
}
