using System.Security.Cryptography;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Infrastructure.Backups.Crypto;

namespace MacroDeckHost.Tests.UnitTests.Backups;

[TestFixture]
public class BackupRecoveryKeyFormatTests
{
	[Test]
	public void RoundTripsAFormattedKeyBackToTheSameBytes()
	{
		var key = RandomNumberGenerator.GetBytes(BackupRecoveryKeyFormat.KeyBytes);
		var formatted = Format(key);

		var parsedKey = new byte[BackupRecoveryKeyFormat.KeyBytes];
		Span<byte> parsedChecksum = stackalloc byte[BackupRecoveryKeyFormat.ChecksumBytes];
		var success = BackupRecoveryKeyFormat.TryParse(formatted, parsedKey, parsedChecksum, out var error);

		Assert.Multiple(() =>
		{
			Assert.That(success, Is.True);
			Assert.That(error, Is.EqualTo(BackupRecoveryKeyParseError.None));
			Assert.That(parsedKey, Is.EqualTo(key));
		});
	}

	[Test]
	public void ParsesAPasteWithDifferentCasingNoSeparatorsAndSurroundingWhitespace()
	{
		var key = RandomNumberGenerator.GetBytes(BackupRecoveryKeyFormat.KeyBytes);
		var formatted = Format(key);
		var pasted = "  \n\t" + formatted.Replace("-", string.Empty).ToLowerInvariant() + "  \n";

		var parsedKey = new byte[BackupRecoveryKeyFormat.KeyBytes];
		Span<byte> parsedChecksum = stackalloc byte[BackupRecoveryKeyFormat.ChecksumBytes];
		var success = BackupRecoveryKeyFormat.TryParse(pasted, parsedKey, parsedChecksum, out var error);

		Assert.Multiple(() =>
		{
			Assert.That(success, Is.True);
			Assert.That(error, Is.EqualTo(BackupRecoveryKeyParseError.None));
			Assert.That(parsedKey, Is.EqualTo(key));
		});
	}

	[Test]
	public void ParsesOAndIAndLTypedInPlaceOfZeroAndOne()
	{
		var key = RandomNumberGenerator.GetBytes(BackupRecoveryKeyFormat.KeyBytes);
		var formatted = Format(key);

		var confused = new string([
			.. formatted.Select((ch, index) => ch switch
			{
				'0' => 'O',
				'1' => index % 2 == 0 ? 'I' : 'L',
				_ => ch
			})
		]);

		var parsedKey = new byte[BackupRecoveryKeyFormat.KeyBytes];
		Span<byte> parsedChecksum = stackalloc byte[BackupRecoveryKeyFormat.ChecksumBytes];
		var success = BackupRecoveryKeyFormat.TryParse(confused, parsedKey, parsedChecksum, out var error);

		Assert.Multiple(() =>
		{
			Assert.That(success, Is.True);
			Assert.That(error, Is.EqualTo(BackupRecoveryKeyParseError.None));
			Assert.That(parsedKey, Is.EqualTo(key));
		});
	}

	[Test]
	public void ATranscriptionErrorIsCaughtByTheChecksumRatherThanSilentlyProducingDifferentKeyBytes()
	{
		var key = RandomNumberGenerator.GetBytes(BackupRecoveryKeyFormat.KeyBytes);
		var formatted = Format(key).ToCharArray();

		// Index 6 is the first character of the encoded payload, right after the "MDBK1-" prefix, so
		// flipping it can never collide with the canonical-encoding check on the final symbol.
		formatted[6] = formatted[6] == '0' ? '1' : '0';
		var tampered = new string(formatted);

		var parsedKey = new byte[BackupRecoveryKeyFormat.KeyBytes];
		Span<byte> parsedChecksum = stackalloc byte[BackupRecoveryKeyFormat.ChecksumBytes];
		var parsed = BackupRecoveryKeyFormat.TryParse(tampered, parsedKey, parsedChecksum, out _);

		Assert.That(parsed, Is.True);

		Span<byte> recomputedChecksum = stackalloc byte[BackupRecoveryKeyFormat.ChecksumBytes];
		BackupKeyDerivation.DeriveChecksum(parsedKey, recomputedChecksum);

		Assert.That(recomputedChecksum.SequenceEqual(parsedChecksum), Is.False);
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("   \n\t")]
	public void ReportsEmptyForBlankInputRatherThanThrowing(string? text)
	{
		Span<byte> key = stackalloc byte[BackupRecoveryKeyFormat.KeyBytes];
		Span<byte> checksum = stackalloc byte[BackupRecoveryKeyFormat.ChecksumBytes];
		var success = BackupRecoveryKeyFormat.TryParse(text, key, checksum, out var error);

		Assert.Multiple(() =>
		{
			Assert.That(success, Is.False);
			Assert.That(error, Is.EqualTo(BackupRecoveryKeyParseError.Empty));
		});
	}

	[Test]
	public void ReportsMalformedForInputWithNoCrockfordCharactersRatherThanThrowing()
	{
		Span<byte> key = stackalloc byte[BackupRecoveryKeyFormat.KeyBytes];
		Span<byte> checksum = stackalloc byte[BackupRecoveryKeyFormat.ChecksumBytes];
		var success = BackupRecoveryKeyFormat.TryParse("!!! @@@ ### ???", key, checksum, out var error);

		Assert.Multiple(() =>
		{
			Assert.That(success, Is.False);
			Assert.That(error, Is.EqualTo(BackupRecoveryKeyParseError.Malformed));
		});
	}

	private static string Format(byte[] key)
	{
		Span<byte> checksum = stackalloc byte[BackupRecoveryKeyFormat.ChecksumBytes];
		BackupKeyDerivation.DeriveChecksum(key, checksum);

		return BackupRecoveryKeyFormat.Format(key, checksum);
	}

	// Crockford indices of M,D,B,K,1 are 20,13,11,19,1, so these leading bytes make the encoded key start
	// with the very characters used as the format prefix.
	[Test]
	public void ParsesAKeyWhoseOwnEncodingStartsWithThePrefixCharacters()
	{
		var key = new byte[32];
		key[0] = 0xA3;
		key[1] = 0x57;
		key[2] = 0x30;
		key[3] = 0x80;
		var checksum = new byte[] { 0x12, 0x34 };

		var formatted = BackupRecoveryKeyFormat.Format(key, checksum);

		// Strip only the leading prefix and its separator - the encoded body starts with the same
		// characters, which is the whole point of this case.
		var withoutPrefix = formatted["MDBK1-".Length..].Replace("-", string.Empty, StringComparison.Ordinal);

		var parsed = new byte[32];
		var parsedChecksum = new byte[2];
		var succeeded = BackupRecoveryKeyFormat.TryParse(withoutPrefix, parsed, parsedChecksum, out var error);

		Assert.Multiple(() =>
		{
			Assert.That(succeeded, Is.True, $"parse failed with {error}");
			Assert.That(parsed, Is.EqualTo(key));
		});
	}
}
