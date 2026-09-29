using MacroDeckHost.Application.Persistence.Profiles;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Portable;

namespace MacroDeckHost.Tests.UnitTests.Portable;

[TestFixture]
public class EmptyCellStylePortabilityTests
{
	private PortabilityTestHarness _harness = null!;

	[SetUp]
	public void SetUp() => _harness = new PortabilityTestHarness();

	[TearDown]
	public void TearDown() => _harness.Dispose();

	[Test]
	public async Task A_profile_export_then_import_keeps_the_default_and_the_folder_override()
	{
		var (source, root, _) = await _harness.SeedProfile(null);
		var profile = _harness.ProfileCache.GetById(source.Id)!;
		profile.DefaultEmptyCellStyle = EmptyCellStyle.Transparent;
		await _harness.ProfileCache.AddOrUpdate(profile);
		root.EmptyCellStyle = EmptyCellStyle.Visible;
		await _harness.FolderCache.AddOrUpdate(root);

		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);
		var import = await _harness.ProfileService.Import(export.Data!, password: null, CancellationToken.None);

		Assert.That(import.Success, Is.True);
		var importedFolder = _harness.FolderCache.GetFoldersByProfileId(import.Data!.Id).Single();
		Assert.Multiple(() =>
		{
			Assert.That(import.Data!.DefaultEmptyCellStyle, Is.EqualTo(EmptyCellStyle.Transparent));
			Assert.That(importedFolder.EmptyCellStyle, Is.EqualTo(EmptyCellStyle.Visible));
		});
	}

	[Test]
	public async Task A_profile_archive_with_an_unknown_style_imports_it_as_unset()
	{
		var archive = PortableArchive.Write(new PortableArchiveManifest { Kind = PortableArchiveKind.Profile },
			new PortableContent
			{
				Kind = PortableArchiveKind.Profile,
				Profile = new ProfileFile
				{
					Id = Guid.NewGuid(),
					Name = "From the future",
					DefaultEmptyCellStyle = "dimmed",
					Folders =
					[
						new ProfileFolder { Id = Guid.NewGuid(), Name = "Home", EmptyCellStyle = "blurred" }
					]
				}
			},
			[],
			password: null);

		var import = await _harness.ProfileService.Import(archive, password: null, CancellationToken.None);

		Assert.That(import.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(import.Data!.DefaultEmptyCellStyle, Is.Null);
			Assert.That(_harness.FolderCache.GetFoldersByProfileId(import.Data!.Id).Single().EmptyCellStyle,
				Is.Null);
		});
	}

	[Test]
	public async Task A_folder_export_then_import_keeps_its_style()
	{
		var (profile, root, _) = await _harness.SeedProfile(null);
		var source = await _harness.AddFolder(profile.Id, "Stream");
		source.EmptyCellStyle = EmptyCellStyle.Transparent;
		await _harness.FolderCache.AddOrUpdate(source);

		var export = await _harness.FolderService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);
		var import = await _harness.FolderService.Import(profile.Id,
			root.Id,
			export.Data!,
			password: null,
			CancellationToken.None);

		Assert.That(import.Success, Is.True);
		Assert.That(import.Data!.Single().EmptyCellStyle, Is.EqualTo(EmptyCellStyle.Transparent));
	}

	[Test]
	public async Task A_folder_archive_with_an_unknown_style_imports_it_as_unset()
	{
		var (profile, root, _) = await _harness.SeedProfile(null);
		var archive = PortableArchive.Write(new PortableArchiveManifest { Kind = PortableArchiveKind.Folder },
			new PortableContent
			{
				Kind = PortableArchiveKind.Folder,
				Folders =
				[
					new ProfileFolder
					{
						Id = Guid.NewGuid(), Name = "Stream", Rows = 3, Columns = 5, EmptyCellStyle = "glass"
					}
				]
			},
			[],
			password: null);

		var import = await _harness.FolderService.Import(profile.Id,
			root.Id,
			archive,
			password: null,
			CancellationToken.None);

		Assert.That(import.Success, Is.True);
		Assert.That(import.Data!.Single().EmptyCellStyle, Is.Null);
	}
}
