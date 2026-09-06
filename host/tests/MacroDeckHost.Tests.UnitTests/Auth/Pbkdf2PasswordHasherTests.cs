using MacroDeckHost.Infrastructure.Auth;

namespace MacroDeckHost.Tests.UnitTests.Auth;

public class Pbkdf2PasswordHasherTests
{
	private readonly Pbkdf2PasswordHasher _hasher = new();

	[Test]
	public void Roundtrip_verifies_correct_password()
	{
		var encoded = _hasher.Hash("correct horse battery staple");

		Assert.Multiple(() =>
		{
			Assert.That(_hasher.Verify("correct horse battery staple", encoded), Is.True);
			Assert.That(encoded, Does.StartWith("PBKDF2$SHA256$"));
		});
	}

	[Test]
	public void Wrong_password_fails()
	{
		var encoded = _hasher.Hash("correct horse battery staple");

		Assert.That(_hasher.Verify("wrong password", encoded), Is.False);
	}

	[Test]
	public void Same_password_produces_different_hashes()
	{
		var first = _hasher.Hash("password123");
		var second = _hasher.Hash("password123");

		Assert.That(second, Is.Not.EqualTo(first));
	}

	[TestCase("")]
	[TestCase("garbage")]
	[TestCase("PBKDF2$SHA256$notanumber$c2FsdA==$aGFzaA==")]
	[TestCase("PBKDF2$SHA256$1000$not-base64!$aGFzaA==")]
	[TestCase("BCRYPT$SHA256$1000$c2FsdA==$aGFzaA==")]
	public void Malformed_encoding_fails_without_throwing(string encoded)
	{
		Assert.That(_hasher.Verify("password123", encoded), Is.False);
	}
}
