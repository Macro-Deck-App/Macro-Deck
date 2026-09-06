using System.IO.Compression;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.Keys;
using MacroDeck.Signing.Packages;
using MacroDeck.Signing.TestSupport;

namespace MacroDeck.Signing.Tests.UnitTests;

/// <summary>
/// <see cref="PackageVerifier.VerifyExtractedAsync" />: an installed package tree must reach the same
/// verdict as the archive it came from, and must keep reaching it when someone edits the tree afterwards.
/// The plugin directory is user-writable, so "it verified at install time" is not an answer.
/// </summary>
[TestFixture]
internal sealed class PackageVerifierExtractedTests
{
	private static readonly PluginManifestReader _manifestReader = new();

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

	[Test]
	public async Task An_extracted_signed_plugin_verifies()
	{
		var extracted = await CreateExtractedSignedPluginAsync();

		var result = await VerifyExtractedAsync(extracted);

		Assert.That(result.Success, Is.True);
	}

	/// <summary>The verdict must not depend on which side of extraction it is taken from - a package that
	/// verifies as an archive and fails once installed (or the reverse) would be a hole, not a nuance.</summary>
	[Test]
	public async Task An_extracted_tree_reaches_the_same_verdict_as_its_archive()
	{
		var (archivePath, _) = await CreateSignedArchiveAsync();
		var extracted = Path.Combine(_directory, "extracted");
		ZipFile.ExtractToDirectory(archivePath, extracted);

		var archiveResult = await PackageVerifier.VerifyAsync(archivePath, _manifestReader, TestPki.Root.PublicKey);
		var extractedResult = await VerifyExtractedAsync(extracted);

		Assert.Multiple(() =>
		{
			Assert.That(archiveResult.Success, Is.True);
			Assert.That(extractedResult.Success, Is.True);
			Assert.That(extractedResult.CertificateId, Is.EqualTo(archiveResult.CertificateId));
		});
	}

	[Test]
	public async Task Rewriting_an_installed_payload_file_fails_on_its_digest()
	{
		var extracted = await CreateExtractedSignedPluginAsync();
		// Same length as the original "binary-content", so this exercises the digest check rather than the
		// size check that runs before it.
		await File.WriteAllTextAsync(Path.Combine(extracted, "app"), "tampered-conte");

		var result = await VerifyExtractedAsync(extracted);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.FileDigestMismatch));
		});
	}

	[Test]
	public async Task Truncating_an_installed_payload_file_fails_on_its_size()
	{
		var extracted = await CreateExtractedSignedPluginAsync();
		await File.WriteAllTextAsync(Path.Combine(extracted, "app"), "short");

		var result = await VerifyExtractedAsync(extracted);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.FileSizeMismatch));
		});
	}

	[Test]
	public async Task Deleting_an_installed_payload_file_fails()
	{
		var extracted = await CreateExtractedSignedPluginAsync();
		File.Delete(Path.Combine(extracted, "app"));

		var result = await VerifyExtractedAsync(extracted);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.DeclaredFileMissing));
		});
	}

	[Test]
	public async Task Adding_a_file_to_an_installed_tree_fails()
	{
		var extracted = await CreateExtractedSignedPluginAsync();
		await File.WriteAllTextAsync(Path.Combine(extracted, "smuggled"), "payload");

		var result = await VerifyExtractedAsync(extracted);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.UndeclaredFile));
		});
	}

	/// <summary>Stripping the certificate must not read as "this package was never signed" - the manifest
	/// still claims a signature, so the package is broken, not unsigned.</summary>
	[Test]
	public async Task Stripping_the_certificate_from_an_installed_tree_does_not_read_as_unsigned()
	{
		var extracted = await CreateExtractedSignedPluginAsync();
		File.Delete(Path.Combine(extracted, "certificate.sig"));

		var result = await VerifyExtractedAsync(extracted);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.CertificateUnreadable));
			Assert.That(result.Error, Is.Not.EqualTo(SigningError.SignatureMissing));
		});
	}

	[Test]
	public async Task An_extracted_tree_signed_under_another_root_is_untrusted()
	{
		var extracted = await CreateExtractedSignedPluginAsync();

		var result = await PackageVerifier.VerifyExtractedAsync(extracted,
			SignablePackageFormat.Plugin,
			_manifestReader,
			TestPki.OtherRoot.PublicKey);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.CertificateUntrusted));
		});
	}

	/// <summary>A symlink planted in the installed tree must be reported, not followed: following it would
	/// let verification read bytes other than the ones that will be loaded.</summary>
	[Test]
	[Platform(Exclude = "Win")]
	public async Task A_symlink_in_an_installed_tree_is_rejected_rather_than_followed()
	{
		var extracted = await CreateExtractedSignedPluginAsync();
		var target = Path.Combine(_directory, "outside-target");
		await File.WriteAllTextAsync(target, "binary-content");

		File.Delete(Path.Combine(extracted, "app"));
		File.CreateSymbolicLink(Path.Combine(extracted, "app"), target);

		var result = await VerifyExtractedAsync(extracted);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.UnsafeEntry));
		});
	}

	[Test]
	public async Task An_unsigned_extracted_tree_reports_a_missing_signature()
	{
		var packagePath = Path.Combine(_directory, "unsigned.macroDeckPlugin");
		PackageArchiveFixtures.CreatePluginArchive(packagePath,
			"com.example.test",
			"1.0.0",
			[new PackageArchiveFixtures.DeclaredFile("app", "binary-content")]);
		var extracted = Path.Combine(_directory, "extracted");
		ZipFile.ExtractToDirectory(packagePath, extracted);

		var result = await VerifyExtractedAsync(extracted);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.SignatureMissing));
		});
	}

	private static Task<PackageVerifyResult> VerifyExtractedAsync(string rootDirectory) =>
		PackageVerifier.VerifyExtractedAsync(rootDirectory,
			SignablePackageFormat.Plugin,
			_manifestReader,
			TestPki.Root.PublicKey);

	private async Task<string> CreateExtractedSignedPluginAsync()
	{
		var (archivePath, _) = await CreateSignedArchiveAsync();
		var extracted = Path.Combine(_directory, Guid.NewGuid().ToString("N"));
		ZipFile.ExtractToDirectory(archivePath, extracted);
		return extracted;
	}

	private async Task<(string SignedPath, TestPki.IssuedCertificate Issued)> CreateSignedArchiveAsync()
	{
		var packagePath = Path.Combine(_directory, $"{Guid.NewGuid():N}.macroDeckPlugin");
		var outputPath = Path.Combine(_directory, $"{Guid.NewGuid():N}.signed.macroDeckPlugin");
		PackageArchiveFixtures.CreatePluginArchive(packagePath,
			"com.example.test",
			"1.0.0",
			[new PackageArchiveFixtures.DeclaredFile("app", "binary-content")]);

		var issued = TestPki.IssueCertificate();
		var trusted = TestPki.VerifyChain(issued, SigningCertificateChain.PackageKeyUsage);
		using var signer = SigningMaterial.Create(issued.PrivateKey, trusted.Certificate).Material!;

		var result = await PackageSigner.SignAsync(packagePath,
			outputPath,
			signer,
			issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			_manifestReader);
		Assert.That(result.Success, Is.True, "test setup: signing must succeed");

		return (outputPath, issued);
	}
}
