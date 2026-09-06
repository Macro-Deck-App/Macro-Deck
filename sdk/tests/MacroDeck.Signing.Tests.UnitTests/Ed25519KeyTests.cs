using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.Keys;
using MacroDeck.Signing.TestSupport;

namespace MacroDeck.Signing.Tests.UnitTests;

/// <summary>
/// Raw key generation and one cross-implementation known-answer vector, so a future provider swap for
/// <see cref="Ed25519KeyPair" /> and <see cref="Ed25519VerificationKey" /> is caught even though nothing
/// else here pins to a specific implementation.
/// </summary>
[TestFixture]
internal sealed class Ed25519KeyTests
{
	[Test]
	public void A_generated_key_pair_round_trips_through_sign_and_verify()
	{
		var (privateKey, publicKey) = Ed25519KeyPair.Create();
		var certificate = TestPki.IssueCertificate(subjectKeyOverride: (privateKey, publicKey));
		var trusted = TestPki.VerifyChain(certificate, SigningCertificateChain.PackageKeyUsage);

		var materialResult = SigningMaterial.Create(privateKey, trusted.Certificate);
		Assert.That(materialResult.Success, Is.True);

		using var material = materialResult.Material!;
		var payload = "payload"u8.ToArray();
		var signature = material.Sign(payload);

		var verificationKey = Ed25519VerificationKey.TryImport(publicKey);
		Assert.That(verificationKey, Is.Not.Null);
		Assert.That(verificationKey!.Verify(payload, signature), Is.True);
	}

	[Test]
	public void Generated_keys_are_exactly_32_raw_bytes()
	{
		var (privateKey, publicKey) = Ed25519KeyPair.Create();

		Assert.Multiple(() =>
		{
			Assert.That(privateKey, Has.Length.EqualTo(Ed25519KeyPair.PrivateKeyLength));
			Assert.That(publicKey, Has.Length.EqualTo(Ed25519KeyPair.PublicKeyLength));
		});
	}

	[Test]
	public void Two_generations_produce_different_keys()
	{
		var first = Ed25519KeyPair.Create();
		var second = Ed25519KeyPair.Create();

		Assert.Multiple(() =>
		{
			Assert.That(first.PrivateKey, Is.Not.EqualTo(second.PrivateKey));
			Assert.That(first.PublicKey, Is.Not.EqualTo(second.PublicKey));
		});
	}

	/// <summary>A signature made by an unrelated key must not verify - the failure path
	/// <see cref="Ed25519VerificationKey.Verify" /> exists for, distinct from throwing.</summary>
	[Test]
	public void A_wrong_signature_returns_false_rather_than_throwing()
	{
		var (signerPrivate, _) = Ed25519KeyPair.Create();
		var (_, otherPublic) = Ed25519KeyPair.Create();

		var verificationKey = Ed25519VerificationKey.TryImport(otherPublic);
		var signature = SignRaw(signerPrivate, "payload"u8.ToArray());

		Assert.DoesNotThrow(() => Assert.That(verificationKey!.Verify("payload"u8.ToArray(), signature), Is.False));
	}

	/// <summary>
	/// A known-answer vector produced by OpenSSL 3.6's own Ed25519 implementation - deliberately a
	/// different codebase than the libsodium-backed NSec this library uses - so a future provider swap that
	/// silently changes what this library signs or accepts is caught, not just a swap that breaks
	/// self-consistency. The seed, public key, message and signature are all fixed constants; nothing here
	/// is generated at test time.
	/// </summary>
	[Test]
	public void A_cross_implementation_known_answer_vector_verifies()
	{
		var message = "Macro Deck Ed25519 KAT"u8.ToArray();
		var publicKey = Convert.FromHexString("d81628ff82fc01f4969c7c1e4f92815fbefcdb0bf1e092d4aeb4e40c234bf0b5");
		var expectedSignature = Convert.FromHexString(
			"eee2d11c7b69e09e5fd7e2153fd1df0c68592f1ef29fd9db8c6cf923943e7ec2620af1ceac84cadf3d236c189eab3ef03dda56a589bb89d16186458d50f19d0b");

		var verificationKey = Ed25519VerificationKey.TryImport(publicKey);
		Assert.That(verificationKey, Is.Not.Null);
		Assert.That(verificationKey!.Verify(message, expectedSignature), Is.True);
	}

	private static byte[] SignRaw(byte[] rawPrivateKey, byte[] payload)
	{
		using var key = NSec.Cryptography.Key.Import(NSec.Cryptography.SignatureAlgorithm.Ed25519,
			rawPrivateKey,
			NSec.Cryptography.KeyBlobFormat.RawPrivateKey);
		return NSec.Cryptography.SignatureAlgorithm.Ed25519.Sign(key, payload);
	}
}
