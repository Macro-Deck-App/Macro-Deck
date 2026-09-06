using MacroDeckHost.Application.Persistence.Profiles;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Persistence;

[TestFixture]
public class JsonProfileStoreTests
{
	private TestPaths _paths = null!;
	private JsonProfileStore _store = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		_store = new JsonProfileStore(_paths, new LoggerConfiguration().CreateLogger());
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public void Save_ThenLoadAll_RoundTripsProfileWithFoldersAndWidgets()
	{
		var profile = new ProfileFile
		{
			Id = Guid.NewGuid(),
			Name = "Gaming",
			Order = 2,
			LayoutType = ProfileLayoutType.Grid,
			DefaultRows = 4,
			DefaultColumns = 6,
			DefaultBackgroundColor = "#101010",
			Folders =
			[
				new ProfileFolder
				{
					Id = Guid.NewGuid(),
					Name = "Main",
					Order = 0,
					Rows = 4,
					Columns = 6,
					WidgetSpacing = 20,
					WidgetBorderRadius = 30,
					Widgets =
					[
						new ProfileWidget
						{
							Id = Guid.NewGuid(),
							Type = WidgetTypeIds.ActionButton,
							PositionX = 1,
							PositionY = 2,
							Width = 2,
							Height = 1,
							Data = "{\"label\":\"Hi\"}"
						}
					]
				}
			]
		};

		_store.Save(profile);
		var loaded = _store.LoadAll().Profiles;

		Assert.That(loaded, Has.Count.EqualTo(1));
		var roundTripped = loaded[0];
		Assert.Multiple(() =>
		{
			Assert.That(roundTripped.Id, Is.EqualTo(profile.Id));
			Assert.That(roundTripped.Name, Is.EqualTo("Gaming"));
			Assert.That(roundTripped.LayoutType, Is.EqualTo(ProfileLayoutType.Grid));
			Assert.That(roundTripped.DefaultColumns, Is.EqualTo(6));
			Assert.That(roundTripped.Folders, Has.Count.EqualTo(1));
			Assert.That(roundTripped.Folders[0].WidgetSpacing, Is.EqualTo(20));
			Assert.That(roundTripped.Folders[0].WidgetBorderRadius, Is.EqualTo(30));
			Assert.That(roundTripped.Folders[0].Widgets, Has.Count.EqualTo(1));
			Assert.That(roundTripped.Folders[0].Widgets[0].Data, Is.EqualTo("{\"label\":\"Hi\"}"));
		});
	}

	[Test]
	public void Save_ThenLoadAll_RoundTripsAWidgetOfATypeNothingProvides_WithoutCoercingItToActionButton()
	{
		const string unknownType = "com.example.gauges::gauge";
		const string data = "{\"min\":0,\"max\":100}";

		var profile = new ProfileFile
		{
			Id = Guid.NewGuid(),
			Name = "Gaming",
			Folders =
			[
				new ProfileFolder
				{
					Id = Guid.NewGuid(),
					Name = "Main",
					Rows = 4,
					Columns = 4,
					Widgets =
					[
						new ProfileWidget
						{
							Id = Guid.NewGuid(),
							Type = unknownType,
							PositionX = 0,
							PositionY = 0,
							Width = 1,
							Height = 1,
							Data = data
						}
					]
				}
			]
		};

		_store.Save(profile);
		var widget = _store.LoadAll().Profiles.Single().Folders.Single().Widgets.Single();

		Assert.Multiple(() =>
		{
			Assert.That(widget.Type, Is.EqualTo(unknownType));
			Assert.That(widget.Data, Is.EqualTo(data));
		});
	}

	[Test]
	public void Save_WritesCamelCasePropertyNames()
	{
		var profile = new ProfileFile
		{
			Id = Guid.NewGuid(),
			Name = "Gaming",
			LayoutType = ProfileLayoutType.Grid,
			DefaultColumns = 6
		};

		_store.Save(profile);

		var json = File.ReadAllText(Path.Combine(_paths.ProfilesDirectory, profile.Id + ".json"));
		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"name\""));
			Assert.That(json, Does.Contain("\"layoutType\""));
			Assert.That(json, Does.Contain("\"defaultColumns\""));
			Assert.That(json, Does.Not.Contain("\"Name\""));
			Assert.That(json, Does.Not.Contain("\"LayoutType\""));
			Assert.That(json, Does.Not.Contain("\"DefaultColumns\""));

			Assert.That(json, Does.Contain("\"Grid\""));
		});
	}

	[Test]
	public void LoadAll_ReadsLegacyPascalCaseFileWithTrailingCommas()
	{
		var id = Guid.NewGuid();
		var folderId = Guid.NewGuid();
		var legacyJson = $$"""
						   {
						   	"Id": "{{id}}",
						   	"Name": "Legacy",
						   	"Order": 1,
						   	"LayoutType": "Grid",
						   	"DefaultRows": 3,
						   	"DefaultColumns": 5,
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
		File.WriteAllText(Path.Combine(_paths.ProfilesDirectory, id + ".json"), legacyJson);

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
		});
	}

	[Test]
	public void LoadAll_ReadsAPreIssue245WidgetWithNoScopeProperty_AsProfileScope()
	{
		var id = Guid.NewGuid();
		var folderId = Guid.NewGuid();
		var widgetId = Guid.NewGuid();
		var legacyJson = $$"""
						   {
						   	"id": "{{id}}",
						   	"name": "Legacy",
						   	"defaultRows": 4,
						   	"defaultColumns": 4,
						   	"folders": [
						   		{
						   			"id": "{{folderId}}",
						   			"name": "Main",
						   			"rows": 4,
						   			"columns": 4,
						   			"widgets": [
						   				{
						   					"id": "{{widgetId}}",
						   					"type": "ActionButton",
						   					"positionX": 0,
						   					"positionY": 0,
						   					"width": 1,
						   					"height": 1,
						   					"isPinned": true
						   				}
						   			]
						   		}
						   	]
						   }
						   """;
		Directory.CreateDirectory(_paths.ProfilesDirectory);
		File.WriteAllText(Path.Combine(_paths.ProfilesDirectory, id + ".json"), legacyJson);

		var loaded = _store.LoadAll().Profiles;

		var widget = loaded.Single().Folders.Single().Widgets.Single();
		Assert.Multiple(() =>
		{
			Assert.That(widget.IsPinned, Is.True);
			Assert.That(widget.PinScope, Is.EqualTo(PinScope.Profile));
		});
	}

	[Test]
	public void Save_ThenLoadAll_AnInheritedGridIsAbsentFromTheJsonAndReadsBackAsNull()
	{
		var profile = new ProfileFile
		{
			Id = Guid.NewGuid(),
			Name = "Gaming",
			Folders = [new ProfileFolder { Id = Guid.NewGuid(), Name = "Main", Rows = null, Columns = null }]
		};

		_store.Save(profile);

		var json = File.ReadAllText(Path.Combine(_paths.ProfilesDirectory, profile.Id + ".json"));
		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Not.Contain("\"rows\""));
			Assert.That(json, Does.Not.Contain("\"columns\""));
		});

		var loaded = _store.LoadAll().Profiles.Single().Folders.Single();
		Assert.Multiple(() =>
		{
			Assert.That(loaded.Rows, Is.Null);
			Assert.That(loaded.Columns, Is.Null);
		});
	}

	[Test]
	public void Save_AProfileScopedPin_OmitsPinScopeFromTheJson()
	{
		var profile = new ProfileFile
		{
			Id = Guid.NewGuid(),
			Name = "Gaming",
			Folders =
			[
				new ProfileFolder
				{
					Id = Guid.NewGuid(),
					Name = "Main",
					Rows = 4,
					Columns = 4,
					Widgets =
					[
						new ProfileWidget
						{
							Id = Guid.NewGuid(),
							Type = WidgetTypeIds.ActionButton,
							Width = 1,
							Height = 1,
							IsPinned = true,
							PinScope = PinScope.Profile
						}
					]
				}
			]
		};

		_store.Save(profile);

		var json = File.ReadAllText(Path.Combine(_paths.ProfilesDirectory, profile.Id + ".json"));
		Assert.That(json, Does.Not.Contain("pinScope"));
	}

	[Test]
	public void Save_ThenLoadAll_ASubtreeScopedPin_RoundTrips()
	{
		var widgetId = Guid.NewGuid();
		var profile = new ProfileFile
		{
			Id = Guid.NewGuid(),
			Name = "Gaming",
			Folders =
			[
				new ProfileFolder
				{
					Id = Guid.NewGuid(),
					Name = "Main",
					Rows = 4,
					Columns = 4,
					Widgets =
					[
						new ProfileWidget
						{
							Id = widgetId,
							Type = WidgetTypeIds.ActionButton,
							Width = 1,
							Height = 1,
							IsPinned = true,
							PinScope = PinScope.Subtree
						}
					]
				}
			]
		};

		_store.Save(profile);

		var json = File.ReadAllText(Path.Combine(_paths.ProfilesDirectory, profile.Id + ".json"));
		Assert.That(json, Does.Contain("\"pinScope\""));

		var reloaded = _store.LoadAll().Profiles.Single().Folders.Single().Widgets.Single(w => w.Id == widgetId);
		Assert.That(reloaded.PinScope, Is.EqualTo(PinScope.Subtree));
	}

	[Test]
	public void Save_WritesAtomically_WithoutLeavingTempFile()
	{
		var id = Guid.NewGuid();
		_store.Save(new ProfileFile { Id = id, Name = "X" });

		Assert.Multiple(() =>
		{
			Assert.That(File.Exists(Path.Combine(_paths.ProfilesDirectory, id + ".json")), Is.True);
			Assert.That(Directory.GetFiles(_paths.ProfilesDirectory, "*.tmp"), Is.Empty);
		});
	}

	[Test]
	public void LoadAll_SkipsCorruptFile()
	{
		var good = new ProfileFile { Id = Guid.NewGuid(), Name = "Good" };
		_store.Save(good);
		File.WriteAllText(Path.Combine(_paths.ProfilesDirectory, "broken.json"), "{ not valid json");

		var loaded = _store.LoadAll();

		Assert.That(loaded.Profiles.Select(p => p.Id), Is.EquivalentTo([good.Id]));
	}

	[Test]
	public void LoadAll_CountsATruncatedProfileWithNoBackupOrTempFile_AsUnreadable()
	{
		var id = Guid.NewGuid();
		Directory.CreateDirectory(_paths.ProfilesDirectory);
		File.WriteAllText(Path.Combine(_paths.ProfilesDirectory, id + ".json"), string.Empty);

		var loaded = _store.LoadAll();

		Assert.Multiple(() =>
		{
			Assert.That(loaded.Profiles, Is.Empty);
			Assert.That(loaded.UnreadableCount, Is.EqualTo(1));
		});
	}

	[Test]
	public void Delete_RemovesFile()
	{
		var id = Guid.NewGuid();
		_store.Save(new ProfileFile { Id = id, Name = "X" });

		_store.Delete(id);

		Assert.That(_store.LoadAll().Profiles, Is.Empty);
	}

	[Test]
	public void Save_ReturnsTrue_OnSuccess()
	{
		var result = _store.Save(new ProfileFile { Id = Guid.NewGuid(), Name = "X" });

		Assert.That(result, Is.True);
	}

	[Test]
	public void Save_ReturnsFalseAndLeavesThePreviousFileIntact_WhenTheWriteFails()
	{
		var id = Guid.NewGuid();
		_store.Save(new ProfileFile { Id = id, Name = "Original" });
		var path = Path.Combine(_paths.ProfilesDirectory, id + ".json");
		var originalContent = File.ReadAllText(path);

		Directory.CreateDirectory(path + ".tmp");

		var result = _store.Save(new ProfileFile { Id = id, Name = "Changed" });

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.False);
			Assert.That(File.ReadAllText(path), Is.EqualTo(originalContent));
		});
	}
}
