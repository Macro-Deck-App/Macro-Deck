using System.Security.Cryptography;
using MacroDeckHost.Application.Portable;

namespace MacroDeckHost.Infrastructure.Portable;

public enum PortableDecryptResult
{
	Success,
	WrongPassword,
	Corrupt,

	UnsupportedAlgorithm
}

public static class PortableArchiveCrypto
{
	public const int Iterations = 600_000;

	public const int MinIterations = 100_000;

	public const int MaxIterations = 10_000_000;

	private const int SaltBytes = 16;
	private const int NonceBytes = 12;
	private const int TagBytes = 16;
	private const int KeyBytes = 32;
	private const int MagicBytes = 4;
	private const int HeaderBytes = MagicBytes + NonceBytes + KeyBytes + TagBytes + NonceBytes + TagBytes;

	private static ReadOnlySpan<byte> Magic => "MDPE"u8;

	public static PortableEncryptionInfo CreateParameters()
		=> new()
		{
			Cipher = PortableEncryptionInfo.Aes256Gcm,
			Kdf = new PortableKdfInfo
			{
				Id = PortableKdfInfo.Pbkdf2HmacSha256,
				Iterations = Iterations,
				Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SaltBytes))
			}
		};

	public static byte[] Encrypt(byte[] plaintext,
		string password,
		PortableEncryptionInfo info,
		ReadOnlySpan<byte> associatedData)
	{
		if (!TryReadParameters(info, out var salt, out var iterations))
		{
			throw new ArgumentException("Unsupported encryption parameters", nameof(info));
		}

		var kek = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeyBytes);
		var dek = RandomNumberGenerator.GetBytes(KeyBytes);
		try
		{
			var payload = new byte[HeaderBytes + plaintext.Length];
			var cursor = payload.AsSpan();

			Magic.CopyTo(cursor);
			cursor = cursor[MagicBytes..];

			var wrapNonce = cursor[..NonceBytes];
			RandomNumberGenerator.Fill(wrapNonce);
			cursor = cursor[NonceBytes..];
			var wrappedKey = cursor[..KeyBytes];
			cursor = cursor[KeyBytes..];
			var wrapTag = cursor[..TagBytes];
			cursor = cursor[TagBytes..];
			using (var kekCipher = new AesGcm(kek, TagBytes))
			{
				kekCipher.Encrypt(wrapNonce, dek, wrappedKey, wrapTag, associatedData);
			}

			var nonce = cursor[..NonceBytes];
			RandomNumberGenerator.Fill(nonce);
			cursor = cursor[NonceBytes..];
			var tag = cursor[..TagBytes];
			cursor = cursor[TagBytes..];
			using (var dekCipher = new AesGcm(dek, TagBytes))
			{
				dekCipher.Encrypt(nonce, plaintext, cursor, tag);
			}

			return payload;
		}
		finally
		{
			CryptographicOperations.ZeroMemory(kek);
			CryptographicOperations.ZeroMemory(dek);
		}
	}

	public static PortableDecryptResult TryDecrypt(byte[] payload,
		PortableEncryptionInfo info,
		ReadOnlySpan<byte> associatedData,
		string password,
		out byte[] plaintext)
	{
		plaintext = [];

		if (payload.Length < HeaderBytes || !payload.AsSpan(0, MagicBytes).SequenceEqual(Magic))
		{
			return PortableDecryptResult.Corrupt;
		}

		if (!TryReadParameters(info, out var salt, out var iterations))
		{
			return IsKnownAlgorithm(info) ? PortableDecryptResult.Corrupt : PortableDecryptResult.UnsupportedAlgorithm;
		}

		var kek = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeyBytes);
		var dek = new byte[KeyBytes];
		try
		{
			var cursor = payload.AsSpan(MagicBytes);
			var wrapNonce = cursor[..NonceBytes];
			cursor = cursor[NonceBytes..];
			var wrappedKey = cursor[..KeyBytes];
			cursor = cursor[KeyBytes..];
			var wrapTag = cursor[..TagBytes];
			cursor = cursor[TagBytes..];

			try
			{
				using var kekCipher = new AesGcm(kek, TagBytes);
				kekCipher.Decrypt(wrapNonce, wrappedKey, wrapTag, dek, associatedData);
			}
			catch (CryptographicException)
			{
				// The wrap covers the manifest too, so an edited manifest reports as a wrong password. That is
				// the honest answer to the user: with the password alone we cannot tell the two apart.
				return PortableDecryptResult.WrongPassword;
			}

			var nonce = cursor[..NonceBytes];
			cursor = cursor[NonceBytes..];
			var tag = cursor[..TagBytes];
			cursor = cursor[TagBytes..];

			var buffer = new byte[cursor.Length];
			try
			{
				using var dekCipher = new AesGcm(dek, TagBytes);
				dekCipher.Decrypt(nonce, cursor, tag, buffer);
			}
			catch (CryptographicException)
			{
				CryptographicOperations.ZeroMemory(buffer);
				return PortableDecryptResult.Corrupt;
			}

			plaintext = buffer;
			return PortableDecryptResult.Success;
		}
		finally
		{
			CryptographicOperations.ZeroMemory(kek);
			CryptographicOperations.ZeroMemory(dek);
		}
	}

	private static bool IsKnownAlgorithm(PortableEncryptionInfo info)
		=> info.Cipher == PortableEncryptionInfo.Aes256Gcm && info.Kdf?.Id == PortableKdfInfo.Pbkdf2HmacSha256;

	private static bool TryReadParameters(PortableEncryptionInfo info, out byte[] salt, out int iterations)
	{
		salt = [];
		iterations = 0;

		if (!IsKnownAlgorithm(info) || info.Kdf is null)
		{
			return false;
		}

		if (info.Kdf.Iterations < MinIterations || info.Kdf.Iterations > MaxIterations)
		{
			return false;
		}

		try
		{
			salt = Convert.FromBase64String(info.Kdf.Salt ?? string.Empty);
		}
		catch (FormatException)
		{
			return false;
		}

		if (salt.Length != SaltBytes)
		{
			return false;
		}

		iterations = info.Kdf.Iterations;
		return true;
	}
}
