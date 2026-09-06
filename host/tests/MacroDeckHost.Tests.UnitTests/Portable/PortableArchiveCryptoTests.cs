using System.Diagnostics;
using System.Text;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Infrastructure.Portable;

namespace MacroDeckHost.Tests.UnitTests.Portable;

[TestFixture]
public class PortableArchiveCryptoTests
{
	private static readonly byte[] _plaintext = Encoding.UTF8.GetBytes("{\"secret\":\"hunter2\",\"nested\":[1,2,3]}");
	private static readonly byte[] _aad = "manifest"u8.ToArray();

	private static readonly string[] _expectedProperties = ["Cipher", "Kdf", "Id", "Iterations", "Salt"];

	[Test]
	public void Encrypt_ThenDecrypt_WithCorrectPassword_RoundTrips()
	{
		var info = PortableArchiveCrypto.CreateParameters();
		var payload = PortableArchiveCrypto.Encrypt(_plaintext, "correct horse", info, _aad);

		var status = PortableArchiveCrypto.TryDecrypt(payload, info, _aad, "correct horse", out var decrypted);

		Assert.Multiple(() =>
		{
			Assert.That(status, Is.EqualTo(PortableDecryptResult.Success));
			Assert.That(decrypted, Is.EqualTo(_plaintext));
			Assert.That(info.Cipher, Is.EqualTo(PortableEncryptionInfo.Aes256Gcm));
			Assert.That(info.Kdf!.Id, Is.EqualTo(PortableKdfInfo.Pbkdf2HmacSha256));
			Assert.That(info.Kdf.Iterations, Is.EqualTo(PortableArchiveCrypto.Iterations));
			Assert.That(Convert.FromBase64String(info.Kdf.Salt!), Has.Length.EqualTo(16));
		});
	}

	[Test]
	public void CreateParameters_CarriesNoPasswordVerifier()
	{
		var info = PortableArchiveCrypto.CreateParameters();

		var properties = typeof(PortableEncryptionInfo).GetProperties()
			.Concat(typeof(PortableKdfInfo).GetProperties())
			.Select(property => property.Name)
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(properties, Does.Not.Contain("PasswordHash"));
			Assert.That(properties, Is.EquivalentTo(_expectedProperties));
			Assert.That(info.Kdf, Is.Not.Null);
		});
	}

	[Test]
	public void TryDecrypt_WithWrongPassword_ReturnsWrongPassword()
	{
		var info = PortableArchiveCrypto.CreateParameters();
		var payload = PortableArchiveCrypto.Encrypt(_plaintext, "correct horse", info, _aad);

		var status = PortableArchiveCrypto.TryDecrypt(payload, info, _aad, "wrong horse", out var decrypted);

		Assert.Multiple(() =>
		{
			Assert.That(status, Is.EqualTo(PortableDecryptResult.WrongPassword));
			Assert.That(decrypted, Is.Empty);
		});
	}

	[Test]
	public void TryDecrypt_WithTamperedPayload_ReturnsCorrupt()
	{
		var info = PortableArchiveCrypto.CreateParameters();
		var payload = PortableArchiveCrypto.Encrypt(_plaintext, "correct horse", info, _aad);
		payload[^1] ^= 0xFF;

		var status = PortableArchiveCrypto.TryDecrypt(payload, info, _aad, "correct horse", out _);

		Assert.That(status, Is.EqualTo(PortableDecryptResult.Corrupt));
	}

	[Test]
	public void TryDecrypt_WithTamperedAssociatedData_Fails()
	{
		var info = PortableArchiveCrypto.CreateParameters();
		var payload = PortableArchiveCrypto.Encrypt(_plaintext, "correct horse", info, _aad);

		var status = PortableArchiveCrypto.TryDecrypt(payload, info, "manifezt"u8, "correct horse", out _);

		Assert.That(status, Is.EqualTo(PortableDecryptResult.WrongPassword));
	}

	[Test]
	public void TryDecrypt_WithTamperedWrappedKey_Fails()
	{
		var info = PortableArchiveCrypto.CreateParameters();
		var payload = PortableArchiveCrypto.Encrypt(_plaintext, "correct horse", info, _aad);
		payload[20] ^= 0xFF;

		var status = PortableArchiveCrypto.TryDecrypt(payload, info, _aad, "correct horse", out _);

		Assert.That(status, Is.EqualTo(PortableDecryptResult.WrongPassword));
	}

	[Test]
	public void TryDecrypt_WithForeignMagic_ReturnsCorruptWithoutDerivingAKey()
	{
		var info = PortableArchiveCrypto.CreateParameters();
		var payload = PortableArchiveCrypto.Encrypt(_plaintext, "correct horse", info, _aad);
		payload[0] ^= 0xFF;

		var stopwatch = Stopwatch.StartNew();
		var status = PortableArchiveCrypto.TryDecrypt(payload, info, _aad, "correct horse", out _);
		stopwatch.Stop();

		Assert.Multiple(() =>
		{
			Assert.That(status, Is.EqualTo(PortableDecryptResult.Corrupt));
			Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(500), "the magic check must precede the KDF");
		});
	}

	[Test]
	public void TryDecrypt_WithTruncatedPayload_ReturnsCorrupt()
	{
		var info = PortableArchiveCrypto.CreateParameters();
		var payload = PortableArchiveCrypto.Encrypt(_plaintext, "correct horse", info, _aad);

		var status = PortableArchiveCrypto.TryDecrypt(payload[..40], info, _aad, "correct horse", out _);

		Assert.That(status, Is.EqualTo(PortableDecryptResult.Corrupt));
	}

	[TestCase(PortableArchiveCrypto.MinIterations - 1)]
	[TestCase(PortableArchiveCrypto.MaxIterations + 1)]
	[TestCase(0)]
	[TestCase(-1)]
	public void TryDecrypt_WithOutOfRangeIterations_IsRejected(int iterations)
	{
		var info = PortableArchiveCrypto.CreateParameters();
		var payload = PortableArchiveCrypto.Encrypt(_plaintext, "correct horse", info, _aad);
		info.Kdf!.Iterations = iterations;

		var status = PortableArchiveCrypto.TryDecrypt(payload, info, _aad, "correct horse", out _);

		Assert.That(status, Is.EqualTo(PortableDecryptResult.Corrupt));
	}

	[Test]
	public void TryDecrypt_WithAbsurdIterationCount_FailsFastInsteadOfBurningCpu()
	{
		var info = PortableArchiveCrypto.CreateParameters();
		var payload = PortableArchiveCrypto.Encrypt(_plaintext, "correct horse", info, _aad);
		info.Kdf!.Iterations = int.MaxValue;

		var stopwatch = Stopwatch.StartNew();
		var status = PortableArchiveCrypto.TryDecrypt(payload, info, _aad, "correct horse", out _);
		stopwatch.Stop();

		Assert.Multiple(() =>
		{
			Assert.That(status, Is.EqualTo(PortableDecryptResult.Corrupt));
			Assert.That(stopwatch.ElapsedMilliseconds,
				Is.LessThan(2_000),
				"an unclamped iteration count would derive for minutes");
		});
	}

	[Test]
	public void TryDecrypt_WithUnknownCipher_ReturnsUnsupportedAlgorithm()
	{
		var info = PortableArchiveCrypto.CreateParameters();
		var payload = PortableArchiveCrypto.Encrypt(_plaintext, "correct horse", info, _aad);
		info.Cipher = "ChaCha20-Poly1305";

		var status = PortableArchiveCrypto.TryDecrypt(payload, info, _aad, "correct horse", out _);

		Assert.That(status, Is.EqualTo(PortableDecryptResult.UnsupportedAlgorithm));
	}

	[Test]
	public void TryDecrypt_WithUnknownKdf_ReturnsUnsupportedAlgorithm()
	{
		var info = PortableArchiveCrypto.CreateParameters();
		var payload = PortableArchiveCrypto.Encrypt(_plaintext, "correct horse", info, _aad);
		info.Kdf!.Id = "Argon2id";

		var status = PortableArchiveCrypto.TryDecrypt(payload, info, _aad, "correct horse", out _);

		Assert.That(status, Is.EqualTo(PortableDecryptResult.UnsupportedAlgorithm));
	}

	[Test]
	public void TryDecrypt_WithMissingKdf_IsRejected()
	{
		var info = PortableArchiveCrypto.CreateParameters();
		var payload = PortableArchiveCrypto.Encrypt(_plaintext, "correct horse", info, _aad);
		info.Kdf = null;

		var status = PortableArchiveCrypto.TryDecrypt(payload, info, _aad, "correct horse", out _);

		Assert.That(status, Is.EqualTo(PortableDecryptResult.UnsupportedAlgorithm));
	}

	[TestCase("")]
	[TestCase("not base64!")]
	[TestCase("AAAA")]
	public void TryDecrypt_WithMalformedSalt_ReturnsCorrupt(string salt)
	{
		var info = PortableArchiveCrypto.CreateParameters();
		var payload = PortableArchiveCrypto.Encrypt(_plaintext, "correct horse", info, _aad);
		info.Kdf!.Salt = salt;

		var status = PortableArchiveCrypto.TryDecrypt(payload, info, _aad, "correct horse", out _);

		Assert.That(status, Is.EqualTo(PortableDecryptResult.Corrupt));
	}

	[Test]
	public void Encrypt_UsesAFreshSaltAndKeyEachTime()
	{
		var first = PortableArchiveCrypto.CreateParameters();
		var firstPayload = PortableArchiveCrypto.Encrypt(_plaintext, "pw", first, _aad);
		var second = PortableArchiveCrypto.CreateParameters();
		var secondPayload = PortableArchiveCrypto.Encrypt(_plaintext, "pw", second, _aad);

		Assert.Multiple(() =>
		{
			Assert.That(first.Kdf!.Salt, Is.Not.EqualTo(second.Kdf!.Salt));
			Assert.That(firstPayload, Is.Not.EqualTo(secondPayload));
		});
	}

	[Test]
	public void Encrypt_WithParametersItCannotHonour_Throws()
	{
		var info = PortableArchiveCrypto.CreateParameters();
		info.Cipher = "AES-128-CBC";

		Assert.Throws<ArgumentException>(() => PortableArchiveCrypto.Encrypt(_plaintext, "pw", info, _aad));
	}

	[Test]
	public void Encrypt_WithEmptyPlaintext_RoundTrips()
	{
		var info = PortableArchiveCrypto.CreateParameters();
		var payload = PortableArchiveCrypto.Encrypt([], "pw", info, _aad);

		var status = PortableArchiveCrypto.TryDecrypt(payload, info, _aad, "pw", out var decrypted);

		Assert.Multiple(() =>
		{
			Assert.That(status, Is.EqualTo(PortableDecryptResult.Success));
			Assert.That(decrypted, Is.Empty);
		});
	}
}
