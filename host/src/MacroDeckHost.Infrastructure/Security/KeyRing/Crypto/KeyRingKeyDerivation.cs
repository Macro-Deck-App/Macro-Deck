using System.Security.Cryptography;

namespace MacroDeckHost.Infrastructure.Security.KeyRing.Crypto;

/// <summary>
/// Domain separation for the key ring's key-encryption key. The info strings are deliberately distinct
/// from <c>MacroDeck.Backup.v1.*</c>: a backup archive and the live key ring are both derived from the
/// same recovery key, and reusing an info string would make one openable with the other's derived key.
/// </summary>
public static class KeyRingKeyDerivation
{
	public const int KekBytes = 32;
	public const int SaltBytes = 16;
	public const int KekIdBytes = 16;

	private static ReadOnlySpan<byte> KekIdInfo => "MacroDeck.KeyRing.v1.kekid"u8;
	private static ReadOnlySpan<byte> EscrowInfo => "MacroDeck.KeyRing.v1.escrow"u8;

	public static byte[] CreateKek() => RandomNumberGenerator.GetBytes(KekBytes);

	/// <summary>
	/// Names a key-encryption key without revealing it, so a key file can say which key opens it and a
	/// mismatch can be told from corruption before anything is decrypted.
	/// </summary>
	public static string DeriveKekId(ReadOnlySpan<byte> kek)
	{
		Span<byte> id = stackalloc byte[KekIdBytes];
		HKDF.DeriveKey(HashAlgorithmName.SHA256, kek, id, salt: default, KekIdInfo);

		return Convert.ToHexStringLower(id);
	}

	public static byte[] DeriveEscrowKey(ReadOnlySpan<byte> recoveryKey, ReadOnlySpan<byte> salt)
	{
		var key = new byte[KekBytes];
		HKDF.DeriveKey(HashAlgorithmName.SHA256, recoveryKey, key, salt, EscrowInfo);

		return key;
	}
}
