using System.Security.Cryptography;
using MacroDeck.Signing.Certificates;
using NSec.Cryptography;

namespace MacroDeck.Signing.Keys;

/// <summary>
/// A private key paired with the certificate that vouches for it, ready to sign. The private key is
/// imported with <see cref="KeyExportPolicies.None"/> - it can sign, but nothing in this process can ever
/// read its raw bytes back out - and is disposed by <see cref="Dispose"/>.
/// </summary>
public sealed class SigningMaterial : IDisposable
{
	private readonly Key _privateKey;
	private readonly Ed25519VerificationKey _certificatePublicKey;

	private SigningMaterial(Key privateKey, Ed25519VerificationKey certificatePublicKey, SigningCertificate certificate)
	{
		_privateKey = privateKey;
		_certificatePublicKey = certificatePublicKey;
		Certificate = certificate;
	}

	/// <summary>The certificate this key was issued under.</summary>
	public SigningCertificate Certificate { get; }

	/// <summary>Signs <paramref name="payload"/> with the imported private key.</summary>
	public byte[] Sign(ReadOnlySpan<byte> payload)
	{
		return SignatureAlgorithm.Ed25519.Sign(_privateKey, payload);
	}

	/// <summary>Verifies <paramref name="signature"/> over <paramref name="payload"/> against this
	/// certificate's own public key. Used to confirm a signature this process just produced is actually
	/// verifiable before it is written anywhere.</summary>
	public bool SelfVerify(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> signature)
	{
		return _certificatePublicKey.Verify(payload, signature);
	}

	/// <inheritdoc />
	public void Dispose()
	{
		_privateKey.Dispose();
	}

	/// <summary>
	/// Imports <paramref name="rawPrivateKey"/> and checks, with
	/// <see cref="CryptographicOperations.FixedTimeEquals"/>, that it belongs to <paramref name="certificate"/>
	/// before pairing the two. <paramref name="certificate"/> should already have passed
	/// <see cref="SigningCertificateChain.Verify(byte[], byte[], string)"/> - this factory trusts the
	/// certificate document it is given and only re-derives the key relationship.
	/// </summary>
	public static SigningMaterialResult Create(ReadOnlySpan<byte> rawPrivateKey, SigningCertificate certificate)
	{
		ArgumentNullException.ThrowIfNull(certificate);

		if (rawPrivateKey.Length != Ed25519KeyPair.PrivateKeyLength)
		{
			return SigningMaterialResult.Fail(SigningError.PrivateKeyMalformed,
				$"The private key must be exactly {Ed25519KeyPair.PrivateKeyLength} raw bytes.");
		}

		var certificatePublicBytes = Base64Material.TryDecode(certificate.PublicKey, Ed25519KeyPair.PublicKeyLength);
		if (certificatePublicBytes is null)
		{
			return SigningMaterialResult.Fail(SigningError.CertificateMalformed,
				"The certificate's public key is not a valid base64-encoded Ed25519 key.");
		}

		Key privateKey;
		try
		{
			privateKey = Key.Import(SignatureAlgorithm.Ed25519,
				rawPrivateKey,
				KeyBlobFormat.RawPrivateKey,
				new KeyCreationParameters { ExportPolicy = KeyExportPolicies.None });
		}
		catch (FormatException ex)
		{
			CryptographicOperations.ZeroMemory(certificatePublicBytes);
			return SigningMaterialResult.Fail(SigningError.PrivateKeyMalformed, ex.Message);
		}

		var actualPublicBytes = privateKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);
		try
		{
			if (!CryptographicOperations.FixedTimeEquals(certificatePublicBytes, actualPublicBytes))
			{
				privateKey.Dispose();
				return SigningMaterialResult.Fail(SigningError.PrivateKeyDoesNotMatchCertificate,
					"The supplied private key does not belong to the certificate.");
			}

			var certificatePublicKey = Ed25519VerificationKey.TryImport(certificatePublicBytes);
			if (certificatePublicKey is null)
			{
				privateKey.Dispose();
				return SigningMaterialResult.Fail(SigningError.CertificateMalformed,
					"The certificate's public key could not be imported.");
			}

			return SigningMaterialResult.Ok(new SigningMaterial(privateKey, certificatePublicKey, certificate));
		}
		finally
		{
			CryptographicOperations.ZeroMemory(certificatePublicBytes);
			CryptographicOperations.ZeroMemory(actualPublicBytes);
		}
	}
}

/// <summary>The outcome of <see cref="SigningMaterial.Create"/>.</summary>
public sealed record SigningMaterialResult
{
	public required bool Success { get; init; }

	public SigningMaterial? Material { get; init; }

	public SigningError? Error { get; init; }

	public string? Message { get; init; }

	public static SigningMaterialResult Ok(SigningMaterial material) => new() { Success = true, Material = material };

	public static SigningMaterialResult Fail(SigningError error, string message) =>
		new() { Success = false, Error = error, Message = message };
}
