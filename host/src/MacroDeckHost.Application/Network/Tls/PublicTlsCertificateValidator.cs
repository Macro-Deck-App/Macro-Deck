using System.Security.Cryptography;
using MacroDeck.Localization;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Network.Tls;

public sealed record PublicTlsValidationResult(bool Valid, PublicTlsCertificateInfo? Certificate, LocalizedText? Error);

public static class PublicTlsCertificateValidator
{
	private const string EncryptedPrivateKeyLabel = "ENCRYPTED PRIVATE KEY";

	public static PublicTlsValidationResult Validate(
		string? certificatePem,
		string? privateKeyPem,
		PublicTlsCertificateSource source)
	{
		if (string.IsNullOrWhiteSpace(certificatePem))
		{
			return new PublicTlsValidationResult(false, null, AppStrings.Settings.Network.Tls.UploadCertificateRequired());
		}

		if (string.IsNullOrWhiteSpace(privateKeyPem))
		{
			return new PublicTlsValidationResult(false, null, AppStrings.Settings.Network.Tls.UploadPrivateKeyRequired());
		}

		if (privateKeyPem.Contains(EncryptedPrivateKeyLabel, StringComparison.Ordinal))
		{
			return new PublicTlsValidationResult(false,
				null,
				AppStrings.Settings.Network.Tls.UploadPrivateKeyEncrypted());
		}

		// Deliberately the full server-certificate path, not just CreateFromPem: a pair that parses but
		// cannot be loaded as a TLS server certificate would be accepted here and then fail every
		// handshake at the next start, after the port had already bound. CreateFromPem also checks the
		// key matches the certificate's public key, so a mismatched pair fails here too.
		try
		{
			using var certificate = PublicTlsServerCertificate.Create(certificatePem, privateKeyPem);

			var info = new PublicTlsCertificateInfo(certificate.Subject,
				certificate.GetCertHashString(HashAlgorithmName.SHA256),
				certificate.NotBefore,
				certificate.NotAfter,
				source);

			return new PublicTlsValidationResult(true, info, null);
		}
		catch (Exception)
		{
			// Deliberately not ex.Message: CreateFromPem's failure text can echo back fragments of the
			// input it was given, and this validator's confidentiality guarantee is that no rejection
			// message - ever - can carry any part of either PEM. A fixed message is the only way to be
			// sure of that.
			return new PublicTlsValidationResult(false,
				null,
				AppStrings.Settings.Network.Tls.UploadPairInvalid());
		}
	}
}
