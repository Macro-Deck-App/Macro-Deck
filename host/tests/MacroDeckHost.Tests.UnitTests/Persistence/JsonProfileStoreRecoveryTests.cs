using System.Text;
using MacroDeckHost.Application.Persistence.Profiles;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Persistence;

[TestFixture]
public class JsonProfileStoreRecoveryTests
{
	private TestPaths _paths = null!;
	private RecordingPersistenceRecoveryReporter _reporter = null!;
	private JsonProfileStore _store = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		_reporter = new RecordingPersistenceRecoveryReporter();
		_store = new JsonProfileStore(_paths, new LoggerConfiguration().CreateLogger(), _reporter);
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public void Save_KeepsThePreviousVersionAsABackup_OnTheSecondSave()
	{
		var id = Guid.NewGuid();
		var path = PrimaryPathFor(id);
		_store.Save(new ProfileFile { Id = id, Name = "V1" });
		var v1Content = File.ReadAllText(path);

		_store.Save(new ProfileFile { Id = id, Name = "V2" });

		Assert.Multiple(() =>
		{
			Assert.That(_store.LoadAll().Profiles.Single().Name, Is.EqualTo("V2"));
			Assert.That(File.ReadAllText(path + ".bak"), Is.EqualTo(v1Content));
		});
	}

	[Test]
	public void Save_DoesNotWriteABackup_OnTheFirstSaveOfAProfile()
	{
		var id = Guid.NewGuid();
		_store.Save(new ProfileFile { Id = id, Name = "V1" });

		Assert.That(File.Exists(PrimaryPathFor(id) + ".bak"), Is.False);
	}

	[Test]
	public void LoadAll_LeavesTheBackupHoldingTheGoodVersion_AfterRecoveringFromIt()
	{
		var id = Guid.NewGuid();
		var path = PrimaryPathFor(id);
		_store.Save(new ProfileFile { Id = id, Name = "V1" });
		_store.Save(new ProfileFile { Id = id, Name = "V2" });
		var backupBefore = File.ReadAllBytes(path + ".bak");
		File.WriteAllText(path, string.Empty);

		var loaded = _store.LoadAll();

		Assert.That(loaded.Profiles[0].Name, Is.EqualTo("V1"));
		Assert.That(File.ReadAllBytes(path + ".bak"), Is.EqualTo(backupBefore));
	}

	[Test]
	public void LoadAll_RecoversFromTheBackup_WhenThePrimaryIsTruncated()
	{
		var id = Guid.NewGuid();
		var path = PrimaryPathFor(id);
		_store.Save(new ProfileFile { Id = id, Name = "V1" });
		_store.Save(new ProfileFile { Id = id, Name = "V2" });
		var truncatedText = $$"""{"id":"{{id}}","name":"V""";
		var truncated = Encoding.UTF8.GetBytes(truncatedText);
		File.WriteAllBytes(path, truncated);

		var loaded = _store.LoadAll();

		Assert.Multiple(() =>
		{
			Assert.That(loaded.Profiles, Has.Count.EqualTo(1));
			Assert.That(loaded.Profiles[0].Name, Is.EqualTo("V1"));
			Assert.That(loaded.UnreadableCount, Is.EqualTo(0));
		});

		var corruptFiles = Directory.GetFiles(_paths.ProfilesDirectory, Path.GetFileName(path) + ".corrupt-*");
		Assert.That(corruptFiles, Has.Length.EqualTo(1));
		Assert.That(File.ReadAllBytes(corruptFiles[0]), Is.EqualTo(truncated));
		Assert.Multiple(() =>
		{
			Assert.That(File.ReadAllText(path), Does.Contain("\"V1\""));
			Assert.That(_reporter.Recoveries, Has.Count.EqualTo(1));
			Assert.That(_reporter.Recoveries[0].Path, Is.EqualTo(path));
		});

		var secondReporter = new RecordingPersistenceRecoveryReporter();
		var secondStore = new JsonProfileStore(_paths, new LoggerConfiguration().CreateLogger(), secondReporter);
		var secondLoad = secondStore.LoadAll();

		Assert.Multiple(() =>
		{
			Assert.That(secondLoad.Profiles.Single().Name, Is.EqualTo("V1"));
			Assert.That(secondReporter.Recoveries, Is.Empty);
			Assert.That(Directory.GetFiles(_paths.ProfilesDirectory, Path.GetFileName(path) + ".corrupt-*"),
				Has.Length.EqualTo(1));
		});
	}

	[Test]
	public void LoadAll_RecoversFromTheBackup_WhenThePrimaryIsEmpty()
	{
		var id = Guid.NewGuid();
		var path = PrimaryPathFor(id);
		_store.Save(new ProfileFile { Id = id, Name = "V1" });
		_store.Save(new ProfileFile { Id = id, Name = "V2" });
		File.WriteAllText(path, string.Empty);

		var loaded = _store.LoadAll();

		Assert.Multiple(() =>
		{
			Assert.That(loaded.Profiles, Has.Count.EqualTo(1));
			Assert.That(loaded.Profiles[0].Name, Is.EqualTo("V1"));
			Assert.That(loaded.UnreadableCount, Is.EqualTo(0));
		});

		var corruptFiles = Directory.GetFiles(_paths.ProfilesDirectory, Path.GetFileName(path) + ".corrupt-*");
		Assert.Multiple(() =>
		{
			Assert.That(corruptFiles, Has.Length.EqualTo(1));
			Assert.That(File.ReadAllBytes(corruptFiles[0]), Is.Empty);
			Assert.That(File.ReadAllText(path), Does.Contain("\"V1\""));
			Assert.That(_reporter.Recoveries, Has.Count.EqualTo(1));
		});

		var secondReporter = new RecordingPersistenceRecoveryReporter();
		var secondStore = new JsonProfileStore(_paths, new LoggerConfiguration().CreateLogger(), secondReporter);
		var secondLoad = secondStore.LoadAll();

		Assert.Multiple(() =>
		{
			Assert.That(secondLoad.Profiles.Single().Name, Is.EqualTo("V1"));
			Assert.That(secondReporter.Recoveries, Is.Empty);
			Assert.That(Directory.GetFiles(_paths.ProfilesDirectory, Path.GetFileName(path) + ".corrupt-*"),
				Has.Length.EqualTo(1));
		});
	}

	[Test]
	public void LoadAll_RecoversFromTheTemporaryFile_WhenThePrimaryIsMissing()
	{
		var id = Guid.NewGuid();
		var path = PrimaryPathFor(id);
		Directory.CreateDirectory(_paths.ProfilesDirectory);
		File.WriteAllText(path + ".bak", ProfileJson(id, "V1"));
		File.WriteAllText(path + ".tmp", ProfileJson(id, "V2"));

		var loaded = _store.LoadAll();

		Assert.Multiple(() =>
		{
			Assert.That(loaded.Profiles.Single().Name, Is.EqualTo("V2"));
			Assert.That(File.Exists(path), Is.True);
			Assert.That(File.ReadAllText(path), Does.Contain("\"V2\""));
			Assert.That(File.Exists(path + ".tmp"), Is.False);
		});
	}

	[Test]
	public void LoadAll_PrefersTheBackup_WhenBothThePrimaryAndTheTemporaryFileAreUnreadable()
	{
		var id = Guid.NewGuid();
		var path = PrimaryPathFor(id);
		Directory.CreateDirectory(_paths.ProfilesDirectory);
		File.WriteAllText(path, $$"""{"id":"{{id}}","name":"V""");
		File.WriteAllText(path + ".tmp", string.Empty);
		File.WriteAllText(path + ".bak", ProfileJson(id, "V1"));

		var loaded = _store.LoadAll();

		Assert.Multiple(() =>
		{
			Assert.That(loaded.Profiles.Single().Name, Is.EqualTo("V1"));
			Assert.That(_reporter.Recoveries, Has.Count.EqualTo(1));
			Assert.That(_reporter.Losses, Is.Empty);
		});
	}

	[Test]
	public void LoadAll_PromotesTheTemporaryFile_WhenItIsNewerThanAValidPrimary()
	{
		var id = Guid.NewGuid();
		var path = PrimaryPathFor(id);
		_store.Save(new ProfileFile { Id = id, Name = "V1" });
		File.WriteAllText(path + ".tmp", ProfileJson(id, "V2"));
		File.SetLastWriteTimeUtc(path + ".tmp", File.GetLastWriteTimeUtc(path).AddSeconds(5));

		var loaded = _store.LoadAll();

		Assert.Multiple(() =>
		{
			Assert.That(loaded.Profiles.Single().Name, Is.EqualTo("V2"));
			Assert.That(File.ReadAllText(path), Does.Contain("\"V2\""));
		});
	}

	[Test]
	public void LoadAll_KeepsTheValidPrimary_AndDiscardsAnOlderTemporaryFile()
	{
		var id = Guid.NewGuid();
		var path = PrimaryPathFor(id);
		_store.Save(new ProfileFile { Id = id, Name = "V1" });
		var primaryBytes = File.ReadAllBytes(path);
		File.WriteAllText(path + ".tmp", ProfileJson(id, "V2"));
		File.SetLastWriteTimeUtc(path + ".tmp", File.GetLastWriteTimeUtc(path).AddSeconds(-5));

		var loaded = _store.LoadAll();

		Assert.Multiple(() =>
		{
			Assert.That(loaded.Profiles.Single().Name, Is.EqualTo("V1"));
			Assert.That(File.ReadAllBytes(path), Is.EqualTo(primaryBytes));
			Assert.That(File.Exists(path + ".tmp"), Is.False);
			Assert.That(_reporter.Recoveries, Is.Empty);
		});
	}

	[Test]
	public void LoadAll_LeavesTheFileUntouched_WhenNothingCanBeRecovered()
	{
		var id = Guid.NewGuid();
		var path = PrimaryPathFor(id);
		Directory.CreateDirectory(_paths.ProfilesDirectory);
		var bytes = "{ not valid json"u8.ToArray();
		File.WriteAllBytes(path, bytes);

		var loaded = _store.LoadAll();

		Assert.Multiple(() =>
		{
			Assert.That(loaded.Profiles, Is.Empty);
			Assert.That(loaded.UnreadableCount, Is.EqualTo(1));
			Assert.That(File.Exists(path), Is.True);
			Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
			Assert.That(Directory.GetFiles(_paths.ProfilesDirectory, Path.GetFileName(path) + ".corrupt-*"), Is.Empty);
			Assert.That(_reporter.Losses, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public void LoadAll_KeepsTheBackupIntact_WhenTheRepairCannotBeWritten()
	{
		var id = Guid.NewGuid();
		var path = PrimaryPathFor(id);
		Directory.CreateDirectory(_paths.ProfilesDirectory);
		File.WriteAllText(path, $$"""{"id":"{{id}}","name":"V""");
		var backupBytes = Encoding.UTF8.GetBytes(ProfileJson(id, "V1"));
		File.WriteAllBytes(path + ".bak", backupBytes);
		Directory.CreateDirectory(path + ".tmp");

		var loaded = _store.LoadAll();

		Assert.Multiple(() =>
		{
			Assert.That(loaded.Profiles.Single().Name, Is.EqualTo("V1"));
			Assert.That(File.Exists(path + ".bak"), Is.True);
			Assert.That(File.ReadAllBytes(path + ".bak"), Is.EqualTo(backupBytes));
		});

		Directory.Delete(path + ".tmp");
		var secondStore = new JsonProfileStore(_paths, new LoggerConfiguration().CreateLogger());
		var secondLoad = secondStore.LoadAll();

		Assert.That(secondLoad.Profiles.Single().Name, Is.EqualTo("V1"));
	}

	[Test]
	public void LoadAll_KeepsAtMostThreeDamagedCopies_PerProfile()
	{
		var id = Guid.NewGuid();
		var path = PrimaryPathFor(id);
		Directory.CreateDirectory(_paths.ProfilesDirectory);
		File.WriteAllText(path + ".corrupt-20200101-000000-000", "oldest");
		File.WriteAllText(path + ".corrupt-20210101-000000-000", "older");
		File.WriteAllText(path + ".corrupt-20220101-000000-000", "old");

		var otherId = Guid.NewGuid();
		var otherPath = PrimaryPathFor(otherId);
		File.WriteAllText(otherPath + ".corrupt-20200101-000000-000", "other-oldest");
		File.WriteAllText(otherPath + ".corrupt-20210101-000000-000", "other-older");
		File.WriteAllText(otherPath + ".corrupt-20220101-000000-000", "other-old");

		File.WriteAllText(path, $$"""{"id":"{{id}}","name":"V""");
		File.WriteAllText(path + ".bak", ProfileJson(id, "V1"));

		_store.LoadAll();

		var corruptFiles = Directory.GetFiles(_paths.ProfilesDirectory, Path.GetFileName(path) + ".corrupt-*");
		var contents = corruptFiles.Select(File.ReadAllText).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(corruptFiles, Has.Length.EqualTo(3));
			Assert.That(contents, Has.None.EqualTo("oldest"));
			Assert.That(contents, Has.Some.EqualTo("older"));
			Assert.That(contents, Has.Some.EqualTo("old"));

			var otherCorruptFiles
				= Directory.GetFiles(_paths.ProfilesDirectory, Path.GetFileName(otherPath) + ".corrupt-*");
			Assert.That(otherCorruptFiles, Has.Length.EqualTo(3));
			var otherContents = otherCorruptFiles.Select(File.ReadAllText).ToList();
			Assert.That(otherContents, Is.EquivalentTo(["other-oldest", "other-older", "other-old"]));
		});
	}

	[Test]
	public void Delete_RemovesEverySidecar_SoAProfileDoesNotReappear()
	{
		var id = Guid.NewGuid();
		var path = PrimaryPathFor(id);
		_store.Save(new ProfileFile { Id = id, Name = "V1" });
		_store.Save(new ProfileFile { Id = id, Name = "V2" });
		File.WriteAllText(path + ".tmp", ProfileJson(id, "V3"));

		_store.Delete(id);

		var freshReporter = new RecordingPersistenceRecoveryReporter();
		var freshStore = new JsonProfileStore(_paths, new LoggerConfiguration().CreateLogger(), freshReporter);
		var loaded = freshStore.LoadAll();

		Assert.Multiple(() =>
		{
			Assert.That(loaded.Profiles, Is.Empty);
			Assert.That(loaded.UnreadableCount, Is.EqualTo(0));
			Assert.That(Directory.GetFiles(_paths.ProfilesDirectory, id + ".json*"), Is.Empty);
			Assert.That(freshReporter.Recoveries, Is.Empty);
		});
	}

	[Test]
	public void LoadAll_RecoversALegacyFormattedBackup_WithoutRewritingIt()
	{
		var id = Guid.NewGuid();
		var folderId = Guid.NewGuid();
		var path = PrimaryPathFor(id);
		var legacyJson = $$"""
						   {
						   	"Id": "{{id}}",
						   	"Name": "Legacy",
						   	"Order": 1,
						   	"LayoutType": "Grid",
						   	"DefaultRows": 3,
						   	"DefaultColumns": 5,
						   	"LegacyOnlyField": "keep-me",
						   	"Folders": [
						   		{
						   			"Id": "{{folderId}}",
						   			"Name": "Main",
						   			"Order": 0,
						   			"Rows": 3,
						   			"Columns": 5,
						   			"Widgets": [],
						   		},
						   	],
						   }
						   """;
		Directory.CreateDirectory(_paths.ProfilesDirectory);
		File.WriteAllText(path + ".bak", legacyJson);
		File.WriteAllText(path, $$"""{"id":"{{id}}","name":"V""");

		var loaded = _store.LoadAll().Profiles;

		Assert.That(loaded, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(loaded[0].Id, Is.EqualTo(id));
			Assert.That(loaded[0].Name, Is.EqualTo("Legacy"));
			Assert.That(loaded[0].LayoutType, Is.EqualTo(ProfileLayoutType.Grid));
			Assert.That(loaded[0].DefaultColumns, Is.EqualTo(5));
			Assert.That(loaded[0].Folders, Has.Count.EqualTo(1));
			Assert.That(loaded[0].Folders[0].Name, Is.EqualTo("Main"));
			Assert.That(loaded[0].Folders[0].WidgetSpacing, Is.Null);
			Assert.That(loaded[0].Folders[0].WidgetBorderRadius, Is.Null);
			Assert.That(loaded[0].DefaultWidgetSpacing, Is.Null);
			Assert.That(loaded[0].DefaultWidgetBorderRadius, Is.Null);
			Assert.That(File.ReadAllText(path), Does.Contain("LegacyOnlyField"));
		});
	}

	private string PrimaryPathFor(Guid id) => Path.Combine(_paths.ProfilesDirectory, id + ".json");

	private static string ProfileJson(Guid id, string name) => $$"""{"id":"{{id}}","name":"{{name}}"}""";
}
