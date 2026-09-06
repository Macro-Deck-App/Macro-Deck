using System.Security.Cryptography;

namespace MacroDeckHost.Infrastructure.Security.KeyRing.Crypto;

public readonly record struct KeyRingSealed(byte[] Nonce, byte[] Ciphertext, byte[] Tag);

/// <summary>
/// AES-256-GCM with the key used directly - both keys sealed here are already 256 uniformly random
/// bits, so a KDF would add nothing. The associated data is what binds a sealed value to the identity
/// it claims, so a blob cannot be moved between key files or escrow entries and still open.
/// </summary>
public static class KeyRingAead
{
	public const int NonceBytes = 12;
	public const int TagBytes = 16;

	public static KeyRingSealed Seal(ReadOnlySpan<byte> key,
		ReadOnlySpan<byte> plaintext,
		ReadOnlySpan<byte> associatedData)
	{
		var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
		var ciphertext = new byte[plaintext.Length];
		var tag = new byte[TagBytes];

		using var aes = new AesGcm(key, TagBytes);
		aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

		return new KeyRingSealed(nonce, ciphertext, tag);
	}

	/// <summary>
	/// Returns null when the tag does not verify. A wrong key, a tampered blob and a mismatched identity
	/// are indistinguishable here by design, and all three mean the same thing to a caller.
	/// </summary>
	public static byte[]? TryOpen(ReadOnlySpan<byte> key, KeyRingSealed sealedValue, ReadOnlySpan<byte> associatedData)
	{
		if (key.Length != KeyRingKeyDerivation.KekBytes ||
			sealedValue.Nonce.Length != NonceBytes ||
			sealedValue.Tag.Length != TagBytes)
		{
			return null;
		}

		var plaintext = new byte[sealedValue.Ciphertext.Length];
		try
		{
			using var aes = new AesGcm(key, TagBytes);
			aes.Decrypt(sealedValue.Nonce, sealedValue.Ciphertext, sealedValue.Tag, plaintext, associatedData);

			return plaintext;
		}
		catch (CryptographicException)
		{
			CryptographicOperations.ZeroMemory(plaintext);

			return null;
		}
	}
}
