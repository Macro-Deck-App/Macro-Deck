using System.Security.Cryptography;
using System.Text;

namespace MacroDeckHost.Application.Backups;

public enum BackupRecoveryKeyParseError
{
	None,
	Empty,
	Malformed,
	WrongLength
}

public static class BackupRecoveryKeyFormat
{
	public const int KeyBytes = 32;
	public const int ChecksumBytes = 2;

	private const string Prefix = "MDBK1";
	private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
	private const int GroupSize = 5;
	private const int PayloadBytes = KeyBytes + ChecksumBytes;
	private const int EncodedCharCount = (PayloadBytes * 8 + 4) / 5;

	private static ReadOnlySpan<byte> ChecksumInfo => "MacroDeck.Backup.v1.checksum"u8;

	/// <summary>
	/// Derives the transcription checksum. Lives here rather than beside the other backup derivations so
	/// that parsing a typed recovery key needs nothing but this class - the key ring unlock path runs
	/// when the database and every protector are unreadable.
	/// </summary>
	public static void DeriveChecksum(ReadOnlySpan<byte> recoveryKey, Span<byte> destination)
		=> HKDF.DeriveKey(HashAlgorithmName.SHA256, recoveryKey, destination, salt: default, ChecksumInfo);

	/// <summary>
	/// Parses an exported recovery key and verifies its checksum. Pure: no repository, no protector.
	/// </summary>
	public static bool TryParseExported(string? text, out byte[] key)
	{
		var candidate = new byte[KeyBytes];
		Span<byte> checksum = stackalloc byte[ChecksumBytes];

		if (!TryParse(text, candidate, checksum, out _))
		{
			key = [];

			return false;
		}

		Span<byte> expected = stackalloc byte[ChecksumBytes];
		DeriveChecksum(candidate, expected);

		if (!checksum.SequenceEqual(expected))
		{
			key = [];

			return false;
		}

		key = candidate;

		return true;
	}

	public static string Format(ReadOnlySpan<byte> key, ReadOnlySpan<byte> checksum)
	{
		if (key.Length != KeyBytes)
		{
			throw new ArgumentException($"Recovery key must be {KeyBytes} bytes.", nameof(key));
		}

		if (checksum.Length != ChecksumBytes)
		{
			throw new ArgumentException($"Checksum must be {ChecksumBytes} bytes.", nameof(checksum));
		}

		Span<byte> payload = stackalloc byte[PayloadBytes];
		key.CopyTo(payload);
		checksum.CopyTo(payload[KeyBytes..]);

		Span<char> encoded = stackalloc char[EncodedCharCount];
		Encode(payload, encoded);

		var builder = new StringBuilder(Prefix.Length + EncodedCharCount + EncodedCharCount / GroupSize + 1);
		builder.Append(Prefix);

		for (var i = 0; i < encoded.Length; i += GroupSize)
		{
			builder.Append('-');
			builder.Append(encoded[i..Math.Min(i + GroupSize, encoded.Length)]);
		}

		return builder.ToString();
	}

	// The checksum only guards against transcription errors (typos, dropped or swapped characters); it is
	// not a MAC, so tampering must still be caught by the archive's AEAD tag, not by this check.
	public static bool TryParse(string? text,
		Span<byte> key,
		Span<byte> checksum,
		out BackupRecoveryKeyParseError error)
	{
		if (key.Length != KeyBytes || checksum.Length != ChecksumBytes)
		{
			throw new ArgumentException("Destination buffers have the wrong length.");
		}

		if (string.IsNullOrWhiteSpace(text))
		{
			error = BackupRecoveryKeyParseError.Empty;
			return false;
		}

		var normalized = text.Length <= 512 ? stackalloc char[text.Length] : new char[text.Length];
		var normalizedLength = Normalize(text, normalized);

		if (normalizedLength == 0)
		{
			error = BackupRecoveryKeyParseError.Malformed;
			return false;
		}

		var payload = normalized[..normalizedLength];

		// Length-gated so a key whose own encoding happens to start with the prefix characters is not
		// truncated into an unparseable one.
		if (payload.Length == Prefix.Length + EncodedCharCount && payload[..Prefix.Length].SequenceEqual(Prefix))
		{
			payload = payload[Prefix.Length..];
		}

		if (payload.Length != EncodedCharCount)
		{
			error = BackupRecoveryKeyParseError.WrongLength;
			return false;
		}

		Span<byte> decoded = stackalloc byte[PayloadBytes];
		if (!TryDecode(payload, decoded))
		{
			error = BackupRecoveryKeyParseError.Malformed;
			return false;
		}

		decoded[..KeyBytes].CopyTo(key);
		decoded[KeyBytes..].CopyTo(checksum);
		error = BackupRecoveryKeyParseError.None;

		return true;
	}

	private static int Normalize(ReadOnlySpan<char> text, Span<char> destination)
	{
		var length = 0;

		foreach (var ch in text)
		{
			var mapped = char.ToUpperInvariant(ch) switch
			{
				'O' => '0',
				'I' or 'L' => '1',
				var upper => upper
			};

			if (Alphabet.Contains(mapped))
			{
				destination[length++] = mapped;
			}
		}

		return length;
	}

	private static void Encode(ReadOnlySpan<byte> data, Span<char> destination)
	{
		var bitBuffer = 0;
		var bitCount = 0;
		var charIndex = 0;

		foreach (var b in data)
		{
			bitBuffer = (bitBuffer << 8) | b;
			bitCount += 8;

			while (bitCount >= 5)
			{
				bitCount -= 5;
				destination[charIndex++] = Alphabet[(bitBuffer >> bitCount) & 0b11111];
			}
		}

		if (bitCount > 0)
		{
			destination[charIndex++] = Alphabet[(bitBuffer << (5 - bitCount)) & 0b11111];
		}
	}

	private static bool TryDecode(ReadOnlySpan<char> chars, Span<byte> destination)
	{
		var bitBuffer = 0;
		var bitCount = 0;
		var byteIndex = 0;

		foreach (var ch in chars)
		{
			var value = Alphabet.IndexOf(ch);
			bitBuffer = (bitBuffer << 5) | value;
			bitCount += 5;

			if (bitCount >= 8)
			{
				bitCount -= 8;
				destination[byteIndex++] = (byte)(bitBuffer >> bitCount);
			}
		}

		var leftoverMask = (1 << bitCount) - 1;

		return byteIndex == destination.Length && (bitBuffer & leftoverMask) == 0;
	}
}
