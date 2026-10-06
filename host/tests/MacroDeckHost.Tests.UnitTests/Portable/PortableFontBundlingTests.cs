using System.IO.Compression;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.Keys;
using MacroDeck.Signing.Packages;
using MacroDeck.Signing.TestSupport;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Portable;

namespace MacroDeckHost.Tests.UnitTests.Portable;

[TestFixture]
public class PortableFontBundlingTests
{
	private const string AntaFaceId = "anta-400-5-upright";

	private PortabilityTestHarness _source = null!;
	private PortabilityTestHarness _target = null!;
	private string _directory = null!;

	private static byte[] Fixture =>
		File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Rendering", "Fixtures", "Anta-Regular.ttf"));

	[SetUp]
	public void SetUp()
	{
		_source = new PortabilityTestHarness();
		_target = new PortabilityTestHarness();
		_directory = Directory.CreateTempSubdirectory("macrodeck-portable-fonts-").FullName;
	}

	[TearDown]
	public void TearDown()
	{
		_source.Dispose();
		_target.Dispose();
		Directory.Delete(_directory, recursive: true);
	}

	[Test]
	public async Task Exporting_a_widget_that_uses_an_imported_font_carries_the_font_and_the_ids_it_is_referenced_by()
	{
		await ImportFixture(_source);

		var read = PortableArchive.Read(await ExportWidget($"{{\"fontFaceId\":\"{AntaFaceId}\"}}"), password: null);

		Assert.Multiple(() =>
		{
			Assert.That(read.Fonts, Has.Count.EqualTo(1));
			Assert.That(read.Fonts[0].Bytes, Is.EqualTo(Fixture));
			Assert.That(read.Content!.Fonts.Single().FaceIds, Does.Contain(AntaFaceId));
			Assert.That(read.Manifest!.Contents.FontCount, Is.EqualTo(1));
			Assert.That(read.Manifest.Files!.Select(file => file.Path), Has.One.StartsWith("fonts/"));
		});
	}

	[Test]
	public async Task Fonts_that_are_installed_or_unused_are_not_bundled()
	{
		await ImportFixture(_source);

		var read = PortableArchive.Read(await ExportWidget("{\"fontFaceId\":\"arial-400-5-upright\"}"), password: null);

		Assert.Multiple(() =>
		{
			Assert.That(read.Fonts, Is.Empty);
			Assert.That(read.Content!.Fonts, Is.Empty);
			Assert.That(read.Manifest!.Contents.FontCount, Is.Zero);
		});
	}

	[Test]
	public async Task Importing_on_an_instance_without_the_font_installs_it_before_the_widget()
	{
		await ImportFixture(_source);
		var archive = await ExportWidget($"{{\"fontFaceId\":\"{AntaFaceId}\"}}");

		var created = await ImportWidget(archive);

		Assert.Multiple(() =>
		{
			Assert.That(_target.UserFonts.List().Select(font => font.FaceId), Is.EqualTo(new[] { AntaFaceId }));
			Assert.That(created.Data, Does.Contain(AntaFaceId));
		});
	}

	[Test]
	public async Task A_face_id_recorded_on_another_platform_is_rewritten_to_the_local_id()
	{
		await ImportFixture(_source);
		const string foreignId = "anta-regular-400-5-upright";
		var archive = Rewrite(await ExportWidget($"{{\"fontFaceId\":\"{AntaFaceId}\"}}"),
			content =>
			{
				content.Widgets!.Single().Data = $"{{\"fontFaceId\":\"{foreignId}\"}}";
				content.Fonts.Single().FaceIds = [foreignId];
			});

		var created = await ImportWidget(archive);

		Assert.Multiple(() =>
		{
			Assert.That(created.Data, Does.Contain(AntaFaceId));
			Assert.That(created.Data, Does.Not.Contain(foreignId));
		});
	}

	[Test]
	public async Task An_archive_never_changes_the_font_of_widgets_that_already_exist()
	{
		await ImportFixture(_source);
		const string missingId = "arial-700-5-upright";
		var (_, existingFolder, existing) = await _target.SeedProfile($"{{\"fontFaceId\":\"{missingId}\"}}");
		var archive = Rewrite(await ExportWidget($"{{\"fontFaceId\":\"{AntaFaceId}\"}}"),
			content =>
			{
				content.Widgets!.Single().Data = $"{{\"fontFaceId\":\"{missingId}\"}}";
				content.Fonts.Single().FaceIds = [missingId];
			});

		await ImportWidget(archive);

		var stored = _target.FolderCache.GetFolderById(existingFolder.Id)!.Widgets.Single(widget => widget.Id == existing.Id);
		Assert.Multiple(() =>
		{
			Assert.That(stored.Data, Does.Contain(missingId));
			Assert.That(_target.FontCatalog.ResolveFaceId(missingId), Is.EqualTo(missingId));
		});
	}

	[Test]
	public async Task Exporting_and_importing_a_whole_profile_carries_its_imported_fonts()
	{
		await ImportFixture(_source);
		var (profile, _, _) = await _source.SeedProfile($"{{\"fontFaceId\":\"{AntaFaceId}\"}}");
		var export = await _source.ProfileService.Export(profile.Id, PortableExportOptions.Default, CancellationToken.None);
		Assert.That(export.Success, Is.True);

		var import = await _target.ProfileService.Import(export.Data!, password: null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(import.Success, Is.True);
			Assert.That(_target.UserFonts.List().Select(font => font.FaceId), Is.EqualTo(new[] { AntaFaceId }));
		});
	}

	[Test]
	public async Task A_bundled_font_whose_bytes_do_not_match_its_recorded_hash_is_not_installed()
	{
		await ImportFixture(_source);
		var archive = Rewrite(await ExportWidget($"{{\"fontFaceId\":\"{AntaFaceId}\"}}"),
			_ => { },
			font => font with { Bytes = [.. font.Bytes, 0] });

		var created = await ImportWidget(archive);

		Assert.Multiple(() =>
		{
			Assert.That(_target.UserFonts.List(), Is.Empty);
			Assert.That(created, Is.Not.Null);
		});
	}

	[Test]
	public async Task An_archive_with_fonts_still_passes_the_released_signing_and_declared_file_checks()
	{
		await ImportFixture(_source);
		var package = Path.Combine(_directory, "fonts.macroDeckWidget");
		await File.WriteAllBytesAsync(package, await ExportWidget($"{{\"fontFaceId\":\"{AntaFaceId}\"}}"));

		var signed = await Sign(package);
		var verified = await PackageVerifier.VerifyAsync(signed, new PluginManifestReader(), TestPki.Root.PublicKey);
		using var zip = ZipFile.OpenRead(signed);

		Assert.Multiple(() =>
		{
			Assert.That(verified.Success, Is.True, verified.Message);
			Assert.That(zip.Entries.Select(entry => entry.FullName), Has.One.StartsWith("fonts/"));
		});
	}

	private static async Task ImportFixture(PortabilityTestHarness harness)
	{
		var results = await harness.UserFonts.Import([new UserFontUpload("Anta-Regular.ttf", Fixture)], CancellationToken.None);
		Assert.That(results.Single().Status, Is.EqualTo(UserFontImportStatus.Imported));
	}

	private async Task<byte[]> ExportWidget(string data)
	{
		var (_, folder, widget) = await _source.SeedProfile(data);
		var export = await _source.WidgetService.Export(folder.Id, [widget.Id], PortableExportOptions.Default, CancellationToken.None);
		Assert.That(export.Success, Is.True);
		return export.Data!;
	}

	private async Task<WidgetEntity> ImportWidget(byte[] archive)
	{
		var (_, folder, _) = await _target.SeedProfile(null);
		var import = await _target.WidgetService.Import(folder.Id, anchorX: 2, anchorY: 2, archive, password: null, CancellationToken.None);
		Assert.That(import.Success, Is.True);
		return import.Data!.Single();
	}

	private static byte[] Rewrite(byte[] archive,
		Action<PortableContent> editContent,
		Func<PortableFontFile, PortableFontFile>? editFont = null)
	{
		var read = PortableArchive.Read(archive, password: null);
		editContent(read.Content!);
		var fonts = read.Fonts.Select(editFont ?? (font => font)).ToList();
		return PortableArchive.Write(read.Manifest!, read.Content!, read.Icons, password: null, fonts);
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
			new PluginManifestReader());

		Assert.That(result.Success, Is.True, result.Message);
		return output;
	}
}
