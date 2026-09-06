using NSec.Cryptography;

namespace MacroDeck.Signing.Keys;

/// <summary>
/// Raw Ed25519 key material lengths, and creation of a new key pair. NSec stays behind this type: every
/// member here works with raw bytes, never <see cref="NSec.Cryptography.Key"/> or
/// <see cref="NSec.Cryptography.PublicKey"/>.
/// </summary>
public static class Ed25519KeyPair
{
	/// <summary>Length of a raw Ed25519 private key.</summary>
	public const int PrivateKeyLength = 32;

	/// <summary>Length of a raw Ed25519 public key.</summary>
	public const int PublicKeyLength = 32;

	/// <summary>Length of a raw Ed25519 signature.</summary>
	public const int SignatureLength = 64;

	/// <summary>Creates a new, random Ed25519 key pair. The caller owns the returned private key bytes and
	/// is responsible for zeroing them with <see cref="System.Security.Cryptography.CryptographicOperations.ZeroMemory"/>
	/// once they are no longer needed.</summary>
	public static (byte[] PrivateKey, byte[] PublicKey) Create()
	{
		using var key = new Key(SignatureAlgorithm.Ed25519,
			new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });

		return (key.Export(KeyBlobFormat.RawPrivateKey), key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
	}
}
