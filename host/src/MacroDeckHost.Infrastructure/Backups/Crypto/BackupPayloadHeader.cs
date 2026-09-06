using System.Buffers.Binary;
using System.Security.Cryptography;

namespace MacroDeckHost.Infrastructure.Backups.Crypto;

/// <summary>
/// Fixed-size prologue of <c>payload.enc</c>. The whole header is the associated data of every payload
/// segment, so none of it can be edited without breaking authentication.
/// </summary>
public sealed class BackupPayloadHeader
{
	public const int Version = 1;
	public const int NoncePrefixBytes = 7;
	public const int NonceBytes = 12;
	public const int TagBytes = 16;
	public const int DefaultChunkSizeLog2 = 20;
	public const int MinChunkSizeLog2 = 16;
	public const int MaxChunkSizeLog2 = 24;

	public const int TotalBytes = MagicBytes +
		1 +
		BackupKeyDerivation.SaltBytes +
		NonceBytes +
		BackupKeyDerivation.KeyBytes +
		TagBytes +
		NoncePrefixBytes +
		1;

	private const int MagicBytes = 4;

	private static ReadOnlySpan<byte> Magic => "MDBK"u8;

	public required byte[] Salt { get; init; }

	public required byte[] WrapNonce { get; init; }

	public required byte[] WrappedKey { get; init; }

	public required byte[] WrapTag { get; init; }

	public required byte[] NoncePrefix { get; init; }

	public required int ChunkSizeLog2 { get; init; }

	public int ChunkSize => 1 << ChunkSizeLog2;

	public static BackupPayloadHeader Create()
		=> new()
		{
			Salt = RandomNumberGenerator.GetBytes(BackupKeyDerivation.SaltBytes),
			WrapNonce = RandomNumberGenerator.GetBytes(NonceBytes),
			WrappedKey = new byte[BackupKeyDerivation.KeyBytes],
			WrapTag = new byte[TagBytes],
			NoncePrefix = RandomNumberGenerator.GetBytes(NoncePrefixBytes),
			ChunkSizeLog2 = DefaultChunkSizeLog2
		};

	public static BackupPayloadHeader Parse(ReadOnlySpan<byte> bytes)
	{
		if (bytes.Length != TotalBytes || !bytes[..MagicBytes].SequenceEqual(Magic))
		{
			throw new BackupCryptoException(BackupDecryptResult.Corrupt, "The backup payload header is not valid.");
		}

		var cursor = bytes[MagicBytes..];
		var version = cursor[0];
		if (version != Version)
		{
			throw new BackupCryptoException(BackupDecryptResult.UnsupportedAlgorithm,
				$"Backup payload version {version} is not supported.");
		}

		cursor = cursor[1..];
		var salt = cursor[..BackupKeyDerivation.SaltBytes].ToArray();
		cursor = cursor[BackupKeyDerivation.SaltBytes..];
		var wrapNonce = cursor[..NonceBytes].ToArray();
		cursor = cursor[NonceBytes..];
		var wrappedKey = cursor[..BackupKeyDerivation.KeyBytes].ToArray();
		cursor = cursor[BackupKeyDerivation.KeyBytes..];
		var wrapTag = cursor[..TagBytes].ToArray();
		cursor = cursor[TagBytes..];
		var noncePrefix = cursor[..NoncePrefixBytes].ToArray();
		cursor = cursor[NoncePrefixBytes..];
		var chunkSizeLog2 = cursor[0];

		if (chunkSizeLog2 is < MinChunkSizeLog2 or > MaxChunkSizeLog2)
		{
			throw new BackupCryptoException(BackupDecryptResult.UnsupportedAlgorithm,
				$"Backup payload chunk size 2^{chunkSizeLog2} is out of range.");
		}

		return new BackupPayloadHeader
		{
			Salt = salt,
			WrapNonce = wrapNonce,
			WrappedKey = wrappedKey,
			WrapTag = wrapTag,
			NoncePrefix = noncePrefix,
			ChunkSizeLog2 = chunkSizeLog2
		};
	}

	public byte[] ToArray()
	{
		var bytes = new byte[TotalBytes];
		var cursor = bytes.AsSpan();

		Magic.CopyTo(cursor);
		cursor = cursor[MagicBytes..];
		cursor[0] = Version;
		cursor = cursor[1..];
		Salt.CopyTo(cursor);
		cursor = cursor[BackupKeyDerivation.SaltBytes..];
		WrapNonce.CopyTo(cursor);
		cursor = cursor[NonceBytes..];
		WrappedKey.CopyTo(cursor);
		cursor = cursor[BackupKeyDerivation.KeyBytes..];
		WrapTag.CopyTo(cursor);
		cursor = cursor[TagBytes..];
		NoncePrefix.CopyTo(cursor);
		cursor = cursor[NoncePrefixBytes..];
		cursor[0] = (byte)ChunkSizeLog2;

		return bytes;
	}

	/// <summary>
	/// Tink's AES-GCM-HKDF-STREAMING nonce layout. The segment counter binds ordering and the final-segment
	/// flag is what makes a truncated payload fail instead of decrypting to a shorter backup.
	/// </summary>
	public byte[] SegmentNonce(uint segment, bool isFinal)
	{
		var nonce = new byte[NonceBytes];
		NoncePrefix.CopyTo(nonce, 0);
		BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(NoncePrefixBytes), segment);
		nonce[NonceBytes - 1] = isFinal ? (byte)1 : (byte)0;

		return nonce;
	}
}
