using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Persistence;

[TestFixture]
public class JsonUserVariableStoreTests
{
	private TestPaths _paths = null!;
	private JsonUserVariableStore _store = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		_store = new JsonUserVariableStore(_paths, new LoggerConfiguration().CreateLogger());
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public void Save_WritesCamelCasePropertiesAndStringEnums()
	{
		_store.Save([
			new VariableEntity
			{
				Id = Guid.NewGuid(),
				Name = "MyVar",
				Scope = VariableScope.Widget,
				Type = VariableType.Numeric,
				Classification = VariableClassification.User,
				Value = "42"
			}
		]);

		var json = File.ReadAllText(Path.Combine(_paths.DataDirectory, "user-variables.json"));
		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"name\""));
			Assert.That(json, Does.Contain("\"scope\""));
			Assert.That(json, Does.Contain("\"classification\""));
			Assert.That(json, Does.Not.Contain("\"Name\""));
			Assert.That(json, Does.Not.Contain("\"Scope\""));

			Assert.That(json, Does.Contain("\"Widget\""));
			Assert.That(json, Does.Contain("\"Numeric\""));
			Assert.That(json, Does.Contain("\"User\""));
		});
	}

	[Test]
	public void Load_ReadsAScopePersistedAsTheActionButtonEnumName()
	{
		// Every file written before the widget scope was generalized spells it "ActionButton". Both entries
		// matter: a converter that mapped anything not spelled "Global" to Widget would pass on its own.
		var scopedId = Guid.NewGuid();
		var globalId = Guid.NewGuid();
		var json = $$"""
					 [
					 	{
					 		"id": "{{scopedId}}",
					 		"name": "toggled",
					 		"scope": "ActionButton",
					 		"scopeRefId": "11111111-1111-1111-1111-111111111111",
					 		"type": "Boolean",
					 		"classification": "User",
					 		"value": "true"
					 	},
					 	{
					 		"id": "{{globalId}}",
					 		"name": "greeting",
					 		"scope": "Global",
					 		"type": "Text",
					 		"classification": "User",
					 		"value": "hello"
					 	}
					 ]
					 """;
		Directory.CreateDirectory(_paths.DataDirectory);
		File.WriteAllText(Path.Combine(_paths.DataDirectory, "user-variables.json"), json);

		var loaded = _store.Load();

		Assert.That(loaded, Has.Count.EqualTo(2));
		Assert.Multiple(() =>
		{
			Assert.That(loaded.Single(v => v.Id == scopedId).Scope, Is.EqualTo(VariableScope.Widget));
			Assert.That(loaded.Single(v => v.Id == globalId).Scope, Is.EqualTo(VariableScope.Global));
		});
	}

	[Test]
	public void Load_ReadsLegacyFileWithIntegerEnumsAndTrailingCommas()
	{
		var id = Guid.NewGuid();
		var legacyJson = $$"""
						   [
						   	{
						   		"Id": "{{id}}",
						   		"Name": "MyVar",
						   		"Scope": 1,
						   		"ScopeRefId": null,
						   		"Type": 1,
						   		"Classification": 1,
						   		"Value": "42",
						   		"CreatedAt": "2024-01-01T00:00:00Z",
						   		"UpdatedAt": "2024-01-01T00:00:00Z",
						   	},
						   ]
						   """;
		Directory.CreateDirectory(_paths.DataDirectory);
		File.WriteAllText(Path.Combine(_paths.DataDirectory, "user-variables.json"), legacyJson);

		var loaded = _store.Load();

		Assert.That(loaded, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(loaded[0].Id, Is.EqualTo(id));
			Assert.That(loaded[0].Name, Is.EqualTo("MyVar"));
			Assert.That(loaded[0].Scope, Is.EqualTo(VariableScope.Widget));
			Assert.That(loaded[0].Type, Is.EqualTo(VariableType.Numeric));
			Assert.That(loaded[0].Classification, Is.EqualTo(VariableClassification.User));
			Assert.That(loaded[0].Value, Is.EqualTo("42"));
		});
	}

	[Test]
	public void Load_ReadsLegacyFileWithoutDefinitionIdAndDoesNotReintroduceItOnSave()
	{
		var id = Guid.NewGuid();
		var legacyJson = $$"""
						   [
						   	{
						   		"Id": "{{id}}",
						   		"Name": "MyVar",
						   		"Scope": 1,
						   		"ScopeRefId": null,
						   		"Type": 1,
						   		"Classification": 1,
						   		"Value": "42",
						   		"CreatedAt": "2024-01-01T00:00:00Z",
						   		"UpdatedAt": "2024-01-01T00:00:00Z"
						   	}
						   ]
						   """;
		var path = Path.Combine(_paths.DataDirectory, "user-variables.json");
		Directory.CreateDirectory(_paths.DataDirectory);
		File.WriteAllText(path, legacyJson);

		var loaded = _store.Load();
		Assert.That(loaded[0].DefinitionId, Is.Null);

		_store.Save(loaded);

		Assert.That(File.ReadAllText(path), Does.Not.Contain("definitionId"));
	}

	[Test]
	public void Load_RecoversFromTheBackup_WhenThePrimaryIsEmpty()
	{
		var path = Path.Combine(_paths.DataDirectory, "user-variables.json");
		Directory.CreateDirectory(_paths.DataDirectory);
		File.WriteAllText(path, string.Empty);
		var firstId = Guid.NewGuid();
		var secondId = Guid.NewGuid();
		var backupJson = $$"""
						   [
						   	{
						   		"id": "{{firstId}}",
						   		"name": "First",
						   		"scope": "ActionButton",
						   		"type": "Numeric",
						   		"classification": "User",
						   		"value": "1"
						   	},
						   	{
						   		"id": "{{secondId}}",
						   		"name": "Second",
						   		"scope": "ActionButton",
						   		"type": "Numeric",
						   		"classification": "User",
						   		"value": "2"
						   	}
						   ]
						   """;
		File.WriteAllText(path + ".bak", backupJson);

		var loaded = _store.Load();

		Assert.That(loaded, Has.Count.EqualTo(2));
		Assert.That(loaded.Select(v => v.Name), Is.EquivalentTo(["First", "Second"]));
	}
}
