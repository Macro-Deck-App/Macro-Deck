using System.Reflection;
using System.Text.Json;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Packaging.Tests.UnitTests.Manifest;

/// <summary>
/// Keeps the published manifest schema and the host's own model from drifting apart. A schema that
/// describes fields the host does not read, or omits fields it does, is worse than no schema at all:
/// plugin authors would validate against it and still be rejected, or be told a field is unsupported
/// when it is not.
/// </summary>
[TestFixture]
internal sealed class PluginManifestSchemaTests
{
	private static readonly string[] _expectedRequired =
		["manifestVersion", "id", "name", "version", "entrypoints"];

	private JsonDocument _schema = null!;

	[SetUp]
	public void SetUp() => _schema = JsonDocument.Parse(File.ReadAllText(SchemaPath()));

	[TearDown]
	public void TearDown() => _schema.Dispose();

	private static string SchemaPath()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null)
		{
			var candidate = Path.Combine(directory.FullName,
				"docs",
				"public",
				"schemas",
				"plugin-manifest-v1.schema.json");

			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		throw new FileNotFoundException("Could not find plugin-manifest-v1.schema.json above the test output.");
	}

	/// <summary>Mirrors the camelCase naming policy the host serializes on-disk JSON with.</summary>
	private static string CamelCase(string name)
		=> JsonNamingPolicy.CamelCase.ConvertName(name);

	private static IEnumerable<string> SerializedPropertyNames(Type type)
		=> type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Select(property => CamelCase(property.Name));

	private static IEnumerable<string> RequiredPropertyNames(Type type)
		=> type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(property => property.GetCustomAttributes()
				.Any(attribute => attribute.GetType().Name == "RequiredMemberAttribute"))
			.Select(property => CamelCase(property.Name));

	private JsonElement Definition(string name)
	{
		Assert.That(_schema.RootElement.TryGetProperty("$defs", out var defs), Is.True);
		Assert.That(defs.TryGetProperty(name, out var definition),
			Is.True,
			$"The schema has no $defs entry named '{name}'.");

		return definition;
	}

	private static List<string> DeclaredProperties(JsonElement objectSchema)
	{
		Assert.That(objectSchema.TryGetProperty("properties", out var properties), Is.True);

		return properties.EnumerateObject().Select(property => property.Name).ToList();
	}

	private static void AssertPropertiesMatch(JsonElement objectSchema, Type type, string label)
	{
		var schemaProperties = DeclaredProperties(objectSchema).Order(StringComparer.Ordinal).ToList();
		var modelProperties = SerializedPropertyNames(type).Order(StringComparer.Ordinal).ToList();

		Assert.That(schemaProperties,
			Is.EqualTo(modelProperties),
			$"The schema's '{label}' properties do not match {type.Name}.");
	}

	[Test]
	public void The_manifest_object_declares_exactly_the_model_properties()
	{
		AssertPropertiesMatch(_schema.RootElement, typeof(PluginManifest), "manifest");
	}

	[TestCase("entrypoint", typeof(PluginEntrypoint))]
	[TestCase("entrypointRuntime", typeof(PluginEntrypointRuntime))]
	[TestCase("publisher", typeof(PluginPublisher))]
	[TestCase("compatibility", typeof(PluginCompatibility))]
	[TestCase("dependency", typeof(PluginDependency))]
	[TestCase("iconPackReference", typeof(PluginIconPackReference))]
	[TestCase("fileDigest", typeof(PluginFileDigest))]
	[TestCase("signature", typeof(PluginSignature))]
	[TestCase("shutdownSettings", typeof(PluginShutdownSettings))]
	[TestCase("healthSettings", typeof(PluginHealthSettings))]
	[TestCase("protocolVersionRange", typeof(ProtocolVersionRange))]
	public void Each_nested_object_declares_exactly_the_model_properties(string definitionName, Type type)
	{
		AssertPropertiesMatch(Definition(definitionName), type, definitionName);
	}

	/// <summary>
	/// The one thing a manifest author is most likely to get wrong is believing a field is mandatory
	/// because the schema says so. Only the five fields the reader has always demanded may be required.
	/// </summary>
	[Test]
	public void Only_the_pre_existing_fields_are_required()
	{
		Assert.That(_schema.RootElement.GetProperty("required").EnumerateArray()
				.Select(element => element.GetString()),
			Is.EquivalentTo(_expectedRequired));
	}

	[Test]
	public void The_manifests_required_set_matches_the_models_required_members()
	{
		var schemaRequired = _schema.RootElement.GetProperty("required").EnumerateArray()
			.Select(element => element.GetString()!)
			.Order(StringComparer.Ordinal);

		Assert.That(schemaRequired,
			Is.EqualTo(RequiredPropertyNames(typeof(PluginManifest)).Order(StringComparer.Ordinal)));
	}

	[TestCase("entrypoint", typeof(PluginEntrypoint))]
	[TestCase("publisher", typeof(PluginPublisher))]
	[TestCase("dependency", typeof(PluginDependency))]
	[TestCase("iconPackReference", typeof(PluginIconPackReference))]
	[TestCase("fileDigest", typeof(PluginFileDigest))]
	[TestCase("signature", typeof(PluginSignature))]
	public void Each_nested_objects_required_set_matches_its_models_required_members(string definitionName,
		Type type)
	{
		var definition = Definition(definitionName);
		var schemaRequired = definition.TryGetProperty("required", out var required)
			? required.EnumerateArray().Select(element => element.GetString()!).Order(StringComparer.Ordinal).ToList()
			: new List<string>();

		Assert.That(schemaRequired,
			Is.EqualTo(RequiredPropertyNames(type).Order(StringComparer.Ordinal)),
			$"The schema's '{definitionName}' required set does not match {type.Name}.");
	}

	[Test]
	public void The_runtime_kind_enum_lists_exactly_the_model_members()
	{
		var kinds = Definition("entrypointRuntime").GetProperty("properties").GetProperty("kind")
			.GetProperty("enum").EnumerateArray().Select(element => element.GetString());

		Assert.That(kinds, Is.EquivalentTo(Enum.GetNames<PluginEntrypointRuntimeKind>()));
	}

	/// <summary>Catches the reader and the CLI disagreeing about how long a name may be - the CLI
	/// validates against this schema, the host against <c>PluginManifestReader.MaxNameLength</c>.</summary>
	[Test]
	public void The_schema_bounds_the_name_at_128_characters()
	{
		var name = _schema.RootElement.GetProperty("properties").GetProperty("name");

		Assert.Multiple(() =>
		{
			Assert.That(name.GetProperty("maxLength").GetInt32(), Is.EqualTo(128));
			Assert.That(name.GetProperty("minLength").GetInt32(), Is.EqualTo(1));
		});
	}

	[Test]
	public void The_manifest_version_is_pinned_to_the_one_the_reader_supports()
	{
		Assert.That(_schema.RootElement.GetProperty("properties").GetProperty("manifestVersion")
				.GetProperty("const").GetInt32(),
			Is.EqualTo(PluginManifest.SupportedManifestVersion));
	}

	/// <summary>
	/// The reader ignores unknown properties on purpose, so a manifest written for a newer host still
	/// loads. A schema forbidding them would tell authors the opposite of what the code does.
	/// </summary>
	[Test]
	public void No_object_in_the_schema_forbids_unknown_properties()
	{
		var offenders = new List<string>();
		Walk(_schema.RootElement, "$", offenders);

		Assert.That(offenders,
			Is.Empty,
			"additionalProperties: false contradicts the reader's forward compatibility.");

		static void Walk(JsonElement element, string path, List<string> offenders)
		{
			if (element.ValueKind == JsonValueKind.Object)
			{
				foreach (var property in element.EnumerateObject())
				{
					if (property.NameEquals("additionalProperties") &&
						property.Value.ValueKind == JsonValueKind.False)
					{
						offenders.Add(path);
					}

					Walk(property.Value, $"{path}.{property.Name}", offenders);
				}
			}
			else if (element.ValueKind == JsonValueKind.Array)
			{
				var index = 0;
				foreach (var item in element.EnumerateArray())
				{
					Walk(item, $"{path}[{index++}]", offenders);
				}
			}
		}
	}

	/// <summary>
	/// An unknown permission is deliberately an advisory warning rather than a rejection, so the schema
	/// must not close the list either. The known values belong in the description instead.
	/// </summary>
	[Test]
	public void The_permission_list_is_open_and_documents_the_known_vocabulary()
	{
		var permissions = _schema.RootElement.GetProperty("properties").GetProperty("permissions");
		var items = permissions.GetProperty("items");

		Assert.Multiple(() =>
		{
			Assert.That(items.TryGetProperty("enum", out _),
				Is.False,
				"a closed enum would make an unknown permission invalid.");

			var description = permissions.GetProperty("description").GetString() ?? string.Empty;
			foreach (var known in PluginPermissions.All)
			{
				Assert.That(description, Does.Contain(known));
			}
		});
	}
}
