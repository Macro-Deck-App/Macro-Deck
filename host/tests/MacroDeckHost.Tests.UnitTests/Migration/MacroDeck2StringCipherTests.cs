using MacroDeckHost.Infrastructure.Migration.MacroDeck2;

namespace MacroDeckHost.Tests.UnitTests.Migration;

[TestFixture]
public class MacroDeck2StringCipherTests
{
	// Produced with OpenSSL rather than with the code under test, so this pins the scheme itself:
	// PBKDF2-HMAC-SHA1 over a 16-byte salt, 1000 iterations, a 32-byte key, AES-256-CBC/PKCS7, laid out as
	// base64(salt || iv || ciphertext). A round trip through Encrypt/TryDecrypt would agree with itself
	// even if every one of those parameters changed, and every existing Macro Deck 2 credential file would
	// then be unreadable.
	private const string GoldenCipherText =
		"AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8tII1lrf6+vfRp8tF5OuXdnY/+Bc9F+gbDVQIHHr8T6A==";

	private const string GoldenPassPhrase = "b6e2b3f2-9f1a-4a55-9f0f-2c7a1e5d8a31";

	private const string GoldenPlainText = "oauth:md2-migration-test-token";

	[Test]
	public void TryDecrypt_ReadsTheFormatMacroDeck2Wrote()
		=> Assert.That(MacroDeck2StringCipher.TryDecrypt(GoldenCipherText, GoldenPassPhrase),
			Is.EqualTo(GoldenPlainText));

	[Test]
	public void TryDecrypt_WithTheWrongKey_ReportsFailureInsteadOfThrowing()
		=> Assert.That(MacroDeck2StringCipher.TryDecrypt(GoldenCipherText, "00000000-0000-0000-0000-000000000000"),
			Is.Null);

	// Macro Deck 2's format carries no authentication tag, so a wrong key clears PKCS7 padding by chance
	// for roughly one value in 256. This pair is one of those: the wrong key below decrypts it without a
	// CryptographicException, leaving bytes that are not text. Accepting them made the host trust a key
	// that opens nothing, which is what MigrationError.InvalidDecryptionKey exists to prevent.
	private const string PaddingCollisionCipherText =
		"Dp9ceQnYiOf79LW4B+9eu8PwZpzt4T0wgUvPmq69v9adhw36YYSy6FPTOencG1qI";

	private const string PaddingCollisionWrongPassPhrase = "11111111-1111-1111-1111-111111111111";

	[Test]
	public void TryDecrypt_WithAWrongKeyThatClearsPadding_StillReportsFailure()
		=> Assert.That(MacroDeck2StringCipher.TryDecrypt(PaddingCollisionCipherText,
				PaddingCollisionWrongPassPhrase),
			Is.Null);

	// A truncated or hand-edited value must be refused the same way a wrong key is: the migration offers to
	// carry on without the encrypted data, and that path is only reachable if nothing here throws.
	[TestCase("")]
	[TestCase("   ")]
	[TestCase("not base64 at all")]
	[TestCase("AAECAwQFBgcICQoLDA0ODw==")]
	public void TryDecrypt_WithUnusableInput_ReturnsNull(string cipherText)
		=> Assert.That(MacroDeck2StringCipher.TryDecrypt(cipherText, GoldenPassPhrase), Is.Null);

	[Test]
	public void Encrypt_ProducesAValueItsOwnReaderAccepts()
	{
		var encrypted = MacroDeck2StringCipher.Encrypt("a value", GoldenPassPhrase);

		Assert.Multiple(() =>
		{
			Assert.That(MacroDeck2StringCipher.TryDecrypt(encrypted, GoldenPassPhrase), Is.EqualTo("a value"));
			Assert.That(encrypted,
				Is.Not.EqualTo(MacroDeck2StringCipher.Encrypt("a value", GoldenPassPhrase)),
				"each value carries its own random salt and IV");
		});
	}
}
