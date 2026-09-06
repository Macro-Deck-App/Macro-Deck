using System.Security.Cryptography;
using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.Registry;
using MacroDeck.Signing.TestSupport;

namespace MacroDeck.Signing.Tests.UnitTests;

/// <summary>
/// <see cref="RegistryManifestVerifier" />: a detached signature over <c>registry-manifest.json</c>'s exact
/// bytes, made by a <c>registry</c>-usage certificate - the one signing flow in this library that is never
/// embedded in a ZIP.
/// </summary>
[TestFixture]
internal sealed class RegistryManifestVerifierTests
{
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

	private (string ManifestPath, string SignaturePath) WriteSignedRegistry(TestPki.IssuedCertificate issued)
	{
		var payloadPath = Path.Combine(_directory, "index.json");
		var payloadContent = "{\"plugins\":[]}";
		File.WriteAllText(payloadPath, payloadContent);
		var sha256 = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payloadContent)));

		var manifestJson = $$"""
							 {"schemaVersion":1,"sequence":1,"files":[{"path":"index.json","sha256":"{{sha256}}","size":{{payloadContent.Length}}}]}
							 """;
		var manifestPath = Path.Combine(_directory, "registry-manifest.json");
		File.WriteAllText(manifestPath, manifestJson);

		var manifestBytes = File.ReadAllBytes(manifestPath);
		var signatureBytes = TestPki.SignCertificateBytesAsBase64Text(issued.PrivateKey, manifestBytes);
		// The registry signature document stores the raw signature bytes base64-encoded as a JSON string
		// value (unlike the certificate .sig, which is a bare base64 text file) - decode what
		// SignCertificateBytesAsBase64Text produced and re-encode through the typed document.
		var rawSignature = Convert.FromBase64String(System.Text.Encoding.UTF8.GetString(signatureBytes));

		var document = new RegistrySignatureDocument
		{
			KeyId = issued.CertificateId,
			Value = Convert.ToBase64String(rawSignature),
			SignedAt = DateTimeOffset.UtcNow
		};

		var signaturePath = Path.Combine(_directory, "registry-signature.json");
		File.WriteAllBytes(signaturePath, SigningJson.Serialize(document));

		return (manifestPath, signaturePath);
	}

	[Test]
	public async Task A_registry_manifest_signed_by_a_registry_usage_certificate_verifies()
	{
		var issued = TestPki.IssueCertificate(subjectKind: "service",
			keyUsage: [SigningCertificateChain.RegistryKeyUsage]);
		var (manifestPath, signaturePath) = WriteSignedRegistry(issued);

		var result = await RegistryManifestVerifier.VerifyAsync(manifestPath,
			signaturePath,
			issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			TestPki.Root.PublicKey);

		Assert.That(result.Success, Is.True);
	}

	[Test]
	public async Task A_package_usage_certificate_is_refused_for_registry_verification()
	{
		var issued = TestPki.IssueCertificate(subjectKind: "creator",
			keyUsage: [SigningCertificateChain.PackageKeyUsage]);
		var (manifestPath, signaturePath) = WriteSignedRegistry(issued);

		var result = await RegistryManifestVerifier.VerifyAsync(manifestPath,
			signaturePath,
			issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			TestPki.Root.PublicKey);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.CertificateWrongPurpose));
		});
	}

	[Test]
	public async Task A_registry_usage_certificate_is_refused_for_package_signing()
	{
		var issued = TestPki.IssueCertificate(subjectKind: "service",
			keyUsage: [SigningCertificateChain.RegistryKeyUsage]);

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

	[Test]
	public async Task A_tampered_registry_manifest_fails_verification()
	{
		var issued = TestPki.IssueCertificate(subjectKind: "service",
			keyUsage: [SigningCertificateChain.RegistryKeyUsage]);
		var (manifestPath, signaturePath) = WriteSignedRegistry(issued);

		var original = File.ReadAllText(manifestPath);
		File.WriteAllText(manifestPath, original.Replace("\"sequence\":1", "\"sequence\":2"));

		var result = await RegistryManifestVerifier.VerifyAsync(manifestPath,
			signaturePath,
			issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			TestPki.Root.PublicKey);

		Assert.That(result.Success, Is.False);
	}
}
