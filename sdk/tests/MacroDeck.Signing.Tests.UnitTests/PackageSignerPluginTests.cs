using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.Keys;
using MacroDeck.Signing.Packages;
using MacroDeck.Signing.TestSupport;

namespace MacroDeck.Signing.Tests.UnitTests;

/// <summary>
/// <see cref="PackageSigner" /> for a <c>.macroDeckPlugin</c> archive: what it refuses to sign, and what a
/// successfully signed artifact looks like.
/// </summary>
[TestFixture]
internal sealed class PackageSignerPluginTests
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

	private static SigningMaterial CreateSigner(out TestPki.IssuedCertificate issued)
	{
		issued = TestPki.IssueCertificate();
		var trusted = TestPki.VerifyChain(issued, SigningCertificateChain.PackageKeyUsage);
		var materialResult = SigningMaterial.Create(issued.PrivateKey, trusted.Certificate);
		Assert.That(materialResult.Success, Is.True);
		return materialResult.Material!;
	}

	private string PackagePath(string name = "plugin.macroDeckPlugin") => Path.Combine(_directory, name);

	private string OutputPath(string name = "plugin.signed.macroDeckPlugin") => Path.Combine(_directory, name);

	[Test]
	public async Task
		Signing_a_valid_plugin_embeds_a_signature_that_verifies_against_the_independently_computed_digest()
	{
		var files = new[] { new PackageArchiveFixtures.DeclaredFile("app", "binary-content") };
		var packagePath = PackageArchiveFixtures.CreatePluginArchive(PackagePath(), "com.example.test", "1.0.0", files);
		var outputPath = OutputPath();

		using var signer = CreateSigner(out var issued);
		var result = await PackageSigner.SignAsync(packagePath,
			outputPath,
			signer,
			issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			_manifestReader);

		Assert.That(result.Success, Is.True);

		var signedManifestJson = PackageArchiveFixtures.ReadEntryText(outputPath, "manifest.json");
		var readResult
			= _manifestReader.ReadFromJson(signedManifestJson, versionDirectory: null, "com.example.test", "1.0.0");
		Assert.That(readResult.Success, Is.True);

		var signature = readResult.Manifest!.Signature!;
		var independentDigest = PluginArtifactDigest.Compute(readResult.Manifest! with { Signature = null });
		var verificationKey = Ed25519VerificationKey.TryImport(issued.PublicKey);

		Assert.That(verificationKey!.Verify(independentDigest, Convert.FromBase64String(signature.Value)), Is.True);
	}

	[Test]
	public async Task Signing_refuses_a_declared_file_whose_hash_is_one_hex_digit_off()
	{
		var files = new[] { new PackageArchiveFixtures.DeclaredFile("app", "binary-content") };
		var packagePath = PackageArchiveFixtures.CreatePluginArchive(PackagePath(), "com.example.test", "1.0.0", files);

		// Flip the last hex digit of the declared hash while leaving the archive entry's real bytes alone.
		var manifestJson = PackageArchiveFixtures.ReadEntryText(packagePath, "manifest.json");
		var tampered = FlipLastHexDigitOfFirstSha256(manifestJson);
		PackageArchiveFixtures.WriteEntryText(packagePath, "manifest.json", tampered);

		using var signer = CreateSigner(out var issued);
		var result = await PackageSigner.SignAsync(packagePath,
			OutputPath(),
			signer,
			issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			_manifestReader);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.FileDigestMismatch));
		});
	}

	[Test]
	public async Task Signing_refuses_a_declared_file_whose_size_is_off_by_one()
	{
		var files = new[] { new PackageArchiveFixtures.DeclaredFile("app", "binary-content") };
		var packagePath = PackageArchiveFixtures.CreatePluginArchive(PackagePath(), "com.example.test", "1.0.0", files);

		var manifestJson = PackageArchiveFixtures.ReadEntryText(packagePath, "manifest.json");
		var declaredSize = System.Text.Encoding.UTF8.GetByteCount("binary-content");
		var tampered = manifestJson.Replace($"\"size\":{declaredSize}", $"\"size\":{declaredSize + 1}");
		Assert.That(tampered, Is.Not.EqualTo(manifestJson));
		PackageArchiveFixtures.WriteEntryText(packagePath, "manifest.json", tampered);

		using var signer = CreateSigner(out var issued);
		var result = await PackageSigner.SignAsync(packagePath,
			OutputPath(),
			signer,
			issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			_manifestReader);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.FileSizeMismatch));
		});
	}

	[Test]
	public async Task Signing_refuses_a_declared_file_missing_from_the_archive()
	{
		var declared = new[] { new PackageArchiveFixtures.DeclaredFile("app", "binary-content") };
		var packagePath = PackageArchiveFixtures.CreatePluginArchive(PackagePath(),
			"com.example.test",
			"1.0.0",
			declared,
			entrypointExecutable: "app");

		// Remove the archive entry the manifest still declares.
		using (var stream = new FileStream(packagePath, FileMode.Open, FileAccess.ReadWrite))
		using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update))
		{
			archive.GetEntry("app")!.Delete();
		}

		using var signer = CreateSigner(out var issued);
		var result = await PackageSigner.SignAsync(packagePath,
			OutputPath(),
			signer,
			issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			_manifestReader);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.DeclaredFileMissing));
		});
	}

	[Test]
	public async Task Signing_refuses_an_undeclared_payload_entry()
	{
		var declared = new[] { new PackageArchiveFixtures.DeclaredFile("app", "binary-content") };
		var packagePath
			= PackageArchiveFixtures.CreatePluginArchive(PackagePath(), "com.example.test", "1.0.0", declared);
		PackageArchiveFixtures.AddEntry(packagePath, "extra-file.txt", "not declared anywhere");

		using var signer = CreateSigner(out var issued);
		var result = await PackageSigner.SignAsync(packagePath,
			OutputPath(),
			signer,
			issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			_manifestReader);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.UndeclaredFile));
		});
	}

	[Test]
	public async Task Signing_refuses_an_archive_with_no_declared_files_at_all()
	{
		var packagePath = PackageArchiveFixtures.CreatePluginArchive(PackagePath(),
			"com.example.test",
			"1.0.0",
			[],
			declareFiles: false);

		using var signer = CreateSigner(out var issued);
		var result = await PackageSigner.SignAsync(packagePath,
			OutputPath(),
			signer,
			issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			_manifestReader);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(SigningError.ManifestMalformed));
		});
	}

	[Test]
	public async Task Signing_an_already_signed_artifact_is_refused_and_leaves_the_input_byte_identical()
	{
		var declared = new[] { new PackageArchiveFixtures.DeclaredFile("app", "binary-content") };
		var packagePath
			= PackageArchiveFixtures.CreatePluginArchive(PackagePath(), "com.example.test", "1.0.0", declared);
		var firstOutput = OutputPath("plugin.signed-once.macroDeckPlugin");

		using (var signer = CreateSigner(out var issued))
		{
			var firstResult = await PackageSigner.SignAsync(packagePath,
				firstOutput,
				signer,
				issued.CertificateBytes,
				issued.CertificateSignatureBytes,
				_manifestReader);
			Assert.That(firstResult.Success, Is.True);
		}

		var beforeBytes = await File.ReadAllBytesAsync(firstOutput);
		var secondOutput = OutputPath("plugin.signed-twice.macroDeckPlugin");

		using var secondSigner = CreateSigner(out var secondIssued);
		var secondResult = await PackageSigner.SignAsync(firstOutput,
			secondOutput,
			secondSigner,
			secondIssued.CertificateBytes,
			secondIssued.CertificateSignatureBytes,
			_manifestReader);

		var afterBytes = await File.ReadAllBytesAsync(firstOutput);

		Assert.Multiple(() =>
		{
			Assert.That(secondResult.Success, Is.False);
			Assert.That(secondResult.Error, Is.EqualTo(SigningError.AlreadySigned));
			Assert.That(afterBytes, Is.EqualTo(beforeBytes));
			Assert.That(File.Exists(secondOutput), Is.False);
		});
	}

	[Test]
	public async Task Signing_refuses_a_private_key_that_does_not_belong_to_the_certificate_before_writing_anything()
	{
		var declared = new[] { new PackageArchiveFixtures.DeclaredFile("app", "binary-content") };
		var packagePath
			= PackageArchiveFixtures.CreatePluginArchive(PackagePath(), "com.example.test", "1.0.0", declared);
		var outputPath = OutputPath();

		var issued = TestPki.IssueCertificate();
		var trusted = TestPki.VerifyChain(issued, SigningCertificateChain.PackageKeyUsage);
		var (unrelatedPrivateKey, _) = Ed25519KeyPair.Create();

		var materialResult = SigningMaterial.Create(unrelatedPrivateKey, trusted.Certificate);

		Assert.Multiple(() =>
		{
			Assert.That(materialResult.Success, Is.False);
			Assert.That(materialResult.Error, Is.EqualTo(SigningError.PrivateKeyDoesNotMatchCertificate));
			Assert.That(File.Exists(outputPath), Is.False);
		});
	}

	[Test]
	public async Task
		The_signed_artifact_carries_certificate_material_at_its_root_excluded_from_files_and_the_digest_is_unchanged()
	{
		var declared = new[] { new PackageArchiveFixtures.DeclaredFile("app", "binary-content") };
		var packagePath
			= PackageArchiveFixtures.CreatePluginArchive(PackagePath(), "com.example.test", "1.0.0", declared);
		var outputPath = OutputPath();

		var unsignedManifestJson = PackageArchiveFixtures.ReadEntryText(packagePath, "manifest.json");
		var unsignedReadResult
			= _manifestReader.ReadFromJson(unsignedManifestJson, versionDirectory: null, "com.example.test", "1.0.0");
		var unsignedDigest = PluginArtifactDigest.Compute(unsignedReadResult.Manifest!);

		using var signer = CreateSigner(out var issued);
		var signResult = await PackageSigner.SignAsync(packagePath,
			outputPath,
			signer,
			issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			_manifestReader);
		Assert.That(signResult.Success, Is.True);

		var signedManifestJson = PackageArchiveFixtures.ReadEntryText(outputPath, "manifest.json");
		var signedReadResult
			= _manifestReader.ReadFromJson(signedManifestJson, versionDirectory: null, "com.example.test", "1.0.0");
		var signedDigest = PluginArtifactDigest.Compute(signedReadResult.Manifest! with { Signature = null });

		using var stream = new FileStream(outputPath, FileMode.Open, FileAccess.Read);
		using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);

		Assert.Multiple(() =>
		{
			Assert.That(archive.GetEntry(PluginArtifactFiles.CertificateFileName), Is.Not.Null);
			Assert.That(archive.GetEntry(PluginArtifactFiles.CertificateSignatureFileName), Is.Not.Null);
			Assert.That(signedReadResult.Manifest!.Files!.Select(f => f.Path),
				Has.None.EqualTo(PluginArtifactFiles.CertificateFileName));
			Assert.That(signedReadResult.Manifest!.Files!.Select(f => f.Path),
				Has.None.EqualTo(PluginArtifactFiles.CertificateSignatureFileName));
			Assert.That(signedDigest, Is.EqualTo(unsignedDigest));
		});
	}

	/// <summary>The signer must not round-trip through the typed manifest model when rewriting it - if it
	/// did, an unknown property a newer manifest declares would be silently dropped, breaking forward
	/// compatibility with an SDK a creator built against after this one.</summary>
	[Test]
	public async Task An_unknown_top_level_manifest_property_survives_signing()
	{
		var declared = new[] { new PackageArchiveFixtures.DeclaredFile("app", "binary-content") };
		var packagePath = PackageArchiveFixtures.CreatePluginArchive(PackagePath(),
			"com.example.test",
			"1.0.0",
			declared,
			extraManifestProperties: new Dictionary<string, object> { ["futureField"] = "future-value" });
		var outputPath = OutputPath();

		using var signer = CreateSigner(out var issued);
		var result = await PackageSigner.SignAsync(packagePath,
			outputPath,
			signer,
			issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			_manifestReader);
		Assert.That(result.Success, Is.True);

		var signedManifestJson = PackageArchiveFixtures.ReadEntryText(outputPath, "manifest.json");
		Assert.That(signedManifestJson, Does.Contain("future-value"));
	}

	[Test]
	public async Task The_re_emitted_manifest_stays_under_the_max_manifest_bytes_limit()
	{
		var declared = new[] { new PackageArchiveFixtures.DeclaredFile("app", "binary-content") };
		var packagePath
			= PackageArchiveFixtures.CreatePluginArchive(PackagePath(), "com.example.test", "1.0.0", declared);
		var outputPath = OutputPath();

		using var signer = CreateSigner(out var issued);
		var result = await PackageSigner.SignAsync(packagePath,
			outputPath,
			signer,
			issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			_manifestReader);
		Assert.That(result.Success, Is.True);

		var signedManifestJson = PackageArchiveFixtures.ReadEntryText(outputPath, "manifest.json");
		Assert.That(System.Text.Encoding.UTF8.GetByteCount(signedManifestJson),
			Is.LessThanOrEqualTo(PluginArtifactLimits.MaxManifestBytes));
	}

	private static string FlipLastHexDigitOfFirstSha256(string manifestJson)
	{
		var index = manifestJson.IndexOf("sha256:", StringComparison.Ordinal);
		var lastDigitIndex = index + "sha256:".Length + 63;
		var digit = manifestJson[lastDigitIndex];
		var replacement = digit == '0' ? '1' : '0';
		return manifestJson[..lastDigitIndex] + replacement + manifestJson[(lastDigitIndex + 1)..];
	}
}
