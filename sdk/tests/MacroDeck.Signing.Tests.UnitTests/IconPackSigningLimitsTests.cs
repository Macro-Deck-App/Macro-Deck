using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.IconPacks;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.Keys;
using MacroDeck.Signing.Packages;
using MacroDeck.Signing.TestSupport;

namespace MacroDeck.Signing.Tests.UnitTests;

[TestFixture]
internal sealed class IconPackSigningLimitsTests
{
	private static readonly IPluginManifestReader _manifestReader = new PluginManifestReader();

	private string _directory = null!;

	[SetUp]
	public void SetUp()
	{
		_directory = Directory.CreateTempSubdirectory("macrodeck-icon-pack-limits-").FullName;
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
	public async Task An_icon_pack_whose_pack_json_exceeds_the_plugin_manifest_bound_signs_and_verifies()
	{
		var package = CreateIconPack("large.macroDeckIconPack", files: 2, padding: 12 * 1024 * 1024);

		var signed = await Sign(package, withIssuer: false);
		var verified = await Verify(signed.Output);

		Assert.Multiple(() =>
		{
			Assert.That(signed.Result.Success, Is.True, signed.Result.Message);
			Assert.That(verified.Success, Is.True, verified.Message);
		});
	}

	[Test]
	public async Task An_icon_pack_whose_pack_json_exceeds_the_icon_pack_bound_is_refused_by_signer_and_verifier()
	{
		var package = CreateIconPack("huge.macroDeckIconPack", files: 1, padding: IconPackArchiveLimits.MaxManifestBytes);

		var signed = await Sign(package, withIssuer: false);
		var verified = await Verify(package);

		Assert.Multiple(() =>
		{
			Assert.That(signed.Result.Error, Is.EqualTo(SigningError.ManifestTooLarge));
			Assert.That(File.Exists(signed.Output), Is.False);
			Assert.That(verified.Error, Is.EqualTo(SigningError.ManifestTooLarge));
		});
	}

	[Test]
	public async Task A_plugin_manifest_above_the_plugin_bound_is_still_refused_by_signer_and_verifier()
	{
		var package = PackageArchiveFixtures.CreatePluginArchive(Path.Combine(_directory, "plugin.macroDeckPlugin"),
			"com.example.large",
			"1.0.0",
			[new PackageArchiveFixtures.DeclaredFile("app", "binary")],
			extraManifestProperties: new Dictionary<string, object>
			{
				["description"] = new string('x', PluginArtifactLimits.MaxManifestBytes)
			});

		var signed = await Sign(package, withIssuer: false);
		var verified = await Verify(package);

		Assert.Multiple(() =>
		{
			Assert.That(signed.Result.Error, Is.EqualTo(SigningError.ManifestTooLarge));
			Assert.That(verified.Error, Is.EqualTo(SigningError.ManifestTooLarge));
		});
	}

	[TestCase(false, IconPackArchiveLimits.MaxEntries - 2)]
	[TestCase(true, IconPackArchiveLimits.MaxEntries - 4)]
	public async Task An_icon_pack_that_would_exceed_the_entry_bound_once_signed_is_refused(bool withIssuer, int files)
	{
		var package = CreateIconPack("full.macroDeckIconPack", files, padding: 0);

		var signed = await Sign(package, withIssuer);

		Assert.Multiple(() =>
		{
			Assert.That(signed.Result.Error, Is.EqualTo(SigningError.TooManyEntries));
			Assert.That(File.Exists(signed.Output), Is.False);
		});
	}

	[Test]
	public async Task An_icon_pack_at_the_unsigned_entry_bound_signs_with_issuer_material_to_exactly_the_entry_bound()
	{
		var package = CreateIconPack("bound.macroDeckIconPack", IconPackArchiveLimits.MaxUnsignedEntries - 1, padding: 0);

		var signed = await Sign(package, withIssuer: true);
		var verified = await Verify(signed.Output);

		Assert.Multiple(() =>
		{
			Assert.That(signed.Result.Success, Is.True, signed.Result.Message);
			Assert.That(EntryCount(signed.Output), Is.EqualTo(IconPackArchiveLimits.MaxEntries));
			Assert.That(verified.Success, Is.True, verified.Message);
		});
	}

	[Test]
	public async Task Stale_signature_material_in_the_source_is_not_counted_twice()
	{
		var package = CreateIconPack("stale.macroDeckIconPack",
			IconPackArchiveLimits.MaxEntries - 3,
			padding: 0,
			staleCertificate: true);

		var signed = await Sign(package, withIssuer: false);

		Assert.Multiple(() =>
		{
			Assert.That(signed.Result.Success, Is.True, signed.Result.Message);
			Assert.That(EntryCount(signed.Output), Is.EqualTo(IconPackArchiveLimits.MaxEntries));
		});
	}

	[Test]
	public async Task The_verifier_refuses_an_icon_pack_archive_above_the_entry_bound()
	{
		var package = CreateIconPack("over.macroDeckIconPack", IconPackArchiveLimits.MaxEntries, padding: 0);

		var verified = await Verify(package);

		Assert.That(verified.Error, Is.EqualTo(SigningError.TooManyEntries));
	}

	[Test]
	public async Task The_verifier_refuses_an_extracted_icon_pack_above_the_entry_bound()
	{
		var root = Directory.CreateDirectory(Path.Combine(_directory, "extracted")).FullName;
		await File.WriteAllTextAsync(Path.Combine(root, "pack.json"), "{}");
		var icons = Directory.CreateDirectory(Path.Combine(root, "icons")).FullName;
		for (var index = 0; index < IconPackArchiveLimits.MaxEntries; index++)
		{
			await File.WriteAllBytesAsync(Path.Combine(icons, $"{index}.webp"), []);
		}

		var verified = await PackageVerifier.VerifyExtractedAsync(root,
			SignablePackageFormat.IconPack,
			_manifestReader,
			TestPki.Root.PublicKey);

		Assert.That(verified.Error, Is.EqualTo(SigningError.TooManyEntries));
	}

	private string CreateIconPack(string fileName, int files, int padding, bool staleCertificate = false)
	{
		var declared = Enumerable.Range(0, files)
			.Select(index => new PackageArchiveFixtures.DeclaredFile($"icons/{index}/master.webp", $"image-{index}"))
			.ToList();
		var manifest = new JsonObject
		{
			["id"] = "com.example.icons",
			["name"] = "Test Icons",
			["description"] = new string('x', padding),
			["files"] = new JsonArray(declared.Select(file => (JsonNode)new JsonObject
			{
				["path"] = file.Path,
				["sha256"] = "sha256:" + PackageArchiveFixtures.Sha256Hex(file.Content),
				["size"] = Encoding.UTF8.GetByteCount(file.Content)
			}).ToArray())
		};

		var path = Path.Combine(_directory, fileName);
		using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
		Write(archive, "pack.json", manifest.ToJsonString());
		foreach (var file in declared)
		{
			Write(archive, file.Path, file.Content);
		}

		if (staleCertificate)
		{
			Write(archive, PluginArtifactFiles.CertificateFileName, "{}");
			Write(archive, PluginArtifactFiles.CertificateSignatureFileName, "stale");
		}

		return path;
	}

	private static void Write(ZipArchive archive, string name, string content)
	{
		using var stream = archive.CreateEntry(name, CompressionLevel.Fastest).Open();
		stream.Write(Encoding.UTF8.GetBytes(content));
	}

	private async Task<(PackageSignResult Result, string Output)> Sign(string package, bool withIssuer)
	{
		var issuer = withIssuer ? TestPki.IssueIssuer() : null;
		var certificate = TestPki.IssueCertificate(issuer: issuer);
		var chain = SigningCertificateChain.Verify(certificate.CertificateBytes,
			certificate.CertificateSignatureBytes,
			issuer?.CertificateBytes,
			issuer?.CertificateSignatureBytes,
			TestPki.Root.PublicKey,
			SigningCertificateChain.PackageKeyUsage);
		Assert.That(chain.Success, Is.True, chain.Message);
		using var signer = SigningMaterial.Create(certificate.PrivateKey, chain.TrustedCertificate!.Certificate).Material!;

		var output = Path.Combine(_directory, "signed-" + Path.GetFileName(package));
		var result = await PackageSigner.SignAsync(package,
			output,
			signer,
			certificate.CertificateBytes,
			certificate.CertificateSignatureBytes,
			issuer?.CertificateBytes,
			issuer?.CertificateSignatureBytes,
			_manifestReader);
		return (result, output);
	}

	private static Task<PackageVerifyResult> Verify(string path) =>
		PackageVerifier.VerifyAsync(path, _manifestReader, TestPki.Root.PublicKey);

	private static int EntryCount(string path)
	{
		using var archive = ZipFile.OpenRead(path);
		return archive.Entries.Count;
	}
}
