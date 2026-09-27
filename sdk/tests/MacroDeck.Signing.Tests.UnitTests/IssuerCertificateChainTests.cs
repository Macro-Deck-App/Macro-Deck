using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.TestSupport;

namespace MacroDeck.Signing.Tests.UnitTests;

[TestFixture]
internal sealed class IssuerCertificateChainTests
{
	private static SigningCertificateVerificationResult Verify(TestPki.IssuedCertificate certificate,
		TestPki.IssuedCertificate? issuer,
		string requiredKeyUsage = SigningCertificateChain.PackageKeyUsage)
	{
		return SigningCertificateChain.Verify(certificate.CertificateBytes,
			certificate.CertificateSignatureBytes,
			issuer?.CertificateBytes,
			issuer?.CertificateSignatureBytes,
			TestPki.Root.PublicKey,
			requiredKeyUsage);
	}

	private static void AssertRefused(SigningCertificateVerificationResult result, SigningError expected)
	{
		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(expected), result.Message);
		});
	}

	[Test]
	public void A_package_certificate_signed_by_a_root_signed_issuer_verifies_and_names_its_issuer()
	{
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(issuer: issuer);

		var result = Verify(certificate, issuer);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.Message);
			Assert.That(result.TrustedCertificate!.Certificate.CertificateId, Is.EqualTo(certificate.CertificateId));
			Assert.That(result.TrustedCertificate.Issuer!.CertificateId, Is.EqualTo(issuer.CertificateId));
		});
	}

	[Test]
	public void A_registry_certificate_signed_by_a_root_signed_issuer_verifies()
	{
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(subjectKind: "service",
			keyUsage: [SigningCertificateChain.RegistryKeyUsage],
			issuer: issuer);

		var result = Verify(certificate, issuer, SigningCertificateChain.RegistryKeyUsage);

		Assert.That(result.Success, Is.True, result.Message);
	}

	[Test]
	public void A_root_signed_certificate_still_verifies_without_issuer_material()
	{
		var certificate = TestPki.IssueCertificate();

		var result = Verify(certificate, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.Message);
			Assert.That(result.TrustedCertificate!.Issuer, Is.Null);
		});
	}

	[Test]
	public void A_root_signed_schema_version_2_certificate_without_an_issuer_verifies()
	{
		var certificate = TestPki.IssueCertificate(schemaVersion: 2);

		Assert.That(Verify(certificate, null).Success, Is.True);
	}

	[Test]
	public void An_issuer_certificate_verifies_on_its_own_against_the_root()
	{
		var issuer = TestPki.IssueIssuer();

		var result = Verify(issuer, null, SigningCertificateChain.IssuerKeyUsage);

		Assert.That(result.Success, Is.True, result.Message);
	}

	[Test]
	public void An_issuer_not_signed_by_the_trusted_root_is_refused()
	{
		var issuer = TestPki.IssueIssuer(signingRoot: TestPki.OtherRoot);
		var certificate = TestPki.IssueCertificate(issuer: issuer);

		AssertRefused(Verify(certificate, issuer), SigningError.CertificateUntrusted);
	}

	[Test]
	public void An_issuer_without_the_issuer_key_usage_is_refused()
	{
		var issuer = TestPki.IssueIssuer(keyUsage: [SigningCertificateChain.PackageKeyUsage]);
		var certificate = TestPki.IssueCertificate(issuer: issuer);

		AssertRefused(Verify(certificate, issuer), SigningError.CertificateWrongPurpose);
	}

	[Test]
	public void A_package_certificate_used_as_an_issuer_is_refused()
	{
		var packageCertificate = TestPki.IssueCertificate(notBefore: DateTimeOffset.UtcNow.AddDays(-2),
			notAfter: DateTimeOffset.UtcNow.AddYears(2));
		var certificate = TestPki.IssueCertificate(issuer: packageCertificate);

		AssertRefused(Verify(certificate, packageCertificate), SigningError.CertificateWrongPurpose);
	}

	[Test]
	public void An_issuer_certificate_with_a_non_issuer_subject_is_refused()
	{
		var issuer = TestPki.IssueIssuer(subjectKind: "creator");
		var certificate = TestPki.IssueCertificate(issuer: issuer);

		AssertRefused(Verify(certificate, issuer), SigningError.CertificateWrongPurpose);
	}

	[Test]
	public void An_issuer_certificate_on_schema_version_1_is_refused()
	{
		var issuer = TestPki.IssueIssuer(schemaVersion: 1);

		AssertRefused(Verify(issuer, null, SigningCertificateChain.IssuerKeyUsage), SigningError.CertificateMalformed);
	}

	[Test]
	public void A_second_intermediate_level_is_refused()
	{
		var firstIssuer = TestPki.IssueIssuer();
		var secondIssuer = TestPki.IssueIssuer(notBefore: DateTimeOffset.UtcNow.AddDays(-1),
			notAfter: DateTimeOffset.UtcNow.AddYears(1),
			issuer: firstIssuer);
		var certificate = TestPki.IssueCertificate(notBefore: DateTimeOffset.UtcNow.AddHours(-1),
			notAfter: DateTimeOffset.UtcNow.AddMonths(6),
			issuer: secondIssuer);

		Assert.Multiple(() =>
		{
			Assert.That(Verify(certificate, secondIssuer).Success, Is.False);
			Assert.That(Verify(secondIssuer, firstIssuer, SigningCertificateChain.IssuerKeyUsage).Error,
				Is.EqualTo(SigningError.CertificateWrongPurpose));
			Assert.That(Verify(secondIssuer, null, SigningCertificateChain.IssuerKeyUsage).Error,
				Is.EqualTo(SigningError.CertificateWrongPurpose));
		});
	}

	[Test]
	public void A_certificate_valid_beyond_its_issuer_is_refused()
	{
		var issuer = TestPki.IssueIssuer(notAfter: DateTimeOffset.UtcNow.AddMonths(6));
		var certificate = TestPki.IssueCertificate(notAfter: DateTimeOffset.UtcNow.AddYears(1), issuer: issuer);

		AssertRefused(Verify(certificate, issuer), SigningError.CertificateOutlivesIssuer);
	}

	[Test]
	public void A_certificate_valid_before_its_issuer_is_refused()
	{
		var issuer = TestPki.IssueIssuer(notBefore: DateTimeOffset.UtcNow.AddDays(-1));
		var certificate = TestPki.IssueCertificate(notBefore: DateTimeOffset.UtcNow.AddDays(-2), issuer: issuer);

		AssertRefused(Verify(certificate, issuer), SigningError.CertificateOutlivesIssuer);
	}

	[Test]
	public void An_expired_issuer_is_refused_at_the_signature_instant()
	{
		var issuer = TestPki.IssueIssuer(notBefore: DateTimeOffset.UtcNow.AddYears(-2),
			notAfter: DateTimeOffset.UtcNow.AddYears(-1));
		var certificate = TestPki.IssueCertificate(notBefore: DateTimeOffset.UtcNow.AddYears(-2).AddDays(1),
			notAfter: DateTimeOffset.UtcNow.AddYears(-1).AddDays(-1),
			issuer: issuer);
		var trusted = Verify(certificate, issuer).TrustedCertificate!;

		var failure = SigningCertificateChain.EnsureValidAt(trusted, DateTimeOffset.UtcNow);

		Assert.Multiple(() =>
		{
			Assert.That(failure!.Error, Is.EqualTo(SigningError.CertificateExpired));
			Assert.That(failure.Message, Does.Contain("issuer"));
			Assert.That(SigningCertificateChain.EnsureValidAt(trusted, DateTimeOffset.UtcNow.AddYears(-1).AddDays(-2)),
				Is.Null);
		});
	}

	[Test]
	public void A_certificate_naming_an_issuer_without_issuer_material_is_refused()
	{
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(issuer: issuer);

		AssertRefused(Verify(certificate, null), SigningError.CertificateIssuerMissing);
	}

	[Test]
	public void The_root_only_overload_reports_a_missing_issuer_for_an_issuer_signed_certificate()
	{
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(issuer: issuer);

		var result = SigningCertificateChain.Verify(certificate.CertificateBytes,
			certificate.CertificateSignatureBytes,
			TestPki.Root.PublicKey,
			SigningCertificateChain.PackageKeyUsage);

		AssertRefused(result, SigningError.CertificateIssuerMissing);
	}

	[Test]
	public void Issuer_material_for_a_root_signed_certificate_is_refused()
	{
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate();

		AssertRefused(Verify(certificate, issuer), SigningError.CertificateIssuerMismatch);
	}

	[Test]
	public void An_issuer_certificate_without_its_signature_is_refused()
	{
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(issuer: issuer);

		var result = SigningCertificateChain.Verify(certificate.CertificateBytes,
			certificate.CertificateSignatureBytes,
			issuer.CertificateBytes,
			null,
			TestPki.Root.PublicKey,
			SigningCertificateChain.PackageKeyUsage);

		AssertRefused(result, SigningError.CertificateIssuerMissing);
	}

	[Test]
	public void A_certificate_naming_a_different_issuer_than_the_one_that_signed_it_is_refused()
	{
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(issuer: issuer, issuerIdOverride: "cert_" + Guid.NewGuid().ToString("N"));

		AssertRefused(Verify(certificate, issuer), SigningError.CertificateIssuerMismatch);
	}

	[Test]
	public void A_certificate_signed_by_another_issuer_than_the_supplied_one_is_refused()
	{
		var issuer = TestPki.IssueIssuer();
		var otherIssuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(issuer: otherIssuer);

		AssertRefused(Verify(certificate, issuer), SigningError.CertificateUntrusted);
	}

	[Test]
	public void A_certificate_anchored_to_a_different_root_key_id_than_its_issuer_is_refused()
	{
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(rootKeyId: "root_other0001", issuer: issuer);

		AssertRefused(Verify(certificate, issuer), SigningError.CertificateIssuerMismatch);
	}

	[Test]
	public void A_schema_version_1_certificate_naming_an_issuer_is_refused()
	{
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(schemaVersion: 1, issuer: issuer);

		AssertRefused(Verify(certificate, issuer), SigningError.CertificateMalformed);
	}

	[Test]
	public void A_certificate_naming_an_empty_issuer_is_refused()
	{
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(issuer: issuer, issuerIdOverride: "");

		Assert.Multiple(() =>
		{
			AssertRefused(Verify(certificate, issuer), SigningError.CertificateMalformed);
			AssertRefused(Verify(certificate, null), SigningError.CertificateIssuerMissing);
		});
	}

	[Test]
	public void An_issuer_signed_certificate_cannot_itself_act_as_an_issuer()
	{
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(subjectKind: "issuer",
			keyUsage: [SigningCertificateChain.IssuerKeyUsage],
			issuer: issuer);

		AssertRefused(Verify(certificate, issuer, SigningCertificateChain.IssuerKeyUsage),
			SigningError.CertificateWrongPurpose);
	}
}
