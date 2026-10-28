using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.Keys;
using MacroDeck.Signing.Packages;
using MacroDeck.Signing.TestSupport;

namespace MacroDeck.Signing.Tests.UnitTests;

[TestFixture]
internal sealed class PackageVerifierSubjectTests
{
	private static readonly IPluginManifestReader _manifestReader = new PluginManifestReader();

	private string _directory = null!;

	[SetUp]
	public void SetUp()
	{
		_directory = Directory.CreateTempSubdirectory("macrodeck-signing-tests-").FullName;
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_directory))
		{
			Directory.Delete(_directory, true);
		}
	}

	private async Task<string> SignAsync(TestPki.IssuedCertificate certificate)
	{
		var chain = SigningCertificateChain.Verify(certificate.CertificateBytes,
			certificate.CertificateSignatureBytes,
			TestPki.Root.PublicKey,
			SigningCertificateChain.PackageKeyUsage);
		Assert.That(chain.Success, Is.True, chain.Message);
		var material = SigningMaterial.Create(certificate.PrivateKey, chain.TrustedCertificate!.Certificate);
		using var signer = material.Material!;

		var package = PackageArchiveFixtures.CreatePluginArchive(Path.Combine(_directory, "package.macroDeckPlugin"),
			"com.example.subject",
			"1.0.0",
			[new PackageArchiveFixtures.DeclaredFile("app", "binary-content")]);
		var output = Path.Combine(_directory, "signed.macroDeckPlugin");
		var result = await PackageSigner.SignAsync(package,
			output,
			signer,
			certificate.CertificateBytes,
			certificate.CertificateSignatureBytes,
			_manifestReader);
		Assert.That(result.Success, Is.True, result.Message);
		return output;
	}

	[Test]
	public async Task A_verified_package_names_the_subject_its_certificate_was_issued_to()
	{
		var signed = await SignAsync(TestPki.IssueCertificate("organization", subjectId: "org-42"));

		var result = await PackageVerifier.VerifyAsync(signed, _manifestReader, TestPki.Root.PublicKey);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.Message);
			Assert.That(result.Subject?.Kind, Is.EqualTo("organization"));
			Assert.That(result.Subject?.Id, Is.EqualTo("org-42"));
		});
	}

	[Test]
	public async Task A_package_that_does_not_verify_names_no_subject()
	{
		var signed = await SignAsync(TestPki.IssueCertificate());

		var result = await PackageVerifier.VerifyAsync(signed, _manifestReader, TestPki.OtherRoot.PublicKey);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Subject, Is.Null);
		});
	}

	[Test]
	public void A_result_built_the_way_an_older_consumer_builds_it_is_unchanged()
	{
		var ok = PackageVerifyResult.Ok(SignablePackageFormat.Plugin, "cert_1");
		var failed = PackageVerifyResult.Fail(SigningError.SignatureInvalid, "bad");

		Assert.Multiple(() =>
		{
			Assert.That(ok.Success, Is.True);
			Assert.That(ok.CertificateId, Is.EqualTo("cert_1"));
			Assert.That(ok.IssuerCertificateId, Is.Null);
			Assert.That(ok.Subject, Is.Null);
			Assert.That(failed.Success, Is.False);
			Assert.That(failed.Error, Is.EqualTo(SigningError.SignatureInvalid));
			Assert.That(failed.Subject, Is.Null);
		});
	}
}
