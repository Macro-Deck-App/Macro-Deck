using System.IO.Compression;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.Keys;
using MacroDeck.Signing.Packages;
using MacroDeck.Signing.TestSupport;

namespace MacroDeck.Signing.Tests.UnitTests;

[TestFixture]
internal sealed class IssuerSignedPackageTests
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
			Directory.Delete(_directory, recursive: true);
		}
	}

	private static SigningMaterial CreateSigner(TestPki.IssuedCertificate issued, TestPki.IssuedCertificate? issuer)
	{
		var chain = SigningCertificateChain.Verify(issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			issuer?.CertificateBytes,
			issuer?.CertificateSignatureBytes,
			TestPki.Root.PublicKey,
			SigningCertificateChain.PackageKeyUsage);
		Assert.That(chain.Success, Is.True, chain.Message);
		var material = SigningMaterial.Create(issued.PrivateKey, chain.TrustedCertificate!.Certificate);
		Assert.That(material.Success, Is.True);
		return material.Material!;
	}

	private string CreatePackage(string extension)
	{
		var path = Path.Combine(_directory, "package" + extension);
		return extension switch
		{
			".macroDeckPlugin" => PackageArchiveFixtures.CreatePluginArchive(path,
				"com.example.issuer",
				"1.0.0",
				[new PackageArchiveFixtures.DeclaredFile("app", "binary-content")]),
			".macroDeckIconPack" => PackageArchiveFixtures.CreateIconPackArchive(path,
				"com.example.icons",
				"Test Icons",
				[new PackageArchiveFixtures.DeclaredFile("icon.png", "png-bytes")]),
			_ => PackageArchiveFixtures.CreatePortableArchive(path,
				"Profile",
				1,
				[new PackageArchiveFixtures.DeclaredFile("profile.json", "{\"buttons\":[]}")])
		};
	}

	private async Task<(string Output, TestPki.IssuedCertificate Issuer)> SignWithIssuer(string extension,
		TestPki.IssuedCertificate? issuer = null,
		TestPki.IssuedCertificate? certificate = null)
	{
		issuer ??= TestPki.IssueIssuer();
		certificate ??= TestPki.IssueCertificate(issuer: issuer);
		var output = Path.Combine(_directory, "signed" + extension);
		using var signer = CreateSigner(certificate, issuer);

		var result = await PackageSigner.SignAsync(CreatePackage(extension),
			output,
			signer,
			certificate.CertificateBytes,
			certificate.CertificateSignatureBytes,
			issuer.CertificateBytes,
			issuer.CertificateSignatureBytes,
			_manifestReader);
		Assert.That(result.Success, Is.True, result.Message);
		return (output, issuer);
	}

	private static Task<PackageVerifyResult> Verify(string path) =>
		PackageVerifier.VerifyAsync(path, _manifestReader, TestPki.Root.PublicKey);

	[TestCase(".macroDeckPlugin")]
	[TestCase(".macroDeckIconPack")]
	[TestCase(".macroDeckProfile")]
	public async Task A_package_signed_through_an_issuer_verifies_and_names_the_issuer(string extension)
	{
		var (output, issuer) = await SignWithIssuer(extension);

		var result = await Verify(output);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.Message);
			Assert.That(result.IssuerCertificateId, Is.EqualTo(issuer.CertificateId));
			Assert.That(PackageArchiveFixtures.ReadEntryText(output, PluginArtifactFiles.IssuerCertificateFileName),
				Is.EqualTo(System.Text.Encoding.UTF8.GetString(issuer.CertificateBytes)));
		});
	}

	[Test]
	public async Task An_extracted_plugin_signed_through_an_issuer_verifies()
	{
		var (output, issuer) = await SignWithIssuer(".macroDeckPlugin");
		var extracted = Path.Combine(_directory, "extracted");
		ZipFile.ExtractToDirectory(output, extracted);

		var result = await PackageVerifier.VerifyExtractedAsync(extracted,
			SignablePackageFormat.Plugin,
			_manifestReader,
			TestPki.Root.PublicKey);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.Message);
			Assert.That(result.IssuerCertificateId, Is.EqualTo(issuer.CertificateId));
		});
	}

	[Test]
	public async Task A_root_signed_package_reports_no_issuer()
	{
		var certificate = TestPki.IssueCertificate();
		var output = Path.Combine(_directory, "signed.macroDeckPlugin");
		using var signer = CreateSigner(certificate, null);
		await PackageSigner.SignAsync(CreatePackage(".macroDeckPlugin"),
			output,
			signer,
			certificate.CertificateBytes,
			certificate.CertificateSignatureBytes,
			_manifestReader);

		var result = await Verify(output);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.Message);
			Assert.That(result.IssuerCertificateId, Is.Null);
		});
	}

	[TestCase(PluginArtifactFiles.IssuerCertificateFileName)]
	[TestCase(PluginArtifactFiles.IssuerCertificateSignatureFileName)]
	public async Task A_package_missing_an_issuer_file_it_needs_is_refused(string removed)
	{
		var (output, _) = await SignWithIssuer(".macroDeckPlugin");
		PackageArchiveFixtures.RemoveEntry(output, removed);

		var result = await Verify(output);

		Assert.That(result.Error, Is.EqualTo(SigningError.CertificateIssuerMissing), result.Message);
	}

	[Test]
	public async Task A_package_missing_both_issuer_files_it_needs_is_refused()
	{
		var (output, _) = await SignWithIssuer(".macroDeckPlugin");
		PackageArchiveFixtures.RemoveEntry(output, PluginArtifactFiles.IssuerCertificateFileName);
		PackageArchiveFixtures.RemoveEntry(output, PluginArtifactFiles.IssuerCertificateSignatureFileName);

		var result = await Verify(output);

		Assert.That(result.Error, Is.EqualTo(SigningError.CertificateIssuerMissing), result.Message);
	}

	[Test]
	public async Task A_root_signed_package_carrying_undeclared_issuer_files_is_refused()
	{
		var certificate = TestPki.IssueCertificate();
		var issuer = TestPki.IssueIssuer();
		var output = Path.Combine(_directory, "signed.macroDeckPlugin");
		using var signer = CreateSigner(certificate, null);
		await PackageSigner.SignAsync(CreatePackage(".macroDeckPlugin"),
			output,
			signer,
			certificate.CertificateBytes,
			certificate.CertificateSignatureBytes,
			_manifestReader);
		PackageArchiveFixtures.AddEntry(output,
			PluginArtifactFiles.IssuerCertificateFileName,
			System.Text.Encoding.UTF8.GetString(issuer.CertificateBytes));
		PackageArchiveFixtures.AddEntry(output,
			PluginArtifactFiles.IssuerCertificateSignatureFileName,
			System.Text.Encoding.UTF8.GetString(issuer.CertificateSignatureBytes));

		var result = await Verify(output);

		Assert.That(result.Error, Is.EqualTo(SigningError.UndeclaredFile), result.Message);
	}

	[Test]
	public async Task A_root_signed_package_that_declares_its_own_issuer_json_still_verifies()
	{
		var package = PackageArchiveFixtures.CreatePluginArchive(Path.Combine(_directory, "package.macroDeckPlugin"),
			"com.example.issuer",
			"1.0.0",
			[
				new PackageArchiveFixtures.DeclaredFile("app", "binary-content"),
				new PackageArchiveFixtures.DeclaredFile(PluginArtifactFiles.IssuerCertificateFileName, "{\"theme\":1}")
			]);
		var certificate = TestPki.IssueCertificate();
		var output = Path.Combine(_directory, "signed.macroDeckPlugin");
		using var signer = CreateSigner(certificate, null);

		var signResult = await PackageSigner.SignAsync(package,
			output,
			signer,
			certificate.CertificateBytes,
			certificate.CertificateSignatureBytes,
			_manifestReader);
		var verifyResult = await Verify(output);

		Assert.Multiple(() =>
		{
			Assert.That(signResult.Success, Is.True, signResult.Message);
			Assert.That(verifyResult.Success, Is.True, verifyResult.Message);
			Assert.That(PackageArchiveFixtures.ReadEntryText(output, PluginArtifactFiles.IssuerCertificateFileName),
				Is.EqualTo("{\"theme\":1}"));
		});
	}

	[Test]
	public async Task An_issuer_signed_package_cannot_declare_the_issuer_file_names_as_content()
	{
		var package = PackageArchiveFixtures.CreatePluginArchive(Path.Combine(_directory, "package.macroDeckPlugin"),
			"com.example.issuer",
			"1.0.0",
			[
				new PackageArchiveFixtures.DeclaredFile("app", "binary-content"),
				new PackageArchiveFixtures.DeclaredFile(PluginArtifactFiles.IssuerCertificateFileName, "{}")
			]);
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(issuer: issuer);
		var output = Path.Combine(_directory, "signed.macroDeckPlugin");
		using var signer = CreateSigner(certificate, issuer);

		var result = await PackageSigner.SignAsync(package,
			output,
			signer,
			certificate.CertificateBytes,
			certificate.CertificateSignatureBytes,
			issuer.CertificateBytes,
			issuer.CertificateSignatureBytes,
			_manifestReader);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(SigningError.ManifestMalformed), result.Message);
			Assert.That(File.Exists(output), Is.False);
		});
	}

	[Test]
	public async Task An_extra_undeclared_file_next_to_the_issuer_files_is_refused()
	{
		var (output, _) = await SignWithIssuer(".macroDeckPlugin");
		PackageArchiveFixtures.AddEntry(output, "issuer.extra", "unexpected");

		var result = await Verify(output);

		Assert.That(result.Error, Is.EqualTo(SigningError.UndeclaredFile), result.Message);
	}

	[Test]
	public async Task A_package_signed_after_its_issuer_expired_is_refused()
	{
		var issuer = TestPki.IssueIssuer(notBefore: DateTimeOffset.UtcNow.AddYears(-2),
			notAfter: DateTimeOffset.UtcNow.AddYears(-1));
		var certificate = TestPki.IssueCertificate(notBefore: DateTimeOffset.UtcNow.AddYears(-2).AddDays(1),
			notAfter: DateTimeOffset.UtcNow.AddYears(-1).AddDays(-1),
			issuer: issuer);
		var (output, _) = await SignWithIssuer(".macroDeckPlugin", issuer, certificate);

		var result = await Verify(output);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(SigningError.CertificateExpired));
			Assert.That(result.Message, Does.Contain("issuer"));
		});
	}

	[Test]
	public async Task Signing_with_an_issuer_signed_certificate_but_no_issuer_material_writes_nothing()
	{
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(issuer: issuer);
		var output = Path.Combine(_directory, "signed.macroDeckPlugin");
		using var signer = CreateSigner(certificate, issuer);

		var result = await PackageSigner.SignAsync(CreatePackage(".macroDeckPlugin"),
			output,
			signer,
			certificate.CertificateBytes,
			certificate.CertificateSignatureBytes,
			_manifestReader);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(SigningError.CertificateIssuerMissing));
			Assert.That(File.Exists(output), Is.False);
		});
	}

	[Test]
	public async Task Signing_with_a_different_issuer_than_the_certificate_names_writes_nothing()
	{
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(issuer: issuer);
		var otherIssuer = TestPki.IssueIssuer();
		var output = Path.Combine(_directory, "signed.macroDeckPlugin");
		using var signer = CreateSigner(certificate, issuer);

		var result = await PackageSigner.SignAsync(CreatePackage(".macroDeckPlugin"),
			output,
			signer,
			certificate.CertificateBytes,
			certificate.CertificateSignatureBytes,
			otherIssuer.CertificateBytes,
			otherIssuer.CertificateSignatureBytes,
			_manifestReader);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(SigningError.CertificateIssuerMismatch));
			Assert.That(File.Exists(output), Is.False);
		});
	}

	[Test]
	public async Task Signing_a_root_signed_certificate_with_issuer_material_writes_nothing()
	{
		var certificate = TestPki.IssueCertificate();
		var issuer = TestPki.IssueIssuer();
		var output = Path.Combine(_directory, "signed.macroDeckPlugin");
		using var signer = CreateSigner(certificate, null);

		var result = await PackageSigner.SignAsync(CreatePackage(".macroDeckPlugin"),
			output,
			signer,
			certificate.CertificateBytes,
			certificate.CertificateSignatureBytes,
			issuer.CertificateBytes,
			issuer.CertificateSignatureBytes,
			_manifestReader);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(SigningError.CertificateIssuerMismatch));
			Assert.That(File.Exists(output), Is.False);
		});
	}

	[Test]
	public async Task Stale_issuer_files_in_the_source_are_replaced_by_the_supplied_issuer()
	{
		var package = CreatePackage(".macroDeckPlugin");
		PackageArchiveFixtures.AddEntry(package, PluginArtifactFiles.IssuerCertificateFileName, "stale");
		PackageArchiveFixtures.AddEntry(package, PluginArtifactFiles.IssuerCertificateSignatureFileName, "stale");
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(issuer: issuer);
		var output = Path.Combine(_directory, "signed.macroDeckPlugin");
		using var signer = CreateSigner(certificate, issuer);

		var signResult = await PackageSigner.SignAsync(package,
			output,
			signer,
			certificate.CertificateBytes,
			certificate.CertificateSignatureBytes,
			issuer.CertificateBytes,
			issuer.CertificateSignatureBytes,
			_manifestReader);
		var verifyResult = await Verify(output);

		using var archive = ZipFile.OpenRead(output);
		Assert.Multiple(() =>
		{
			Assert.That(signResult.Success, Is.True, signResult.Message);
			Assert.That(verifyResult.Success, Is.True, verifyResult.Message);
			Assert.That(archive.Entries.Count(entry => entry.FullName == PluginArtifactFiles.IssuerCertificateFileName),
				Is.EqualTo(1));
		});
	}
}
