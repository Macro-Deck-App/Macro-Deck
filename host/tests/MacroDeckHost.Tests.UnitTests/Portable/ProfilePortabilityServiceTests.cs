using System.IO.Compression;
using System.Text;
using MacroDeckHost.Application.Persistence.Profiles;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Infrastructure.Portable;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Portable;

[TestFixture]
public class ProfilePortabilityServiceTests
{
	private PortabilityTestHarness _harness = null!;

	[SetUp]
	public void SetUp() => _harness = new PortabilityTestHarness();

	[TearDown]
	public void TearDown() => _harness.Dispose();

	// Exporting and importing on the same host: the archive's icon is byte-identical to the one already
	// stored, so the import references it instead of writing a second copy (issue #284).
	[Test]
	public async Task Export_ThenImport_CreatesAnIndependentProfile_ReusingTheIconItAlreadyHas()
	{
		var pack = await _harness.Icons.CreatePack("Source Pack");
		var icon = await _harness.AddReadyIcon(pack.Id, "star", sizes: [128]);
		var (source, _, _) = await _harness.SeedProfile($"{{\"states\":{{\"off\":{{\"iconId\":\"{icon.Id}\"}}}}}}");

		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);
		Assert.That(export.Success, Is.True);

		var import = await _harness.ProfileService.Import(export.Data!, password: null, CancellationToken.None);

		Assert.That(import.Success, Is.True);
		var imported = import.Data!;
		var importedFolders = _harness.FolderCache.GetFoldersByProfileId(imported.Id);
		var importedWidget = importedFolders.Single().Widgets.Single();
		var referencedIcons = GuidReferences.ExtractAll(importedWidget.Data)
			.Where(id => _harness.Icons.Cache.GetIconById(id) is not null)
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(imported.Id, Is.Not.EqualTo(source.Id));
			Assert.That(imported.Name, Is.EqualTo("Source"));
			Assert.That(importedFolders.Single().Id, Is.Not.EqualTo(source.Id));
			Assert.That(referencedIcons, Has.Count.EqualTo(1));
			Assert.That(referencedIcons[0], Is.EqualTo(icon.Id), "the identical icon is reused, not copied");
			Assert.That(_harness.Icons.Cache.GetIconsByPackId(pack.Id), Has.Count.EqualTo(1));
		});

		Assert.That(_harness.Icons.Cache.GetAllPacks()
				.Any(p => p.SourceType == IconPackSourceType.MacroDeckImport),
			Is.False);
	}

	[Test]
	public async Task Import_RecreatesAnIconTheCatalogDoesNotHave_InTheSharedPack()
	{
		var pack = await _harness.Icons.CreatePack("Source Pack");
		var icon = await _harness.AddReadyIcon(pack.Id, "star", sizes: [128]);
		var (source, _, _) = await _harness.SeedProfile($"{{\"states\":{{\"off\":{{\"iconId\":\"{icon.Id}\"}}}}}}");

		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);
		Assert.That(export.Success, Is.True);

		await _harness.Icons.Cache.RemoveIcon(icon.Id);

		var import = await _harness.ProfileService.Import(export.Data!, password: null, CancellationToken.None);
		Assert.That(import.Success, Is.True);

		var importedWidget = _harness.FolderCache.GetFoldersByProfileId(import.Data!.Id).Single().Widgets.Single();
		var referencedIcons = GuidReferences.ExtractAll(importedWidget.Data)
			.Where(id => _harness.Icons.Cache.GetIconById(id) is not null)
			.ToList();

		Assert.That(referencedIcons, Has.Count.EqualTo(1));
		var importedIcon = _harness.Icons.Cache.GetIconById(referencedIcons[0])!;
		var importedPack = _harness.Icons.Cache.GetPackById(importedIcon.PackId)!;
		Assert.Multiple(() =>
		{
			Assert.That(importedIcon.Id, Is.Not.EqualTo(icon.Id));
			Assert.That(importedWidget.Data, Does.Not.Contain(icon.Id.ToString()));
			Assert.That(importedPack.SourceType, Is.EqualTo(IconPackSourceType.MacroDeckImport));
			Assert.That(importedIcon.MasterContentHash,
				Is.EqualTo(MasterContentHash.Compute(PortabilityTestHarness.MasterBytesFor("star")).Value));
		});
	}

	[Test]
	public async Task Export_ThenImport_AnInheritedGridAndTheProfileDefaultsSurviveTheRoundTrip()
	{
		var (source, root, _) = await _harness.SeedProfile(null);
		var sourceProfile = _harness.ProfileCache.GetById(source.Id)!;
		sourceProfile.DefaultWidgetSpacing = 12;
		sourceProfile.DefaultWidgetBorderRadius = 18;
		await _harness.ProfileCache.AddOrUpdate(sourceProfile);
		root.Rows = null;
		root.Columns = null;
		await _harness.FolderCache.AddOrUpdate(root);

		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);
		Assert.That(export.Success, Is.True);

		var import = await _harness.ProfileService.Import(export.Data!, password: null, CancellationToken.None);

		Assert.That(import.Success, Is.True);
		var importedFolder = _harness.FolderCache.GetFoldersByProfileId(import.Data!.Id).Single();
		Assert.Multiple(() =>
		{
			Assert.That(importedFolder.Rows, Is.Null);
			Assert.That(importedFolder.Columns, Is.Null);
			Assert.That(import.Data!.DefaultWidgetSpacing, Is.EqualTo(12));
			Assert.That(import.Data!.DefaultWidgetBorderRadius, Is.EqualTo(18));
		});
	}

	[Test]
	public async Task Export_ExcludesTheProfileLocalStartMarker()
	{
		var (source, root, _) = await _harness.SeedProfile("{\"label\":\"x\"}");
		root.IsDefault = true;
		await _harness.FolderCache.AddOrUpdate(root);

		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);

		Assert.That(export.Success, Is.True);
		using var zip = new ZipArchive(new MemoryStream(export.Data!), ZipArchiveMode.Read);
		using var reader = new StreamReader(zip.GetEntry("content.json")!.Open(), Encoding.UTF8);
		var contentJson = await reader.ReadToEndAsync();
		Assert.That(contentJson, Does.Not.Contain("isDefault"));
	}

	[Test]
	public async Task Export_ExcludesFocusRuleDeviceIds()
	{
		var (source, root, _) = await _harness.SeedProfile("{\"label\":\"x\"}");
		root.FocusRules.Add(new FolderFocusRule
		{
			Id = Guid.NewGuid(),
			ApplicationIdentity = "notepad",
			IdentityKind = ApplicationIdentityKind.ProcessName,
			DeviceId = Guid.NewGuid()
		});
		await _harness.FolderCache.AddOrUpdate(root);

		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);

		Assert.That(export.Success, Is.True);
		using var zip = new ZipArchive(new MemoryStream(export.Data!), ZipArchiveMode.Read);
		using var reader = new StreamReader(zip.GetEntry("content.json")!.Open(), Encoding.UTF8);
		Assert.That(await reader.ReadToEndAsync(), Does.Not.Contain("focusRules"));
	}

	[Test]
	public async Task Import_NeverResurrectsFocusRules()
	{
		var (source, root, _) = await _harness.SeedProfile("{\"label\":\"x\"}");
		root.FocusRules.Add(new FolderFocusRule
		{
			Id = Guid.NewGuid(),
			ApplicationIdentity = "notepad",
			IdentityKind = ApplicationIdentityKind.ProcessName,
			DeviceId = Guid.NewGuid()
		});
		await _harness.FolderCache.AddOrUpdate(root);

		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);
		var import = await _harness.ProfileService.Import(export.Data!, password: null, CancellationToken.None);

		Assert.That(import.Success, Is.True);
		var importedFolder = _harness.FolderCache.GetFoldersByProfileId(import.Data!.Id).Single();
		Assert.That(importedFolder.FocusRules, Is.Empty);
	}

	[Test]
	public async Task Import_IgnoresArchivedStartMarkerAndSelectsTheOldestRoot()
	{
		var oldestId = Guid.NewGuid();
		var markedNewerId = Guid.NewGuid();
		var archive = PortableArchive.Write(new PortableArchiveManifest { Kind = PortableArchiveKind.Profile },
			new PortableContent
			{
				Kind = PortableArchiveKind.Profile,
				Profile = new ProfileFile
				{
					Id = Guid.NewGuid(), Name = "Imported", DefaultRows = 3, DefaultColumns = 5,
					Folders =
					[
						new ProfileFolder
						{
							Id = oldestId, Name = "Oldest", Order = 1, Rows = 3, Columns = 5,
							CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
						},
						new ProfileFolder
						{
							Id = markedNewerId, Name = "Marked by source", Order = 0, Rows = 3, Columns = 5,
							IsDefault = true,
							CreatedAt = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc)
						}
					]
				}
			},
			[],
			password: null);

		var import = await _harness.ProfileService.Import(archive, password: null, CancellationToken.None);

		Assert.That(import.Success, Is.True);
		var folders = _harness.FolderCache.GetFoldersByProfileId(import.Data!.Id);
		Assert.Multiple(() =>
		{
			Assert.That(folders.Single(folder => folder.Name == "Oldest").IsDefault, Is.True);
			Assert.That(folders.Single(folder => folder.Name == "Marked by source").IsDefault, Is.False);
		});
	}

	[Test]
	public async Task Import_LegacyArchiveWithoutTimestamps_SelectsTheLowestRootOrder()
	{
		var archive = PortableArchive.Write(new PortableArchiveManifest { Kind = PortableArchiveKind.Profile },
			new PortableContent
			{
				Kind = PortableArchiveKind.Profile,
				Profile = new ProfileFile
				{
					Id = Guid.NewGuid(), Name = "Legacy import", DefaultRows = 3, DefaultColumns = 5,
					// Deliberately reverse archive order and root order: timestamps were absent from older
					// files, so the profile invariant must not derive chronology from import-loop timing.
					Folders =
					[
						new ProfileFolder
							{ Id = Guid.NewGuid(), Name = "Later root", Order = 9, Rows = 3, Columns = 5 },
						new ProfileFolder
							{ Id = Guid.NewGuid(), Name = "Middle root", Order = 5, Rows = 3, Columns = 5 },
						new ProfileFolder { Id = Guid.NewGuid(), Name = "First root", Order = 1, Rows = 3, Columns = 5 }
					]
				}
			},
			[],
			password: null);

		var import = await _harness.ProfileService.Import(archive, password: null, CancellationToken.None);

		Assert.That(import.Success, Is.True);
		var folders = _harness.FolderCache.GetFoldersByProfileId(import.Data!.Id);
		var first = folders.Single(folder => folder.Name == "First root");
		var middle = folders.Single(folder => folder.Name == "Middle root");
		var later = folders.Single(folder => folder.Name == "Later root");
		Assert.Multiple(() =>
		{
			Assert.That(first.IsDefault, Is.True);
			Assert.That(middle.IsDefault, Is.False);
			Assert.That(later.IsDefault, Is.False);
			Assert.That(folders.All(folder => folder.CreatedAt != default(DateTime)), Is.True);
			Assert.That(first.CreatedAt, Is.LessThan(middle.CreatedAt));
			Assert.That(middle.CreatedAt, Is.LessThan(later.CreatedAt));
		});
	}

	[Test]
	public async Task Export_IncludingSecrets_RequiresAPassword()
	{
		var (source, _, _) = await _harness.SeedProfile("{\"label\":\"x\"}");

		var export = await _harness.ProfileService.Export(source.Id,
			new PortableExportOptions { IncludeSecrets = true },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(export.Success, Is.False);
			Assert.That(export.Error, Is.EqualTo(PortabilityError.PasswordRequired));
		});
	}

	[Test]
	public async Task Export_WithSecrets_ThenImport_RecreatesAndRemapsTheSecret()
	{
		var secretId = _harness.Secrets.Store("hunter2", SecretKind.Password);
		var (source, _, _) = await _harness.SeedProfile($"{{\"$secret\":\"{secretId}\"}}");

		var export = await _harness.ProfileService.Export(source.Id,
			new PortableExportOptions { IncludeSecrets = true, Password = "export-password" },
			CancellationToken.None);
		Assert.That(export.Success, Is.True);

		var import = await _harness.ProfileService.Import(export.Data!,
			password: "export-password",
			CancellationToken.None);

		Assert.That(import.Success, Is.True);
		var importedWidget = _harness.FolderCache.GetFoldersByProfileId(import.Data!.Id).Single().Widgets.Single();
		var newSecretId = SecretReferences.Extract(importedWidget.Data).Single();

		Assert.Multiple(() =>
		{
			Assert.That(newSecretId, Is.Not.EqualTo(secretId));
			Assert.That(_harness.Secrets.Resolve(newSecretId).Result, Is.EqualTo("hunter2"));
			Assert.That(_harness.Secrets.Resolve(secretId).Result, Is.EqualTo("hunter2"));
		});
	}

	[Test]
	public async Task Import_WithWrongPassword_ReturnsInvalidPassword()
	{
		var (source, _, _) = await _harness.SeedProfile("{\"label\":\"x\"}");
		var export = await _harness.ProfileService.Export(source.Id,
			new PortableExportOptions { IncludeSecrets = true, Password = "export-password" },
			CancellationToken.None);

		var import = await _harness.ProfileService.Import(export.Data!, password: "nope", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(import.Success, Is.False);
			Assert.That(import.Error, Is.EqualTo(PortabilityError.InvalidPassword));
		});
	}

	[Test]
	public async Task Import_EncryptedArchive_WithoutPassword_ReturnsPasswordRequired()
	{
		var (source, _, _) = await _harness.SeedProfile("{\"label\":\"x\"}");
		var export = await _harness.ProfileService.Export(source.Id,
			new PortableExportOptions { IncludeSecrets = true, Password = "export-password" },
			CancellationToken.None);

		var import = await _harness.ProfileService.Import(export.Data!, password: null, CancellationToken.None);

		Assert.That(import.Error, Is.EqualTo(PortabilityError.PasswordRequired));
	}

	[Test]
	public async Task Export_UnknownProfile_ReturnsNotFound()
	{
		var export = await _harness.ProfileService.Export(Guid.NewGuid(),
			PortableExportOptions.Default,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(export.Success, Is.False);
			Assert.That(export.Error, Is.EqualTo(PortabilityError.NotFound));
		});
	}

	[Test]
	public async Task Export_WithTooShortPassword_ReturnsWeakPassword()
	{
		var (source, _, _) = await _harness.SeedProfile("{\"label\":\"x\"}");

		var export = await _harness.ProfileService.Export(source.Id,
			new PortableExportOptions
			{
				IncludeSecrets = true, Password = new string('a', PortablePasswordPolicy.MinLength - 1)
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(export.Success, Is.False);
			Assert.That(export.Error, Is.EqualTo(PortabilityError.WeakPassword));
		});
	}

	[Test]
	public async Task Export_WithPasswordAtTheMinimumLength_Succeeds()
	{
		var (source, _, _) = await _harness.SeedProfile("{\"label\":\"x\"}");

		var export = await _harness.ProfileService.Export(source.Id,
			new PortableExportOptions
			{
				IncludeSecrets = true, Password = new string('a', PortablePasswordPolicy.MinLength)
			},
			CancellationToken.None);

		Assert.That(export.Success, Is.True);
	}

	[Test]
	public async Task G1_ExportThenImport_PreservesBothScopesAndTheSubtreesNesting()
	{
		var profile = await _harness.AddProfile("Source");
		var main = await _harness.AddFolder(profile.Id, "Main", rows: 4, columns: 4);
		var games = await _harness.AddFolder(profile.Id, "Games", main.Id, rows: 4, columns: 4);
		var retro = await _harness.AddFolder(profile.Id, "Retro", games.Id, rows: 4, columns: 4);

		var profileWidget = new WidgetEntity
		{
			Id = Guid.NewGuid(),
			FolderId = main.Id,
			Type = WidgetTypeIds.ActionButton,
			PositionX = 0,
			PositionY = 0,
			Width = 1,
			Height = 1,
			IsPinned = true,
			PinScope = PinScope.Profile
		};
		_harness.FolderCache.AddWidget(main.Id, profileWidget);
		var subtreeWidget = new WidgetEntity
		{
			Id = Guid.NewGuid(),
			FolderId = games.Id,
			Type = WidgetTypeIds.ActionButton,
			PositionX = 1,
			PositionY = 1,
			Width = 1,
			Height = 1,
			IsPinned = true,
			PinScope = PinScope.Subtree
		};
		_harness.FolderCache.AddWidget(games.Id, subtreeWidget);

		var export = await _harness.ProfileService.Export(profile.Id,
			PortableExportOptions.Default,
			CancellationToken.None);
		Assert.That(export.Success, Is.True);

		var import = await _harness.ProfileService.Import(export.Data!, password: null, CancellationToken.None);
		Assert.That(import.Success, Is.True);

		var importedFolders = _harness.FolderCache.GetFoldersByProfileId(import.Data!.Id);
		var importedMain = importedFolders.Single(f => f.Name == "Main");
		var importedGames = importedFolders.Single(f => f.Name == "Games");
		var importedRetro = importedFolders.Single(f => f.Name == "Retro");
		var importedProfileWidget = importedMain.Widgets.Single();
		var importedSubtreeWidget = importedGames.Widgets.Single();

		Assert.Multiple(() =>
		{
			Assert.That(importedProfileWidget.PinScope, Is.EqualTo(PinScope.Profile));
			Assert.That(importedSubtreeWidget.PinScope, Is.EqualTo(PinScope.Subtree));
			Assert.That(importedGames.ParentId, Is.EqualTo(importedMain.Id));
			Assert.That(importedRetro.ParentId, Is.EqualTo(importedGames.Id));
		});

		var secrets = new FakeSecretService();
		var widgetService = new WidgetService(_harness.FolderCache,
			_harness.ProfileCache,
			new RecordingMediator(),
			new WidgetSecretScrubber(secrets),
			new WidgetSecretCloner(secrets),
			new NullWidgetVariableCloner());
		var createInMain = await widgetService.Create(importedMain.Id, Widget(1, 1));
		var createInRetro = await widgetService.Create(importedRetro.Id, Widget(1, 1));
		Assert.Multiple(() =>
		{
			Assert.That(createInMain.Success, Is.True, "Main is the pin's grandparent, not reached");
			Assert.That(createInRetro.Success, Is.False, "Retro is a descendant of the pin's home, reached");
		});
	}

	[Test]
	public async Task G2_Import_AWidgetWithNoStoredScope_ImportsAsProfile()
	{
		var profile = await _harness.AddProfile("Source");
		var main = await _harness.AddFolder(profile.Id, "Main", rows: 4, columns: 4);
		var w = new WidgetEntity
		{
			Id = Guid.NewGuid(),
			FolderId = main.Id,
			Type = WidgetTypeIds.ActionButton,
			PositionX = 0,
			PositionY = 0,
			Width = 1,
			Height = 1,
			IsPinned = true,
			PinScope = PinScope.Profile
		};
		_harness.FolderCache.AddWidget(main.Id, w);

		var export = await _harness.ProfileService.Export(profile.Id,
			PortableExportOptions.Default,
			CancellationToken.None);
		Assert.That(export.Success, Is.True);
		using (var zip = new ZipArchive(new MemoryStream(export.Data!), ZipArchiveMode.Read))
		using (var reader = new StreamReader(zip.GetEntry("content.json")!.Open(), Encoding.UTF8))
		{
			Assert.That(await reader.ReadToEndAsync(), Does.Not.Contain("pinScope"));
		}

		var import = await _harness.ProfileService.Import(export.Data!, password: null, CancellationToken.None);

		Assert.That(import.Success, Is.True);
		var importedWidget = _harness.FolderCache.GetFoldersByProfileId(import.Data!.Id).Single().Widgets.Single();
		Assert.That(importedWidget.PinScope, Is.EqualTo(PinScope.Profile));
	}

	private static WidgetEntity Widget(int x, int y) => new()
	{
		Id = Guid.NewGuid(),
		Type = WidgetTypeIds.ActionButton,
		PositionX = x,
		PositionY = y,
		Width = 1,
		Height = 1
	};
}
