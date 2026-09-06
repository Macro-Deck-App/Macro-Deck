using System.Security.Cryptography;
using System.Text;

namespace MacroDeckHost.Infrastructure.Migration.MacroDeck2;

/// <summary>
/// Reads the format Macro Deck 2's <c>StringCipher</c> writes: base64 over
/// <c>salt(16) || iv(16) || ciphertext</c>, with the key derived by PBKDF2 over the passphrase and that
/// salt.
/// </summary>
/// <remarks>
/// Every parameter here is dictated by data that already exists on disk and cannot be renegotiated, so
/// none of them may be modernised:
/// <list type="bullet">
/// <item>SHA-1 is the hash <c>Rfc2898DeriveBytes</c> defaulted to in the .NET Framework build that wrote
/// these files. A stronger hash derives a different key and decrypts nothing.</item>
/// <item>1000 iterations is what Macro Deck 2 used. It is low by today's standards, which matters for a
/// passphrase an attacker might guess - it does not matter here, where the passphrase is a machine GUID
/// the user already possesses and the plaintext is being read back by its owner.</item>
/// <item>Macro Deck 2 names its constant <c>Keysize = 128</c> but asks PBKDF2 for <c>Keysize / 4</c> = 32
/// bytes, so the cipher is AES-256 despite the name. Both numbers below are deliberate and must not be
/// "corrected" to agree with each other.</item>
/// </list>
/// </remarks>
internal static class MacroDeck2StringCipher
{
	private const int SaltBytes = 16;

	private const int IvBytes = 16;

	private const int KeyBytes = 32;

	private const int DerivationIterations = 1000;

	/// <summary>
	/// Decrypts one value, or returns null when it cannot be decrypted with this passphrase. A wrong key
	/// is an expected outcome - it is what makes the host offer to ask for another one - so it is never
	/// signalled by an exception.
	/// </summary>
	public static string? TryDecrypt(string? cipherText, string passPhrase)
	{
		if (string.IsNullOrWhiteSpace(cipherText))
		{
			return null;
		}

		byte[] combined;
		try
		{
			combined = Convert.FromBase64String(cipherText);
		}
		catch (FormatException)
		{
			return null;
		}

		if (combined.Length <= SaltBytes + IvBytes)
		{
			return null;
		}

		var salt = combined.AsSpan(0, SaltBytes).ToArray();
		var iv = combined.AsSpan(SaltBytes, IvBytes).ToArray();
		var cipher = combined.AsSpan(SaltBytes + IvBytes).ToArray();

		try
		{
			using var aes = Aes.Create();
			aes.Key = DeriveKey(passPhrase, salt);

			return Encoding.UTF8.GetString(aes.DecryptCbc(cipher, iv, PaddingMode.PKCS7));
		}
		catch (CryptographicException)
		{
			return null;
		}
		catch (ArgumentException)
		{
			return null;
		}
	}

	/// <summary>Writes the same format. Exists so tests can build fixtures; nothing else encrypts here.</summary>
	internal static string Encrypt(string plainText, string passPhrase)
	{
		var salt = RandomNumberGenerator.GetBytes(SaltBytes);
		var iv = RandomNumberGenerator.GetBytes(IvBytes);

		using var aes = Aes.Create();
		aes.Key = DeriveKey(passPhrase, salt);
		var cipher = aes.EncryptCbc(Encoding.UTF8.GetBytes(plainText), iv, PaddingMode.PKCS7);

		return Convert.ToBase64String([.. salt, .. iv, .. cipher]);
	}

#pragma warning disable CA5379 // SHA-1 is fixed by the on-disk format this reads; see the type remarks.
	private static byte[] DeriveKey(string passPhrase, byte[] salt)
		=> Rfc2898DeriveBytes.Pbkdf2(passPhrase, salt, DerivationIterations, HashAlgorithmName.SHA1, KeyBytes);
#pragma warning restore CA5379
}
