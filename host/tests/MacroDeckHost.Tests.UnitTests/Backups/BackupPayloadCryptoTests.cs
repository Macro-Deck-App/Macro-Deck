using System.Security.Cryptography;
using System.Text;
using MacroDeckHost.Infrastructure.Backups.Crypto;

namespace MacroDeckHost.Tests.UnitTests.Backups;

[TestFixture]
public class BackupPayloadCryptoTests
{
	private static readonly byte[] _manifest = Encoding.UTF8.GetBytes("""{"formatVersion":1}""");

	[Test]
	public void RoundTripsAPayloadLargerThanOneSegment()
	{
		var key = RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes);
		var plaintext = RandomNumberGenerator.GetBytes((1 << 20) * 2 + 12_345);

		var restored = Decrypt(Encrypt(key, plaintext), key);

		Assert.That(restored, Is.EqualTo(plaintext));
	}

	[Test]
	public void RoundTripsAnExactMultipleOfTheSegmentSize()
	{
		var key = RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes);
		var plaintext = RandomNumberGenerator.GetBytes(1 << 20);

		Assert.That(Decrypt(Encrypt(key, plaintext), key), Is.EqualTo(plaintext));
	}

	[Test]
	public void RoundTripsAnEmptyPayload()
	{
		var key = RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes);

		Assert.That(Decrypt(Encrypt(key, []), key), Is.Empty);
	}

	[Test]
	public void RefusesADifferentRecoveryKey()
	{
		var payload = Encrypt(RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes), [1, 2, 3]);
		var other = RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes);

		var error = Assert.Throws<BackupCryptoException>(() => Decrypt(payload, other));

		Assert.That(error.Result, Is.EqualTo(BackupDecryptResult.WrongKey));
	}

	[Test]
	public void RefusesAnEditedManifest()
	{
		var key = RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes);
		var payload = Encrypt(key, [1, 2, 3]);

		var error = Assert.Throws<BackupCryptoException>(() =>
			Decrypt(payload, key, Encoding.UTF8.GetBytes("""{"formatVersion":2}""")));

		Assert.That(error.Result, Is.EqualTo(BackupDecryptResult.WrongKey));
	}

	[Test]
	public void RefusesATamperedSegment()
	{
		var key = RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes);
		var payload = Encrypt(key, RandomNumberGenerator.GetBytes(4_096));
		payload[^1] ^= 0xFF;

		var error = Assert.Throws<BackupCryptoException>(() => Decrypt(payload, key));

		Assert.That(error.Result, Is.EqualTo(BackupDecryptResult.Corrupt));
	}

	[Test]
	public void RefusesAPayloadWithItsFinalSegmentRemoved()
	{
		var key = RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes);
		var payload = Encrypt(key, RandomNumberGenerator.GetBytes((1 << 20) + 4_096));
		var truncated = payload[..(BackupPayloadHeader.TotalBytes + (1 << 20) + BackupPayloadHeader.TagBytes)];

		var error = Assert.Throws<BackupCryptoException>(() => Decrypt(truncated, key));

		Assert.That(error.Result, Is.EqualTo(BackupDecryptResult.Corrupt));
	}

	[Test]
	public void DerivesAStableKeyIdThatDiffersPerKey()
	{
		var key = RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes);
		var other = RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes);
		var id = BackupKeyDerivation.DeriveKeyId(key);
		var repeated = BackupKeyDerivation.DeriveKeyId([.. key]);
		var foreign = BackupKeyDerivation.DeriveKeyId(other);

		Assert.Multiple(() =>
		{
			Assert.That(repeated, Is.EqualTo(id));
			Assert.That(foreign, Is.Not.EqualTo(id));
			Assert.That(id, Has.Length.EqualTo(BackupKeyDerivation.KeyIdBytes * 2));
		});
	}

	private static byte[] Encrypt(byte[] key, byte[] plaintext)
	{
		using var destination = new MemoryStream();
		using (var encryptor = BackupPayloadCrypto.CreateEncryptor(destination, key, _manifest, leaveOpen: true))
		{
			encryptor.Write(plaintext);
		}

		return destination.ToArray();
	}

	private static byte[] Decrypt(byte[] payload, byte[] key, byte[]? manifest = null)
	{
		using var source = new MemoryStream(payload);
		using var decryptor = BackupPayloadCrypto.CreateDecryptor(source, key, manifest ?? _manifest, leaveOpen: true);
		using var restored = new MemoryStream();
		decryptor.CopyTo(restored);

		return restored.ToArray();
	}
}
