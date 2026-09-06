using System.IO.Compression;
using System.Text;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Portable;

[TestFixture]
public class FolderPortabilityServiceTests
{
	private PortabilityTestHarness _harness = null!;

	[SetUp]
	public void SetUp() => _harness = new PortabilityTestHarness();

	[TearDown]
	public void TearDown() => _harness.Dispose();

	[Test]
	public async Task GetExportFileName_UsesTheFolderName()
	{
		var (profile, _, _) = await _harness.SeedProfile(null);
		var folder = await _harness.AddFolder(profile.Id, "Lights: on/off");

		var fileName = _harness.FolderService.GetExportFileName(folder.Id);

		Assert.That(fileName.Data, Is.EqualTo("Lights onoff" + PortableFileExtensions.Folder));
	}

	[Test]
	public async Task Export_UnknownFolder_ReturnsNotFound()
	{
		var export = await Export(Guid.NewGuid());

		Assert.Multiple(() =>
		{
			Assert.That(export.Success, Is.False);
			Assert.That(export.Error, Is.EqualTo(PortabilityError.NotFound));
		});
	}

	[Test]
	public async Task Export_ExcludesTheProfileLocalStartMarker()
	{
		var (_, root, _) = await _harness.SeedProfile(null);
		root.IsDefault = true;
		await _harness.FolderCache.AddOrUpdate(root);

		var export = await Export(root.Id);

		Assert.That(export.Success, Is.True);
		using var zip = new ZipArchive(new MemoryStream(export.Data!), ZipArchiveMode.Read);
		using var reader = new StreamReader(zip.GetEntry("content.json")!.Open(), Encoding.UTF8);
		Assert.That(await reader.ReadToEndAsync(), Does.Not.Contain("isDefault"));
	}

	[Test]
	public async Task Export_ExcludesFocusRuleDeviceIds()
	{
		var (profile, _, _) = await _harness.SeedProfile(null);
		var source = await _harness.AddFolder(profile.Id, "Lights");
		source.FocusRules.Add(new FolderFocusRule
		{
			Id = Guid.NewGuid(),
			ApplicationIdentity = "notepad",
			IdentityKind = ApplicationIdentityKind.ProcessName,
			DeviceId = Guid.NewGuid()
		});
		await _harness.FolderCache.AddOrUpdate(source);

		var export = await Export(source.Id);

		Assert.That(export.Success, Is.True);
		using var zip = new ZipArchive(new MemoryStream(export.Data!), ZipArchiveMode.Read);
		using var reader = new StreamReader(zip.GetEntry("content.json")!.Open(), Encoding.UTF8);
		Assert.That(await reader.ReadToEndAsync(), Does.Not.Contain("focusRules"));
	}

	[Test]
	public async Task Import_NeverResurrectsFocusRules()
	{
		var (profile, _, _) = await _harness.SeedProfile(null);
		var source = await _harness.AddFolder(profile.Id, "Lights");
		source.FocusRules.Add(new FolderFocusRule
		{
			Id = Guid.NewGuid(),
			ApplicationIdentity = "notepad",
			IdentityKind = ApplicationIdentityKind.ProcessName,
			DeviceId = Guid.NewGuid()
		});
		await _harness.FolderCache.AddOrUpdate(source);

		var export = await Export(source.Id);
		var import = await Import(profile.Id, parentFolderId: null, export.Data!);

		Assert.That(import.Success, Is.True);
		Assert.That(import.Data!.Single().FocusRules, Is.Empty);
	}

	[Test]
	public async Task Export_ThenImport_RecreatesTheFolder_ReusingTheIcon()
	{
		var pack = await _harness.Icons.CreatePack("Source Pack");
		var icon = await _harness.AddReadyIcon(pack.Id, "star", sizes: [128]);
		var (profile, root, _) = await _harness.SeedProfile(null);
		var source = await _harness.AddFolder(profile.Id, "Lights", rows: 4, columns: 6);
		var widget = _harness.AddWidget(source.Id,
			$"{{\"states\":{{\"off\":{{\"iconId\":\"{icon.Id}\"}}}}}}",
			x: 2,
			y: 1);

		var export = await Export(source.Id);
		Assert.That(export.Success, Is.True);

		var import = await Import(profile.Id, parentFolderId: root.Id, export.Data!);

		Assert.That(import.Success, Is.True);
		var imported = import.Data!.Single();
		var importedWidget = imported.Widgets.Single();
		var referencedIcons = GuidReferences.ExtractAll(importedWidget.Data)
			.Where(id => _harness.Icons.Cache.GetIconById(id) is not null)
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(imported.Id, Is.Not.EqualTo(source.Id));
			Assert.That(imported.Name, Is.EqualTo("Lights"));
			Assert.That(imported.ParentId, Is.EqualTo(root.Id));
			Assert.That(imported.ProfileId, Is.EqualTo(profile.Id));
			Assert.That(imported.Rows, Is.EqualTo(4));
			Assert.That(imported.Columns, Is.EqualTo(6));
			// The layout is preserved exactly: nothing occupies the imported folder's cells.
			Assert.That(importedWidget.PositionX, Is.EqualTo(2));
			Assert.That(importedWidget.PositionY, Is.EqualTo(1));
			Assert.That(importedWidget.Id, Is.Not.EqualTo(widget.Id));
			Assert.That(referencedIcons, Has.Count.EqualTo(1));
			Assert.That(referencedIcons[0], Is.EqualTo(icon.Id));
			Assert.That(_harness.FolderCache.GetFolderById(source.Id)!.Widgets.Single().Id, Is.EqualTo(widget.Id));
		});
	}

	[Test]
	public async Task Export_PinsAnInheritedGridToItsResolvedValue_AndImportKeepsItInADifferentProfile()
	{
		var (profile, root, _) = await _harness.SeedProfile(null); // root has an explicit 3 x 5 grid
		var child = await _harness.AddFolder(profile.Id, "Lights", parentId: root.Id);
		child.Rows = null;
		child.Columns = null;
		await _harness.FolderCache.AddOrUpdate(child);

		var export = await Export(child.Id);
		Assert.That(export.Success, Is.True);

		var target = await _harness.AddProfile("Target");
		var targetRoot = await _harness.AddFolder(target.Id, "Home", rows: 8, columns: 10);

		var import = await Import(target.Id, targetRoot.Id, export.Data!);

		Assert.That(import.Success, Is.True);
		var imported = import.Data!.Single();
		Assert.Multiple(() =>
		{
			Assert.That(imported.Rows, Is.EqualTo(3));
			Assert.That(imported.Columns, Is.EqualTo(5));
		});
	}

	[Test]
	public async Task Export_WithSubfolders_CarriesTheWholeSubtree_AndKeepsItsShape()
	{
		var (profile, _, _) = await _harness.SeedProfile(null);
		var source = await _harness.AddFolder(profile.Id, "Music");
		var child = await _harness.AddFolder(profile.Id, "Playlists", parentId: source.Id);
		var grandChild = await _harness.AddFolder(profile.Id, "Favourites", parentId: child.Id);
		// A sibling of the exported folder must not be dragged in.
		await _harness.AddFolder(profile.Id, "Unrelated");

		var export = await Export(source.Id, includeSubfolders: true);
		var import = await Import(profile.Id, parentFolderId: null, export.Data!);

		Assert.That(import.Success, Is.True);
		var imported = import.Data!;
		var importedRoot = imported[0];
		var importedChild = imported.Single(folder => folder.Name == "Playlists");
		var importedGrandChild = imported.Single(folder => folder.Name == "Favourites");

		Assert.Multiple(() =>
		{
			Assert.That(imported, Has.Count.EqualTo(3));
			Assert.That(importedRoot.Name, Is.EqualTo("Music"));
			Assert.That(importedRoot.ParentId, Is.Null);
			Assert.That(importedChild.ParentId, Is.EqualTo(importedRoot.Id));
			Assert.That(importedGrandChild.ParentId, Is.EqualTo(importedChild.Id));
			Assert.That(importedChild.Id, Is.Not.EqualTo(child.Id));
			Assert.That(importedGrandChild.Id, Is.Not.EqualTo(grandChild.Id));
		});
	}

	[Test]
	public async Task Export_WithoutSubfolders_CarriesOnlyTheFolderItself()
	{
		var (profile, _, _) = await _harness.SeedProfile(null);
		var source = await _harness.AddFolder(profile.Id, "Music");
		await _harness.AddFolder(profile.Id, "Playlists", parentId: source.Id);

		var export = await Export(source.Id);
		var import = await Import(profile.Id, parentFolderId: null, export.Data!);

		Assert.Multiple(() =>
		{
			Assert.That(import.Success, Is.True);
			Assert.That(import.Data!, Has.Count.EqualTo(1));
			Assert.That(import.Data![0].Name, Is.EqualTo("Music"));
		});
	}

	[Test]
	public async Task Import_RewritesADeckNavigationTargetToTheImportedSubfolder()
	{
		var (profile, _, _) = await _harness.SeedProfile(null);
		var source = await _harness.AddFolder(profile.Id, "Music");
		var child = await _harness.AddFolder(profile.Id, "Playlists", parentId: source.Id);
		_harness.AddWidget(source.Id, $"{{\"targetFolderId\":\"{child.Id}\"}}");

		var export = await Export(source.Id, includeSubfolders: true);
		var import = await Import(profile.Id, parentFolderId: null, export.Data!);

		Assert.That(import.Success, Is.True);
		var importedChild = import.Data!.Single(folder => folder.Name == "Playlists");
		var importedWidget = import.Data![0].Widgets.Single();

		Assert.That(importedWidget.Data, Does.Contain(importedChild.Id.ToString()));
	}

	[Test]
	public async Task Import_IntoAnotherProfile_TakesTheTargetProfileAndTheNextRootOrder()
	{
		var (source, sourceRoot, _) = await _harness.SeedProfile(null);
		_harness.AddWidget(sourceRoot.Id, "{\"label\":\"x\"}");
		var target = await _harness.AddProfile("Target");
		await _harness.AddFolder(target.Id, "Existing", order: 7);

		var export = await Export(sourceRoot.Id);
		var import = await Import(target.Id, parentFolderId: null, export.Data!);

		Assert.That(import.Success, Is.True);
		var imported = import.Data!.Single();

		Assert.Multiple(() =>
		{
			Assert.That(imported.ProfileId, Is.EqualTo(target.Id));
			Assert.That(imported.ParentId, Is.Null);
			Assert.That(imported.Order, Is.EqualTo(8));
			Assert.That(_harness.FolderCache.GetFoldersByProfileId(target.Id), Has.Count.EqualTo(2));
			Assert.That(_harness.FolderCache.GetFoldersByProfileId(source.Id), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Import_ClearsTheDefaultFlagAndPins()
	{
		var (profile, _, _) = await _harness.SeedProfile(null);
		var source = await _harness.AddFolder(profile.Id, "Lights");
		source.IsDefault = true;
		await _harness.FolderCache.AddOrUpdate(source);
		_harness.AddWidget(source.Id, "{\"label\":\"x\"}", isPinned: true);
		var target = await _harness.AddProfile("Target");
		await _harness.AddFolder(target.Id, "Existing");

		var export = await Export(source.Id);
		var import = await Import(target.Id, parentFolderId: null, export.Data!);

		Assert.That(import.Success, Is.True);
		var imported = import.Data!.Single();

		Assert.Multiple(() =>
		{
			Assert.That(imported.IsDefault, Is.False);
			Assert.That(imported.Widgets.Single().IsPinned, Is.False);
		});
	}

	[Test]
	public async Task Import_IntoAProfileWithAPinnedWidget_GrowsTheGridAndKeepsThePinnedCellsFree()
	{
		var (profile, root, _) = await _harness.SeedProfile(null);
		var source = await _harness.AddFolder(profile.Id, "Lights", rows: 2, columns: 2);
		_harness.AddWidget(source.Id, "{\"label\":\"a\"}");
		_harness.AddWidget(source.Id, "{\"label\":\"b\"}", x: 1);

		var target = await _harness.AddProfile("Target");
		var targetRoot = await _harness.AddFolder(target.Id, "Home", rows: 4, columns: 4);
		_harness.AddWidget(targetRoot.Id, "{\"label\":\"pinned\"}", x: 3, y: 3, isPinned: true);

		var export = await Export(source.Id);
		var import = await Import(target.Id, parentFolderId: null, export.Data!);

		Assert.That(import.Success, Is.True);
		var imported = import.Data!.Single();

		Assert.Multiple(() =>
		{
			Assert.That(imported.Columns, Is.EqualTo(4));
			Assert.That(imported.Rows, Is.EqualTo(4));
			Assert.That(imported.Widgets.Select(widget => (widget.PositionX, widget.PositionY)),
				Is.EquivalentTo(new[] { (0, 0), (1, 0) }));
		});
	}

	[Test]
	public async Task Import_WhenAPinnedWidgetOccupiesTheLayout_MovesTheGroupAsAWhole()
	{
		var (profile, _, _) = await _harness.SeedProfile(null);
		var source = await _harness.AddFolder(profile.Id, "Lights", rows: 1, columns: 3);
		_harness.AddWidget(source.Id, "{\"label\":\"a\"}");
		_harness.AddWidget(source.Id, "{\"label\":\"b\"}", x: 1);

		var target = await _harness.AddProfile("Target");
		var targetRoot = await _harness.AddFolder(target.Id, "Home", rows: 1, columns: 3);
		_harness.AddWidget(targetRoot.Id, "{\"label\":\"pinned\"}", isPinned: true);

		var export = await Export(source.Id);
		var import = await Import(target.Id, parentFolderId: null, export.Data!);

		Assert.That(import.Success, Is.True);
		var placements = import.Data!
			.Single()
			.Widgets
			.Select(widget => (widget.PositionX, widget.PositionY))
			.ToList();

		Assert.That(placements, Is.EquivalentTo(new[] { (1, 0), (2, 0) }));
	}

	[Test]
	public async Task Import_WhenThePinnedWidgetLeavesNoRoom_FailsWithoutCreatingAnything()
	{
		var (profile, _, _) = await _harness.SeedProfile(null);
		var source = await _harness.AddFolder(profile.Id, "Lights", rows: 1, columns: 2);
		_harness.AddWidget(source.Id, "{\"label\":\"a\"}");
		_harness.AddWidget(source.Id, "{\"label\":\"b\"}", x: 1);

		var target = await _harness.AddProfile("Target");
		var targetRoot = await _harness.AddFolder(target.Id, "Home", rows: 1, columns: 2);
		_harness.AddWidget(targetRoot.Id, "{\"label\":\"pinned\"}", isPinned: true);

		var export = await Export(source.Id);
		var import = await Import(target.Id, parentFolderId: null, export.Data!);

		Assert.Multiple(() =>
		{
			Assert.That(import.Success, Is.False);
			Assert.That(import.Error, Is.EqualTo(PortabilityError.ValidationError));
			Assert.That(_harness.FolderCache.GetFoldersByProfileId(target.Id), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Import_NestedTwoLevelsDeep_StillDetectsASubtreePinThroughAFreshlyMintedAncestor()
	{
		var profile = await _harness.AddProfile("Target profile");
		var target = await _harness.AddFolder(profile.Id, "Target", rows: 4, columns: 4);
		var pin = new WidgetEntity
		{
			Id = Guid.NewGuid(),
			FolderId = target.Id,
			Type = WidgetTypeIds.ActionButton,
			PositionX = 0,
			PositionY = 0,
			Width = 1,
			Height = 1,
			IsPinned = true,
			PinScope = PinScope.Subtree
		};
		_harness.FolderCache.AddWidget(target.Id, pin);

		var archiveProfile = await _harness.AddProfile("Archive source");
		var archiveRoot = await _harness.AddFolder(archiveProfile.Id, "ArchiveRoot", rows: 4, columns: 4);
		var archiveChild = await _harness.AddFolder(archiveProfile.Id,
			"ArchiveChild",
			archiveRoot.Id,
			rows: 4,
			columns: 4);
		_harness.AddWidget(archiveChild.Id, "{\"label\":\"a\"}");

		var export = await Export(archiveRoot.Id, includeSubfolders: true);
		var import = await Import(profile.Id, target.Id, export.Data!);

		Assert.That(import.Success, Is.True);
		var importedChild = import.Data!.Single(f => f.Name == "ArchiveChild");
		var importedWidget = importedChild.Widgets.Single();

		Assert.That((importedWidget.PositionX, importedWidget.PositionY),
			Is.Not.EqualTo((0, 0)),
			"Target's pin reaches two levels down through the freshly minted ArchiveRoot, so the widget must " +
			"be placed around it rather than on top of it");
	}

	[Test]
	public async Task Import_UnknownProfile_ReturnsNotFound()
	{
		var (_, root, _) = await _harness.SeedProfile(null);
		var export = await Export(root.Id);

		var import = await Import(Guid.NewGuid(), parentFolderId: null, export.Data!);

		Assert.Multiple(() =>
		{
			Assert.That(import.Success, Is.False);
			Assert.That(import.Error, Is.EqualTo(PortabilityError.NotFound));
		});
	}

	[Test]
	public async Task Import_UnknownParentFolder_ReturnsValidationError()
	{
		var (profile, root, _) = await _harness.SeedProfile(null);
		var export = await Export(root.Id);

		var import = await Import(profile.Id, parentFolderId: Guid.NewGuid(), export.Data!);

		Assert.Multiple(() =>
		{
			Assert.That(import.Success, Is.False);
			Assert.That(import.Error, Is.EqualTo(PortabilityError.ValidationError));
		});
	}

	[Test]
	public async Task Import_AParentFromAnotherProfile_ReturnsValidationError()
	{
		var (profile, root, _) = await _harness.SeedProfile(null);
		var other = await _harness.AddProfile("Other");
		var otherFolder = await _harness.AddFolder(other.Id, "Elsewhere");
		var export = await Export(root.Id);

		var import = await Import(profile.Id, otherFolder.Id, export.Data!);

		Assert.Multiple(() =>
		{
			Assert.That(import.Success, Is.False);
			Assert.That(import.Error, Is.EqualTo(PortabilityError.ValidationError));
		});
	}

	[Test]
	public async Task Import_AProfileArchive_ReturnsInvalidArchive()
	{
		var (profile, root, _) = await _harness.SeedProfile("{\"label\":\"x\"}");
		var profileArchive = await _harness.ProfileService.Export(profile.Id,
			PortableExportOptions.Default,
			CancellationToken.None);

		var import = await Import(profile.Id, root.Id, profileArchive.Data!);

		Assert.Multiple(() =>
		{
			Assert.That(import.Success, Is.False);
			Assert.That(import.Error, Is.EqualTo(PortabilityError.InvalidArchive));
		});
	}

	[Test]
	public async Task Export_IncludingSecrets_RequiresAPassword()
	{
		var (_, root, _) = await _harness.SeedProfile("{\"label\":\"x\"}");

		var export = await _harness.FolderService.Export(root.Id,
			new PortableExportOptions { IncludeSecrets = true },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(export.Success, Is.False);
			Assert.That(export.Error, Is.EqualTo(PortabilityError.PasswordRequired));
		});
	}

	[Test]
	public async Task Export_WithTooShortPassword_ReturnsWeakPassword()
	{
		var (_, root, _) = await _harness.SeedProfile("{\"label\":\"x\"}");

		var export = await _harness.FolderService.Export(root.Id,
			new PortableExportOptions
			{
				IncludeSecrets = true, Password = new string('a', PortablePasswordPolicy.MinLength - 1)
			},
			CancellationToken.None);

		Assert.That(export.Error, Is.EqualTo(PortabilityError.WeakPassword));
	}

	[Test]
	public async Task Export_WithSecrets_ThenImport_RecreatesAndRemapsTheSecret()
	{
		var secretId = _harness.Secrets.Store("hunter2", SecretKind.Password);
		var (profile, root, _) = await _harness.SeedProfile(null);
		var source = await _harness.AddFolder(profile.Id, "Lights");
		_harness.AddWidget(source.Id, $"{{\"$secret\":\"{secretId}\"}}");

		var export = await _harness.FolderService.Export(source.Id,
			new PortableExportOptions { IncludeSecrets = true, Password = "export-password" },
			CancellationToken.None);
		Assert.That(export.Success, Is.True);

		var import = await _harness.FolderService.Import(profile.Id,
			root.Id,
			export.Data!,
			"export-password",
			CancellationToken.None);

		Assert.That(import.Success, Is.True);
		var newSecretId = SecretReferences.Extract(import.Data!.Single().Widgets.Single().Data).Single();

		Assert.Multiple(() =>
		{
			Assert.That(newSecretId, Is.Not.EqualTo(secretId));
			Assert.That(_harness.Secrets.Resolve(newSecretId).Result, Is.EqualTo("hunter2"));
		});
	}

	[Test]
	public async Task Import_EncryptedArchive_WithoutPassword_ReturnsPasswordRequired()
	{
		var (profile, root, _) = await _harness.SeedProfile("{\"label\":\"x\"}");
		var export = await _harness.FolderService.Export(root.Id,
			new PortableExportOptions { IncludeSecrets = true, Password = "export-password" },
			CancellationToken.None);

		var import = await Import(profile.Id, root.Id, export.Data!);

		Assert.That(import.Error, Is.EqualTo(PortabilityError.PasswordRequired));
	}

	[Test]
	public async Task Import_WithWrongPassword_ReturnsInvalidPassword()
	{
		var (profile, root, _) = await _harness.SeedProfile("{\"label\":\"x\"}");
		var export = await _harness.FolderService.Export(root.Id,
			new PortableExportOptions { IncludeSecrets = true, Password = "export-password" },
			CancellationToken.None);

		var import = await _harness.FolderService.Import(profile.Id,
			root.Id,
			export.Data!,
			"nope",
			CancellationToken.None);

		Assert.That(import.Error, Is.EqualTo(PortabilityError.InvalidPassword));
	}

	[Test]
	public async Task Import_AGarbageFile_ReturnsInvalidArchive()
	{
		var (profile, root, _) = await _harness.SeedProfile(null);

		var import = await Import(profile.Id, root.Id, [1, 2, 3, 4]);

		Assert.That(import.Error, Is.EqualTo(PortabilityError.InvalidArchive));
	}

	private Task<Domain.Common.Result<byte[], PortabilityError>> Export(Guid folderId,
		bool includeSubfolders = false)
		=> _harness.FolderService.Export(folderId,
			PortableExportOptions.Default with { IncludeSubfolders = includeSubfolders },
			CancellationToken.None);

	private Task<Domain.Common.Result<IReadOnlyList<FolderEntity>, PortabilityError>> Import(Guid profileId,
		Guid? parentFolderId,
		byte[] archiveBytes)
		=> _harness.FolderService.Import(profileId,
			parentFolderId,
			archiveBytes,
			password: null,
			CancellationToken.None);
}
