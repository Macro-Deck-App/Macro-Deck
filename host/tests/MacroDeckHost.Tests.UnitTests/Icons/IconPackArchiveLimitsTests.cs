using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Packaging.IconPacks;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.Keys;
using MacroDeck.Signing.Packages;
using MacroDeck.Signing.TestSupport;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Packaging;
using MacroDeckHost.Application.Persistence.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Infrastructure.Persistence;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class IconPackArchiveLimitsTests
{
	private static readonly IPluginManifestReader _manifestReader = new PluginManifestReader();

	private IconTestHarness _harness = null!;
	private string _directory = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new IconTestHarness();
		_directory = Directory.CreateTempSubdirectory("macrodeck-icon-pack-limits-").FullName;
	}

	[TearDown]
	public void TearDown()
	{
		_harness.Dispose();
		if (Directory.Exists(_directory))
		{
			Directory.Delete(_directory, recursive: true);
		}
	}

	[Test]
	public async Task A_pack_above_the_former_ten_thousand_entry_bound_imports()
	{
		var archive = UnsignedPack(14_847, out _);

		var result = await Restore(archive);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.That(_harness.Cache.GetIconsByPackId(result.Data!.Id).Single().Name, Is.EqualTo("star"));
	}

	[Test]
	public async Task A_pack_above_the_entry_bound_is_refused_on_import()
	{
		var archive = UnsignedPack(IconPackArchiveLimits.MaxEntries + 1, out _);
		var packsBefore = _harness.Cache.GetAllPacks().Count;

		var result = await Restore(archive);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(IconError.InvalidArchive));
			Assert.That(_harness.Cache.GetAllPacks(), Has.Count.EqualTo(packsBefore));
		});
	}

	[Test]
	public async Task A_pack_json_above_the_manifest_bound_is_refused_on_import()
	{
		var archive = UnsignedPack(3, out _, description: new string('x', IconPackArchiveLimits.MaxManifestBytes));

		var result = await Restore(archive);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(IconError.InvalidArchive));
			Assert.That(result.ErrorMessage, Does.Contain("pack.json"));
		});
	}

	[Test]
	public async Task A_pack_at_the_unsigned_entry_bound_signs_with_issuer_material_and_imports()
	{
		var package = Path.Combine(_directory, "bound.macroDeckIconPack");
		await File.WriteAllBytesAsync(package, UnsignedPack(IconPackArchiveLimits.MaxUnsignedEntries, out _));

		var signed = await Sign(package);
		var result = await Restore(await File.ReadAllBytesAsync(signed));

		Assert.Multiple(() =>
		{
			Assert.That(EntryCount(signed), Is.EqualTo(IconPackArchiveLimits.MaxEntries));
			Assert.That(result.Success, Is.True, result.ErrorMessage);
		});
	}

	[Test]
	public async Task An_export_just_under_the_unsigned_manifest_bound_signs_with_issuer_material_verifies_and_imports()
	{
		var pack = await _harness.CreatePack("Wordy");
		var icon = new IconEntity
		{
			Id = Guid.CreateVersion7(),
			PackId = pack.Id,
			Name = "star",
			ProcessingState = IconProcessingState.Ready,
			AvailableSizes = [],
			MasterContentHash = MasterContentHash.Compute("abc"u8).Value,
			CreatedAt = DateTime.UtcNow
		};
		await _harness.Cache.AddIcons(pack.Id, [icon]);
		await _harness.Storage.WriteVariant(pack.Id, icon.Id, IconVariants.Master, new byte[] { 1, 2, 3 }, CancellationToken.None);
		var baseline = ManifestLength(await Export(pack.Id));
		pack.Description = new string('ä', (IconPackArchiveLimits.MaxUnsignedManifestBytes - (int)baseline - 64) / 6);
		await _harness.Cache.AddOrUpdatePack(pack);
		var exported = await Export(pack.Id);
		var package = Path.Combine(_directory, "wordy.macroDeckIconPack");
		await File.WriteAllBytesAsync(package, exported);

		var signed = await Sign(package);
		var verified = await PackageVerifier.VerifyAsync(signed, _manifestReader, TestPki.Root.PublicKey);
		var result = await Restore(await File.ReadAllBytesAsync(signed));

		Assert.Multiple(() =>
		{
			Assert.That(ManifestLength(exported), Is.GreaterThan(IconPackArchiveLimits.MaxUnsignedManifestBytes - 1024));
			Assert.That(ManifestLength(File.ReadAllBytes(signed)), Is.LessThanOrEqualTo(IconPackArchiveLimits.MaxManifestBytes));
			Assert.That(verified.Success, Is.True, verified.Message);
			Assert.That(result.Success, Is.True, result.ErrorMessage);
		});
	}

	private Task<MacroDeckHost.Domain.Common.Result<IconPackEntity, IconError>> Restore(byte[] archive)
		=> _harness.RestoreService.RestoreAsNewPack("pack.macroDeckIconPack", new MemoryStream(archive), CancellationToken.None);

	private async Task<byte[]> Export(Guid packId)
	{
		using var stream = new MemoryStream();
		var result = await _harness.CreateExportService().Export(packId, stream, CancellationToken.None);
		Assert.That(result.Success, Is.True);
		return stream.ToArray();
	}

	private async Task<string> Sign(string package)
	{
		var issuer = TestPki.IssueIssuer();
		var certificate = TestPki.IssueCertificate(issuer: issuer);
		var chain = SigningCertificateChain.Verify(certificate.CertificateBytes,
			certificate.CertificateSignatureBytes,
			issuer.CertificateBytes,
			issuer.CertificateSignatureBytes,
			TestPki.Root.PublicKey,
			SigningCertificateChain.PackageKeyUsage);
		using var signer = SigningMaterial.Create(certificate.PrivateKey, chain.TrustedCertificate!.Certificate).Material!;
		var output = Path.Combine(_directory, "signed-" + Path.GetFileName(package));

		var result = await PackageSigner.SignAsync(package,
			output,
			signer,
			certificate.CertificateBytes,
			certificate.CertificateSignatureBytes,
			issuer.CertificateBytes,
			issuer.CertificateSignatureBytes,
			_manifestReader);

		Assert.That(result.Success, Is.True, result.Message);
		return output;
	}

	private static byte[] UnsignedPack(int entries, out Guid iconId, string? description = null)
	{
		iconId = Guid.CreateVersion7();
		var contents = new List<(string Path, byte[] Content)> { ($"icons/{iconId}/master.webp", [1, 2, 3]) };
		contents.AddRange(Enumerable.Range(0, entries - 2)
			.Select(index => ($"extra/{index}.bin", Encoding.UTF8.GetBytes(index.ToString(CultureInfo.InvariantCulture)))));
		var manifest = new IconPackManifest
		{
			Id = Guid.CreateVersion7(),
			Name = "Bound",
			Description = description,
			Icons = [new IconManifestEntry { Id = iconId, Name = "star", State = IconProcessingState.Ready }],
			Files = contents
				.Select(file => new PackageFileDigest
				{
					Path = file.Path,
					Sha256 = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(file.Content)),
					Size = file.Content.Length
				})
				.ToList()
		};

		using var stream = new MemoryStream();
		using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
		{
			Write(zip, "pack.json", JsonSerializer.SerializeToUtf8Bytes(manifest, PersistenceJsonOptions.Default));
			foreach (var (path, content) in contents)
			{
				Write(zip, path, content);
			}
		}

		return stream.ToArray();
	}

	private static void Write(ZipArchive zip, string name, byte[] content)
	{
		using var entry = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
		entry.Write(content);
	}

	private static long ManifestLength(byte[] archive)
	{
		using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
		return zip.GetEntry("pack.json")!.Length;
	}

	private static int EntryCount(string path)
	{
		using var zip = ZipFile.OpenRead(path);
		return zip.Entries.Count;
	}
}
