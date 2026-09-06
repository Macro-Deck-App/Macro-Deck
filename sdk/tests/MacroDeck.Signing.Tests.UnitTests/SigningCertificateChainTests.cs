using System.Text;
using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.TestSupport;

namespace MacroDeck.Signing.Tests.UnitTests;

/// <summary>
/// <see cref="SigningCertificateChain" />: the root signature is checked over the certificate's exact file
/// bytes, key usage must be exactly one entry, subject kind must be permitted for that usage, and validity
/// is a separate, explicit step from chain trust.
/// </summary>
[TestFixture]
internal sealed class SigningCertificateChainTests
{
	[Test]
	public void A_validly_issued_package_certificate_verifies()
	{
		var issued = TestPki.IssueCertificate();

		var result = SigningCertificateChain.Verify(issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			TestPki.Root.PublicKey,
			SigningCertificateChain.PackageKeyUsage);

		Assert.That(result.Success, Is.True);
	}

	/// <summary>The root signature covers the certificate's exact bytes - re-serializing with different
	/// whitespace or property order, even carrying the original <c>.sig</c> forward unchanged, must be
	/// rejected. A verifier that re-serialized before checking the signature would accept this.</summary>
	[Test]
	public void A_byte_identical_document_reserialized_with_different_formatting_is_rejected()
	{
		var issued = TestPki.IssueCertificate();
		var reformatted = TestPki.ReserializeWithDifferentFormatting(issued.CertificateBytes);

		// Sanity: still the same logical document, just different bytes.
		Assert.That(reformatted, Is.Not.EqualTo(issued.CertificateBytes));

		var result = SigningCertificateChain.Verify(reformatted,
			issued.CertificateSignatureBytes,
			TestPki.Root.PublicKey,
			SigningCertificateChain.PackageKeyUsage);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.CertificateUntrusted));
		});
	}

	/// <summary>A certificate signed by a different root must be untrusted even though its own
	/// <c>rootKeyId</c> claims to name the trusted root - <c>rootKeyId</c> is a label the document carries
	/// about itself, not something the verifier can trust without the signature to back it.</summary>
	[Test]
	public void A_certificate_signed_by_a_different_root_is_untrusted_even_when_rootKeyId_names_the_trusted_root()
	{
		var issued = TestPki.IssueCertificate(signingRoot: TestPki.OtherRoot, rootKeyId: "root_test0001");

		var result = SigningCertificateChain.Verify(issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			TestPki.Root.PublicKey,
			SigningCertificateChain.PackageKeyUsage);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.CertificateUntrusted));
		});
	}

	private static readonly IEnumerable<TestCaseData> _wrongKeyUsageCases =
	[
		new TestCaseData((object)new[] { "package", "registry" }).SetName("package_plus_registry"),
		new TestCaseData((object)new[] { "registry" }).SetName("registry_only"),
		new TestCaseData((object)Array.Empty<string>()).SetName("empty")
	];

	[TestCaseSource(nameof(_wrongKeyUsageCases))]
	public void KeyUsage_must_be_exactly_one_package_entry_not_a_superset_or_a_different_single_entry(string[] keyUsage)
	{
		var issued = TestPki.IssueCertificate(keyUsage: keyUsage);

		var result = SigningCertificateChain.Verify(issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			TestPki.Root.PublicKey,
			SigningCertificateChain.PackageKeyUsage);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.CertificateWrongPurpose));
		});
	}

	/// <summary>A certificate that declares no <c>keyUsage</c> array at all fails deserialization of the
	/// required property, distinctly from an empty array - both must still end up rejected for package
	/// signing. Re-signed after mutation so the failure is isolated to the document's shape, not to the
	/// byte-signature mismatch a naive mutation would otherwise trigger first.</summary>
	[Test]
	public void An_absent_keyUsage_property_is_rejected()
	{
		var issued = TestPki.IssueCertificate();
		var withoutKeyUsage = RemoveProperty(issued.CertificateBytes, "keyUsage");
		var resignedSignature = TestPki.SignCertificateBytesAsBase64Text(TestPki.Root.PrivateKey, withoutKeyUsage);

		var result = SigningCertificateChain.Verify(withoutKeyUsage,
			resignedSignature,
			TestPki.Root.PublicKey,
			SigningCertificateChain.PackageKeyUsage);

		Assert.That(result.Success, Is.False);
	}

	[Test]
	public void Subject_kind_service_is_rejected_for_package_signing()
	{
		var issued = TestPki.IssueCertificate(subjectKind: "service");

		var result = SigningCertificateChain.Verify(issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			TestPki.Root.PublicKey,
			SigningCertificateChain.PackageKeyUsage);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.CertificateWrongPurpose));
		});
	}

	[TestCase("creator")]
	[TestCase("organization")]
	public void Creator_and_organization_subjects_are_accepted_for_package_signing(string subjectKind)
	{
		var issued = TestPki.IssueCertificate(subjectKind: subjectKind);

		var result = SigningCertificateChain.Verify(issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			TestPki.Root.PublicKey,
			SigningCertificateChain.PackageKeyUsage);

		Assert.That(result.Success, Is.True);
	}

	[Test]
	public void A_service_subject_with_registry_key_usage_is_accepted_for_registry_signing()
	{
		var issued = TestPki.IssueCertificate(subjectKind: "service",
			keyUsage: [SigningCertificateChain.RegistryKeyUsage]);

		var result = SigningCertificateChain.Verify(issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			TestPki.Root.PublicKey,
			SigningCertificateChain.RegistryKeyUsage);

		Assert.That(result.Success, Is.True);
	}

	[Test]
	public void A_creator_subject_with_registry_key_usage_is_rejected_for_registry_signing()
	{
		var issued = TestPki.IssueCertificate(subjectKind: "creator",
			keyUsage: [SigningCertificateChain.RegistryKeyUsage]);

		var result = SigningCertificateChain.Verify(issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			TestPki.Root.PublicKey,
			SigningCertificateChain.RegistryKeyUsage);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.CertificateWrongPurpose));
		});
	}

	[Test]
	public void Validity_at_an_instant_inside_the_window_passes()
	{
		var notBefore = DateTimeOffset.Parse("2025-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
		var notAfter = DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
		var certificate = IssuedCertificateDocument(notBefore, notAfter);

		var failure = SigningCertificateChain.EnsureValidAt(certificate,
			DateTimeOffset.Parse("2025-06-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

		Assert.That(failure, Is.Null);
	}

	[Test]
	public void Validity_before_notBefore_and_after_notAfter_fail_with_distinguishable_errors()
	{
		var notBefore = DateTimeOffset.Parse("2025-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
		var notAfter = DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
		var certificate = IssuedCertificateDocument(notBefore, notAfter);

		var beforeFailure = SigningCertificateChain.EnsureValidAt(certificate, notBefore.AddSeconds(-1));
		var afterFailure = SigningCertificateChain.EnsureValidAt(certificate, notAfter.AddSeconds(1));

		Assert.Multiple(() =>
		{
			Assert.That(beforeFailure, Is.Not.Null);
			Assert.That(afterFailure, Is.Not.Null);
			Assert.That(beforeFailure!.Error, Is.EqualTo(SigningError.CertificateNotYetValid));
			Assert.That(afterFailure!.Error, Is.EqualTo(SigningError.CertificateExpired));
			Assert.That(beforeFailure.Error, Is.Not.EqualTo(afterFailure.Error));
		});
	}

	/// <summary>The boundary instants themselves are inclusive - exactly <c>notBefore</c> and exactly
	/// <c>notAfter</c> both pass.</summary>
	[Test]
	public void Validity_boundary_instants_are_inclusive()
	{
		var notBefore = DateTimeOffset.Parse("2025-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
		var notAfter = DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
		var certificate = IssuedCertificateDocument(notBefore, notAfter);

		var atNotBefore = SigningCertificateChain.EnsureValidAt(certificate, notBefore);
		var atNotAfter = SigningCertificateChain.EnsureValidAt(certificate, notAfter);

		Assert.Multiple(() =>
		{
			Assert.That(atNotBefore, Is.Null);
			Assert.That(atNotAfter, Is.Null);
		});
	}

	/// <summary>The issued <c>.sig</c> carries the signature base64-encoded as text - a file holding the raw
	/// signature bytes instead must be rejected, not silently reinterpreted.</summary>
	[Test]
	public void A_sig_file_holding_raw_bytes_instead_of_base64_text_is_rejected()
	{
		var issued = TestPki.IssueCertificate();
		var rawSignatureBytes = Convert.FromBase64String(Encoding.UTF8.GetString(issued.CertificateSignatureBytes));

		var result = SigningCertificateChain.Verify(issued.CertificateBytes,
			rawSignatureBytes,
			TestPki.Root.PublicKey,
			SigningCertificateChain.PackageKeyUsage);

		Assert.That(result.Success, Is.False);
	}

	private static SigningCertificate IssuedCertificateDocument(DateTimeOffset notBefore, DateTimeOffset notAfter)
	{
		var issued = TestPki.IssueCertificate(notBefore: notBefore, notAfter: notAfter);
		var result = SigningCertificateChain.Verify(issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			TestPki.Root.PublicKey,
			SigningCertificateChain.PackageKeyUsage);
		return result.TrustedCertificate!.Certificate;
	}

	private static byte[] RemoveProperty(byte[] certificateBytes, string propertyName)
	{
		var node = System.Text.Json.Nodes.JsonNode.Parse(certificateBytes)!.AsObject();
		node.Remove(propertyName);
		return Encoding.UTF8.GetBytes(node.ToJsonString(SigningJson.Options) + "\n");
	}
}
