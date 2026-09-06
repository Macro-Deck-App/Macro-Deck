using System.Security.Cryptography;

namespace MacroDeckHost.Infrastructure.Backups.Crypto;

/// <summary>
/// Wraps a per-archive data key under a key derived from the recovery key, using the serialized manifest
/// as associated data. Editing any manifest field - including the encryption version - therefore breaks
/// the unwrap, so an archive cannot be downgraded to a weaker format it was not written in.
/// </summary>
public static class BackupPayloadCrypto
{
	public static BackupEncryptingStream CreateEncryptor(Stream destination,
		ReadOnlySpan<byte> recoveryKey,
		ReadOnlySpan<byte> manifestBytes,
		bool leaveOpen = false)
	{
		var header = BackupPayloadHeader.Create();
		var kek = BackupKeyDerivation.DeriveKeyEncryptionKey(recoveryKey, header.Salt);
		var dek = RandomNumberGenerator.GetBytes(BackupKeyDerivation.KeyBytes);

		try
		{
			using (var wrap = new AesGcm(kek, BackupPayloadHeader.TagBytes))
			{
				wrap.Encrypt(header.WrapNonce, dek, header.WrappedKey, header.WrapTag, manifestBytes);
			}

			var headerBytes = header.ToArray();
			destination.Write(headerBytes);

			return new BackupEncryptingStream(destination, header, headerBytes, dek, leaveOpen);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(kek);
			CryptographicOperations.ZeroMemory(dek);
		}
	}

	public static BackupDecryptingStream CreateDecryptor(Stream source,
		ReadOnlySpan<byte> recoveryKey,
		ReadOnlySpan<byte> manifestBytes,
		bool leaveOpen = false)
	{
		var headerBytes = new byte[BackupPayloadHeader.TotalBytes];
		if (!TryReadExactly(source, headerBytes))
		{
			throw new BackupCryptoException(BackupDecryptResult.Corrupt, "The backup payload is too short.");
		}

		var header = BackupPayloadHeader.Parse(headerBytes);
		var kek = BackupKeyDerivation.DeriveKeyEncryptionKey(recoveryKey, header.Salt);
		var dek = new byte[BackupKeyDerivation.KeyBytes];

		try
		{
			try
			{
				using var wrap = new AesGcm(kek, BackupPayloadHeader.TagBytes);
				wrap.Decrypt(header.WrapNonce, header.WrappedKey, header.WrapTag, dek, manifestBytes);
			}
			catch (CryptographicException)
			{
				throw new BackupCryptoException(BackupDecryptResult.WrongKey,
					"The backup could not be opened with this recovery key.");
			}

			return new BackupDecryptingStream(source, header, headerBytes, dek, leaveOpen);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(kek);
			CryptographicOperations.ZeroMemory(dek);
		}
	}

	private static bool TryReadExactly(Stream source, Span<byte> destination)
	{
		var total = 0;
		while (total < destination.Length)
		{
			var read = source.Read(destination[total..]);
			if (read == 0)
			{
				return false;
			}

			total += read;
		}

		return true;
	}
}
