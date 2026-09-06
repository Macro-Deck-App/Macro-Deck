using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.Keys;
using MacroDeck.Signing.Packages;
using MacroDeck.Signing.TestSupport;

namespace MacroDeck.Signing.Tests.UnitTests;

/// <summary>
/// <see cref="PackageVerifier" /> for a <c>.macroDeckPlugin</c> archive: the happy path, then every way a
/// validly signed artifact can be tampered with afterwards, one field at a time.
/// </summary>
[TestFixture]
internal sealed class PackageVerifierPluginTests
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

	private async Task<(string SignedPath, TestPki.IssuedCertificate Issued)> CreateSignedArchiveAsync(
		string? id = null,
		IReadOnlyList<PackageArchiveFixtures.DeclaredFile>? files = null,
		TestPki.IssuedCertificate? issuedOverride = null)
	{
		id ??= "com.example.test";
		files ??= [new PackageArchiveFixtures.DeclaredFile("app", "binary-content")];
		var packagePath = Path.Combine(_directory, $"{Guid.NewGuid():N}.macroDeckPlugin");
		var outputPath = Path.Combine(_directory, $"{Guid.NewGuid():N}.signed.macroDeckPlugin");
		PackageArchiveFixtures.CreatePluginArchive(packagePath, id, "1.0.0", files);

		var issued = issuedOverride ?? TestPki.IssueCertificate();
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

	private static Task<PackageVerifyResult> VerifyAsync(string path,
		(byte[] PrivateKey, byte[] PublicKey)? root = null,
		CancellationToken cancellationToken = default) =>
		PackageVerifier.VerifyAsync(path, _manifestReader, (root ?? TestPki.Root).PublicKey, cancellationToken);

	[Test]
	public async Task A_validly_signed_plugin_verifies()
	{
		var (path, _) = await CreateSignedArchiveAsync();

		var result = await VerifyAsync(path);

		Assert.That(result.Success, Is.True);
	}

	[Test]
	public async Task A_changed_payload_byte_fails_naming_the_file_digest()
	{
		var (path, _) = await CreateSignedArchiveAsync();
		// Same length as the original "binary-content" (14 bytes) - only the content changes, so this
		// exercises the hash check specifically rather than the size check that runs before it.
		PackageArchiveFixtures.ReplaceEntry(path, "app", "binary-Content");

		var result = await VerifyAsync(path);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.FileDigestMismatch));
		});
	}

	/// <summary>The discriminating case: a payload byte changed <em>and</em> its <c>files[]</c> entry
	/// updated to match, so the declared-files check alone would pass. The signature must still catch it -
	/// it covers the digest, not just "the manifest agrees with the archive".</summary>
	[Test]
	public async Task A_payload_byte_changed_with_its_declared_hash_updated_to_match_still_fails_on_the_signature()
	{
		var (path, _) = await CreateSignedArchiveAsync();
		var tamperedContent = "tampered-content";
		PackageArchiveFixtures.ReplaceEntry(path, "app", tamperedContent);

		var manifestJson = PackageArchiveFixtures.ReadEntryText(path, "manifest.json");
		var manifest = JsonNode.Parse(manifestJson)!.AsObject();
		var fileEntry = (JsonObject)manifest["files"]![0]!;
		fileEntry["sha256"] = "sha256:" + PackageArchiveFixtures.Sha256Hex(tamperedContent);
		fileEntry["size"] = Encoding.UTF8.GetByteCount(tamperedContent);
		PackageArchiveFixtures.WriteEntryText(path, "manifest.json", manifest.ToJsonString());

		var result = await VerifyAsync(path);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.SignatureInvalid));
		});
	}

	[Test]
	public async Task An_entrypoint_executable_repointed_at_another_declared_file_fails_verification()
	{
		var files = new[]
		{
			new PackageArchiveFixtures.DeclaredFile("app", "app-content"),
			new PackageArchiveFixtures.DeclaredFile("other", "other-content")
		};
		var (path, _) = await CreateSignedArchiveAsync(files: files);

		RewriteManifest(path,
			manifest => { ((JsonObject)manifest["entrypoints"]!["linux-x64"]!)["executable"] = "other"; });

		var result = await VerifyAsync(path);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.SignatureInvalid));
		});
	}

	[Test]
	public async Task An_extra_entrypoint_argument_fails_verification()
	{
		var (path, _) = await CreateSignedArchiveAsync();

		RewriteManifest(path,
			manifest =>
			{
				((JsonObject)manifest["entrypoints"]!["linux-x64"]!)["arguments"] = new JsonArray("--evil");
			});

		var result = await VerifyAsync(path);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.SignatureInvalid));
		});
	}

	[Test]
	public async Task An_added_permission_fails_verification()
	{
		var (path, _) = await CreateSignedArchiveAsync();

		RewriteManifest(path, manifest => { manifest["permissions"] = new JsonArray("host:scripts"); });

		var result = await VerifyAsync(path);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.SignatureInvalid));
		});
	}

	[Test]
	public async Task A_changed_runtime_kind_or_dotnetVersion_fails_verification()
	{
		var files = new[] { new PackageArchiveFixtures.DeclaredFile("App.dll", "content") };
		var (path, _) = await CreateSignedArchiveAsync(files: files);

		// The signed archive's entrypoint has no runtime block (self-contained). Turning it into a
		// framework-dependent one changes both Kind and DotnetVersion at once - the digest must move.
		RewriteManifest(path,
			manifest =>
			{
				var entrypoint = (JsonObject)manifest["entrypoints"]!["linux-x64"]!;
				entrypoint["executable"] = "App.dll";
				entrypoint["runtime"] = new JsonObject { ["kind"] = "FrameworkDependent", ["dotnetVersion"] = "10.0" };
			});

		var result = await VerifyAsync(path);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.SignatureInvalid));
		});
	}

	[Test]
	public async Task A_signature_keyId_naming_a_different_certificate_than_the_one_the_artifact_carries_is_rejected()
	{
		var (path, issued) = await CreateSignedArchiveAsync();
		var otherCertificate = TestPki.IssueCertificate();

		RewriteManifest(path,
			manifest => { ((JsonObject)manifest["signature"]!)["keyId"] = otherCertificate.CertificateId; });

		var result = await VerifyAsync(path);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.SignatureKeyIdMismatch));
		});
		_ = issued;
	}

	[Test]
	public async Task A_signature_made_by_an_unrelated_key_is_rejected()
	{
		var (path, issued) = await CreateSignedArchiveAsync();
		var (unrelatedPrivateKey, _) = Ed25519KeyPair.Create();

		var manifestJson = PackageArchiveFixtures.ReadEntryText(path, "manifest.json");
		var manifest = JsonNode.Parse(manifestJson)!.AsObject();
		var digest = PluginArtifactDigest.Compute(
			_manifestReader.ReadFromJson(manifestJson, null, "com.example.test", "1.0.0").Manifest! with
			{
				Signature = null
			});
		var forgedSignature = SignRaw(unrelatedPrivateKey, digest);
		((JsonObject)manifest["signature"]!)["value"] = Convert.ToBase64String(forgedSignature);
		PackageArchiveFixtures.WriteEntryText(path, "manifest.json", manifest.ToJsonString());

		var result = await VerifyAsync(path);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.SignatureInvalid));
		});
		_ = issued;
	}

	/// <summary>"Not signed" and "invalid" are different verdicts: one says nothing was ever claimed, the
	/// other says a claim did not hold up.</summary>
	[Test]
	public async Task An_unsigned_artifact_fails_with_a_not_signed_verdict_distinct_from_invalid()
	{
		var packagePath = Path.Combine(_directory, "unsigned.macroDeckPlugin");
		var files = new[] { new PackageArchiveFixtures.DeclaredFile("app", "content") };
		PackageArchiveFixtures.CreatePluginArchive(packagePath, "com.example.test", "1.0.0", files);

		var result = await VerifyAsync(packagePath);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.SignatureMissing));
			Assert.That(result.Error, Is.Not.EqualTo(SigningError.SignatureInvalid));
		});
	}

	[Test]
	public async Task A_certificate_with_the_wrong_purpose_fails_verification_not_only_at_signing_time()
	{
		var issued = TestPki.IssueCertificate(keyUsage: [SigningCertificateChain.RegistryKeyUsage]);
		// SignAsync itself would refuse this certificate before ever writing anything (proven in the signer
		// tests), so to exercise verification's own check we hand-assemble a "signed" archive: sign the
		// digest directly and embed the certificate and signature manually.
		var files = new[] { new PackageArchiveFixtures.DeclaredFile("app", "content") };
		var packagePath = Path.Combine(_directory, "wrong-purpose.macroDeckPlugin");
		PackageArchiveFixtures.CreatePluginArchive(packagePath, "com.example.test", "1.0.0", files);

		EmbedHandRolledSignature(packagePath, issued);

		var result = await VerifyAsync(packagePath);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.CertificateWrongPurpose));
		});
	}

	[Test]
	public async Task A_certificate_expired_at_the_signature_signedAt_fails_verification()
	{
		var notBefore = DateTimeOffset.UtcNow.AddDays(-30);
		var notAfter = DateTimeOffset.UtcNow.AddDays(-10);
		var issued = TestPki.IssueCertificate(notBefore: notBefore, notAfter: notAfter);

		var files = new[] { new PackageArchiveFixtures.DeclaredFile("app", "content") };
		var packagePath = Path.Combine(_directory, "expired.macroDeckPlugin");
		PackageArchiveFixtures.CreatePluginArchive(packagePath, "com.example.test", "1.0.0", files);

		// signedAt after notAfter - a certificate whose window had already closed by the time the signature
		// claims to have been made.
		EmbedHandRolledSignature(packagePath, issued, signedAt: notAfter.AddDays(1));

		var result = await VerifyAsync(packagePath);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.CertificateExpired));
		});
	}

	[Test]
	public async Task A_certificate_expired_by_now_but_valid_at_signedAt_still_verifies()
	{
		var notBefore = DateTimeOffset.UtcNow.AddDays(-30);
		var notAfter = DateTimeOffset.UtcNow.AddDays(-10);
		var issued = TestPki.IssueCertificate(notBefore: notBefore, notAfter: notAfter);

		var files = new[] { new PackageArchiveFixtures.DeclaredFile("app", "content") };
		var packagePath = Path.Combine(_directory, "expired-by-now.macroDeckPlugin");
		PackageArchiveFixtures.CreatePluginArchive(packagePath, "com.example.test", "1.0.0", files);

		// signedAt falls inside the certificate's window even though that window has since closed, by
		// DateTimeOffset.UtcNow - this is the converse of the expiry test above, and it is what pins
		// validity being evaluated at signedAt rather than at verification time: switching the check to
		// UtcNow would fail this artifact even though it was validly signed.
		EmbedHandRolledSignature(packagePath, issued, signedAt: notBefore.AddDays(5));

		var result = await VerifyAsync(packagePath);

		Assert.That(result.Success, Is.True, result.Message);
	}

	[Test]
	public void A_cancelled_token_surfaces_as_cancellation_not_as_success_or_an_internal_error()
	{
		using var cts = new CancellationTokenSource();
		cts.Cancel();
		var packagePath = Path.Combine(_directory, "cancel.macroDeckPlugin");
		var files = new[] { new PackageArchiveFixtures.DeclaredFile("app", "content") };
		PackageArchiveFixtures.CreatePluginArchive(packagePath, "com.example.test", "1.0.0", files);

		Assert.CatchAsync<OperationCanceledException>(async () =>
			await VerifyAsync(packagePath, cancellationToken: cts.Token));
	}

	/// <summary>Rewrites the archive's manifest.json in place by parsing, mutating, and writing back - used
	/// by every "tamper with a validly signed artifact" test above so the signature and certificate material
	/// stay exactly as signed while only the manifest content changes.</summary>
	private static void RewriteManifest(string archivePath, Action<JsonObject> mutate)
	{
		var manifestJson = PackageArchiveFixtures.ReadEntryText(archivePath, "manifest.json");
		var manifest = JsonNode.Parse(manifestJson)!.AsObject();
		mutate(manifest);
		PackageArchiveFixtures.WriteEntryText(archivePath, "manifest.json", manifest.ToJsonString());
	}

	/// <summary>Signs the archive's current digest directly with <paramref name="issued" />'s key and embeds
	/// the result - bypassing <see cref="PackageSigner" />, which would refuse a certificate like this one
	/// before ever writing anything. Only for tests that need to reach verification's own certificate checks.</summary>
	private static void EmbedHandRolledSignature(string archivePath,
		TestPki.IssuedCertificate issued,
		DateTimeOffset? signedAt = null)
	{
		var manifestJson = PackageArchiveFixtures.ReadEntryText(archivePath, "manifest.json");
		var readResult = _manifestReader.ReadFromJson(manifestJson, null, "com.example.test", "1.0.0");
		var digest = PluginArtifactDigest.Compute(readResult.Manifest!);
		var signatureBytes = SignRaw(issued.PrivateKey, digest);

		var manifest = JsonNode.Parse(manifestJson)!.AsObject();
		manifest["signature"] = new JsonObject
		{
			["algorithm"] = "ed25519",
			["keyId"] = issued.CertificateId,
			["value"] = Convert.ToBase64String(signatureBytes),
			["signedAt"] = (signedAt ?? DateTimeOffset.UtcNow).ToString("O")
		};
		PackageArchiveFixtures.WriteEntryText(archivePath, "manifest.json", manifest.ToJsonString());

		using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.ReadWrite);
		using var archive = new ZipArchive(stream, ZipArchiveMode.Update);
		WriteBinaryEntry(archive, "certificate.json", issued.CertificateBytes);
		WriteBinaryEntry(archive, "certificate.sig", issued.CertificateSignatureBytes);
	}

	private static void WriteBinaryEntry(ZipArchive archive, string name, byte[] content)
	{
		archive.GetEntry(name)?.Delete();
		var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
		using var entryStream = entry.Open();
		entryStream.Write(content);
	}

	private static byte[] SignRaw(byte[] rawPrivateKey, byte[] payload)
	{
		using var key = NSec.Cryptography.Key.Import(NSec.Cryptography.SignatureAlgorithm.Ed25519,
			rawPrivateKey,
			NSec.Cryptography.KeyBlobFormat.RawPrivateKey);
		return NSec.Cryptography.SignatureAlgorithm.Ed25519.Sign(key, payload);
	}
}
