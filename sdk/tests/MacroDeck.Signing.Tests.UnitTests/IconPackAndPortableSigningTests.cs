using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.Keys;
using MacroDeck.Signing.Packages;
using MacroDeck.Signing.TestSupport;

namespace MacroDeck.Signing.Tests.UnitTests;

/// <summary>
/// The same sign/verify round trip as <see cref="PackageSignerPluginTests" />/<see cref="PackageVerifierPluginTests" />,
/// but for <see cref="SignablePackageFormat.IconPack" /> and one portable format
/// (<see cref="SignablePackageFormat.Profile" />) - the two formats besides plugin that
/// <see cref="PackageSigner" />/<see cref="PackageVerifier" /> handle through the untyped
/// <see cref="System.Text.Json.Nodes.JsonObject" /> path rather than a typed manifest model.
/// </summary>
[TestFixture]
internal sealed class IconPackAndPortableSigningTests
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

	private static SigningMaterial CreateSigner(out TestPki.IssuedCertificate issued)
	{
		issued = TestPki.IssueCertificate();
		var trusted = TestPki.VerifyChain(issued, SigningCertificateChain.PackageKeyUsage);
		var materialResult = SigningMaterial.Create(issued.PrivateKey, trusted.Certificate);
		Assert.That(materialResult.Success, Is.True);
		return materialResult.Material!;
	}

	[Test]
	public async Task An_icon_pack_signs_and_then_verifies()
	{
		var files = new[] { new PackageArchiveFixtures.DeclaredFile("icon.png", "png-bytes") };
		var packagePath = Path.Combine(_directory, "pack.macroDeckIconPack");
		var outputPath = Path.Combine(_directory, "pack.signed.macroDeckIconPack");
		PackageArchiveFixtures.CreateIconPackArchive(packagePath, "com.example.icons", "Test Icons", files);

		using var signer = CreateSigner(out var issued);
		var signResult = await PackageSigner.SignAsync(packagePath,
			outputPath,
			signer,
			issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			_manifestReader);
		Assert.That(signResult.Success, Is.True);

		var verifyResult = await PackageVerifier.VerifyAsync(outputPath, _manifestReader, TestPki.Root.PublicKey);

		Assert.Multiple(() =>
		{
			Assert.That(verifyResult.Success, Is.True);
			Assert.That(verifyResult.Format, Is.EqualTo(SignablePackageFormat.IconPack));
		});
	}

	[Test]
	public async Task An_icon_pack_with_a_tampered_declared_file_fails_verification()
	{
		var files = new[] { new PackageArchiveFixtures.DeclaredFile("icon.png", "png-bytes") };
		var packagePath = Path.Combine(_directory, "pack.macroDeckIconPack");
		var outputPath = Path.Combine(_directory, "pack.signed.macroDeckIconPack");
		PackageArchiveFixtures.CreateIconPackArchive(packagePath, "com.example.icons", "Test Icons", files);

		using var signer = CreateSigner(out var issued);
		await PackageSigner.SignAsync(packagePath,
			outputPath,
			signer,
			issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			_manifestReader);

		// Same length as the original "png-bytes" (9 bytes) - exercises the hash check, not the size check.
		PackageArchiveFixtures.ReplaceEntry(outputPath, "icon.png", "evl-bytes");

		var verifyResult = await PackageVerifier.VerifyAsync(outputPath, _manifestReader, TestPki.Root.PublicKey);

		Assert.Multiple(() =>
		{
			Assert.That(verifyResult.Success, Is.False);
			Assert.That(verifyResult.Error, Is.EqualTo(SigningError.FileDigestMismatch));
		});
	}

	[Test]
	public async Task A_profile_package_signs_and_then_verifies()
	{
		var files = new[] { new PackageArchiveFixtures.DeclaredFile("profile.json", "{\"buttons\":[]}") };
		var packagePath = Path.Combine(_directory, "profile.macroDeckProfile");
		var outputPath = Path.Combine(_directory, "profile.signed.macroDeckProfile");
		PackageArchiveFixtures.CreatePortableArchive(packagePath, "Profile", 1, files);

		using var signer = CreateSigner(out var issued);
		var signResult = await PackageSigner.SignAsync(packagePath,
			outputPath,
			signer,
			issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			_manifestReader);
		Assert.That(signResult.Success, Is.True);

		var verifyResult = await PackageVerifier.VerifyAsync(outputPath, _manifestReader, TestPki.Root.PublicKey);

		Assert.Multiple(() =>
		{
			Assert.That(verifyResult.Success, Is.True);
			Assert.That(verifyResult.Format, Is.EqualTo(SignablePackageFormat.Profile));
		});
	}

	[Test]
	public async Task A_profile_package_with_a_tampered_declared_file_fails_verification()
	{
		var files = new[] { new PackageArchiveFixtures.DeclaredFile("profile.json", "{\"buttons\":[]}") };
		var packagePath = Path.Combine(_directory, "profile.macroDeckProfile");
		var outputPath = Path.Combine(_directory, "profile.signed.macroDeckProfile");
		PackageArchiveFixtures.CreatePortableArchive(packagePath, "Profile", 1, files);

		using var signer = CreateSigner(out var issued);
		await PackageSigner.SignAsync(packagePath,
			outputPath,
			signer,
			issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			_manifestReader);

		// Same length as the original {"buttons":[]} (14 bytes) - exercises the hash check, not the size check.
		PackageArchiveFixtures.ReplaceEntry(outputPath, "profile.json", "{\"Buttons\":[]}");

		var verifyResult = await PackageVerifier.VerifyAsync(outputPath, _manifestReader, TestPki.Root.PublicKey);

		Assert.Multiple(() =>
		{
			Assert.That(verifyResult.Success, Is.False);
			Assert.That(verifyResult.Error, Is.EqualTo(SigningError.FileDigestMismatch));
		});
	}
}
