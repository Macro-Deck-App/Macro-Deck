using System.Security.Cryptography;
using MacroDeckHost.Application.Backups;

namespace MacroDeckHost.Infrastructure.Backups.Crypto;

/// <summary>
/// The recovery key is 256 uniformly random bits, so there is nothing for a password KDF to stretch.
/// HKDF-SHA256 (RFC 5869) is used purely for domain separation between the key-encryption key, the
/// public key fingerprint and the transcription checksum.
/// </summary>
public static class BackupKeyDerivation
{
	public const int RecoveryKeyBytes = 32;
	public const int KeyBytes = 32;
	public const int SaltBytes = 16;
	public const int KeyIdBytes = 16;

	private static ReadOnlySpan<byte> KekInfo => "MacroDeck.Backup.v1.kek"u8;
	private static ReadOnlySpan<byte> KeyIdInfo => "MacroDeck.Backup.v1.keyid"u8;

	public static byte[] DeriveKeyEncryptionKey(ReadOnlySpan<byte> recoveryKey, ReadOnlySpan<byte> salt)
	{
		var key = new byte[KeyBytes];
		HKDF.DeriveKey(HashAlgorithmName.SHA256, recoveryKey, key, salt, KekInfo);

		return key;
	}

	public static string DeriveKeyId(ReadOnlySpan<byte> recoveryKey)
	{
		Span<byte> id = stackalloc byte[KeyIdBytes];
		HKDF.DeriveKey(HashAlgorithmName.SHA256, recoveryKey, id, salt: default, KeyIdInfo);

		return Convert.ToHexStringLower(id);
	}

	public static void DeriveChecksum(ReadOnlySpan<byte> recoveryKey, Span<byte> destination)
		=> BackupRecoveryKeyFormat.DeriveChecksum(recoveryKey, destination);
}
