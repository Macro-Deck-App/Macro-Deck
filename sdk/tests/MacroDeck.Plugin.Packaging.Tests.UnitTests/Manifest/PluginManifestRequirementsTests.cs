using System.Reflection;
using System.Text.Json;
using Json.Schema;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Packaging.Tests.UnitTests.Manifest;

/// <summary>
/// <see cref="PluginManifestRequirements"/> is the single declared contract for which manifest fields are
/// required at which <see cref="PluginManifestValidationLevel"/>. These tests protect its two governing
/// guarantees: every model field is classified (nothing silently falls through to "recommended"), and
/// <see cref="PluginManifestRequirements.Evaluate"/> never gets stricter than <see cref="IPluginManifestReader"/>
/// at <see cref="PluginManifestValidationLevel.Development"/>.
/// </summary>
[TestFixture]
internal sealed class PluginManifestRequirementsTests
{
	private static readonly string[] _sixPublicationPointers =
		["/publisher", "/publisher/name", "/description", "/icon", "/license", "/repository", "/compatibility"];

	private static readonly string[] _publicationGapPointers =
		["/publisher", "/description", "/icon", "/license", "/repository", "/compatibility"];

	private static readonly string[] _filesAndSignaturePointers = ["/files", "/signature"];

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

	private static PluginManifest MinimalManifest() => new()
	{
		ManifestVersion = 1,
		Id = "com.example.minimal",
		Name = "Minimal Plugin",
		Version = "1.0.0",
		Entrypoints = new Dictionary<string, PluginEntrypoint>
		{
			["win-x64"] = new() { Executable = "MinimalPlugin.exe" }
		}
	};

	private static PluginManifest PublicationCompleteManifest() => MinimalManifest() with
	{
		Description = "Does the minimal thing.",
		Icon = "icon.png",
		License = "MIT",
		Repository = "https://github.com/example/minimal",
		Compatibility = new PluginCompatibility { MacroDeck = ">=3.0.0" },
		Publisher = new PluginPublisher { Name = "Example Publisher" }
	};

	// --- Scenario 1 ---------------------------------------------------------------------------------

	/// <summary>Reflects the manifest model so a field added to the model and forgotten in the schema
	/// annotations is caught mechanically rather than relying on a reviewer noticing.</summary>
	private static IEnumerable<string> ModelPointers()
	{
		var camelCase = JsonNamingPolicy.CamelCase;

		string Camel(string name) => camelCase.ConvertName(name);

		IEnumerable<string> Walk(Type type, string basePointer)
		{
			foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
			{
				var pointer = basePointer + "/" + Camel(property.Name);
				yield return pointer;

				var propertyType = property.PropertyType;
				var elementType = propertyType.IsGenericType &&
					(propertyType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
						? propertyType.GetGenericArguments()[0]
						: propertyType.IsGenericType &&
						propertyType.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
							? propertyType.GetGenericArguments()[1]
							: Nullable.GetUnderlyingType(propertyType) ?? propertyType;

				if (elementType.IsClass &&
					elementType != typeof(string) &&
					elementType.Namespace is
						"MacroDeck.Plugin.Packaging.Manifest" or "MacroDeck.Plugin.Protocol.Versioning")
				{
					var isCollection = propertyType.IsGenericType &&
						(propertyType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) ||
							propertyType.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));

					foreach (var nested in Walk(elementType, isCollection ? pointer + "/*" : pointer))
					{
						yield return nested;
					}
				}
			}
		}

		return Walk(typeof(PluginManifest), string.Empty);
	}

	[Test]
	public void Every_manifest_field_is_categorised_by_the_requirement_table()
	{
		var modelPointers = ModelPointers().ToList();

		Assert.Multiple(() =>
		{
			foreach (var pointer in modelPointers)
			{
				Assert.That(PluginManifestRequirements.TryGetRequirement(pointer, out _),
					Is.True,
					$"'{pointer}' falls through to the Recommended default instead of being classified.");
			}
		});

		var ruleTargetsAModelField = PluginManifestRequirements.All
			.Select(rule => rule.PointerPattern)
			.All(pattern => modelPointers.Any(pointer => PointerMatchesPattern(pointer, pattern)));

		Assert.That(ruleTargetsAModelField, Is.True, "A rule in All names a pointer no model property produces.");
	}

	private static bool PointerMatchesPattern(string pointer, string pattern)
	{
		var pointerSegments = pointer.Split('/');
		var patternSegments = pattern.Split('/');

		if (pointerSegments.Length != patternSegments.Length)
		{
			return false;
		}

		return pointerSegments.Zip(patternSegments)
			.All(pair => pair.Second == "*" || pair.Second == pair.First);
	}

	// --- Scenario 2 ---------------------------------------------------------------------------------

	[Test]
	public void The_six_publication_required_pointers_are_exactly_the_agreed_set()
	{
		var publicationPointers = PluginManifestRequirements.All
			.Where(rule => rule.Requirement == PluginManifestFieldRequirement.Publication)
			.Select(rule => rule.PointerPattern)
			.ToList();

		Assert.That(publicationPointers, Is.EquivalentTo(_sixPublicationPointers));
	}

	// --- Scenario 3 ---------------------------------------------------------------------------------

	[Test]
	public void Publisher_id_and_homepage_stay_recommended_while_publisher_name_does_not()
	{
		Assert.Multiple(() =>
		{
			Assert.That(PluginManifestRequirements.For("/publisher/name"),
				Is.EqualTo(PluginManifestFieldRequirement.Publication));
			Assert.That(PluginManifestRequirements.For("/publisher/id"),
				Is.EqualTo(PluginManifestFieldRequirement.Recommended));
			Assert.That(PluginManifestRequirements.For("/publisher/email"),
				Is.EqualTo(PluginManifestFieldRequirement.Recommended));
			Assert.That(PluginManifestRequirements.For("/publisher/url"),
				Is.EqualTo(PluginManifestFieldRequirement.Recommended));
			Assert.That(PluginManifestRequirements.For("/homepage"),
				Is.EqualTo(PluginManifestFieldRequirement.Recommended));
			Assert.That(PluginManifestRequirements.For("/repository"),
				Is.EqualTo(PluginManifestFieldRequirement.Publication));
		});
	}

	// --- Scenario 4 ---------------------------------------------------------------------------------

	[Test]
	public void Files_and_signature_are_generated_and_entrypoint_executable_is_runtime()
	{
		Assert.Multiple(() =>
		{
			Assert.That(PluginManifestRequirements.For("/files"), Is.EqualTo(PluginManifestFieldRequirement.Generated));
			Assert.That(PluginManifestRequirements.For("/signature"),
				Is.EqualTo(PluginManifestFieldRequirement.Generated));

			Assert.That(PluginManifestRequirements.For("/manifestVersion"),
				Is.EqualTo(PluginManifestFieldRequirement.Runtime));
			Assert.That(PluginManifestRequirements.For("/id"), Is.EqualTo(PluginManifestFieldRequirement.Runtime));
			Assert.That(PluginManifestRequirements.For("/name"), Is.EqualTo(PluginManifestFieldRequirement.Runtime));
			Assert.That(PluginManifestRequirements.For("/version"), Is.EqualTo(PluginManifestFieldRequirement.Runtime));
			Assert.That(PluginManifestRequirements.For("/entrypoints"),
				Is.EqualTo(PluginManifestFieldRequirement.Runtime));

			Assert.That(PluginManifestRequirements.For("/entrypoints/win-x64/executable"),
				Is.EqualTo(PluginManifestFieldRequirement.Runtime));
			Assert.That(PluginManifestRequirements.For("/entrypoints/win-x64/arguments"),
				Is.EqualTo(PluginManifestFieldRequirement.Recommended));

			Assert.That(PluginManifestRequirements.For("/signature/value"),
				Is.EqualTo(PluginManifestFieldRequirement.Generated));
		});
	}

	// --- Scenario 5 ---------------------------------------------------------------------------------

	[TestCase("/notAField")]
	[TestCase("")]
	[TestCase("/")]
	[TestCase("entrypoints/win-x64")]
	[TestCase("/entrypoints/win-x64/~0weird")]
	public void An_unknown_pointer_is_recommended_and_never_throws(string pointer)
	{
		Assert.Multiple(() =>
		{
			Assert.That(() => PluginManifestRequirements.For(pointer), Throws.Nothing);
			Assert.That(PluginManifestRequirements.For(pointer),
				Is.EqualTo(PluginManifestFieldRequirement.Recommended));
			Assert.That(PluginManifestRequirements.TryGetRequirement(pointer, out var requirement), Is.False);
			Assert.That(requirement, Is.EqualTo(PluginManifestFieldRequirement.Recommended));
		});
	}

	// --- Scenario 6 ---------------------------------------------------------------------------------

	[Test]
	public void The_minimal_five_field_manifest_has_no_violations_at_development_level()
	{
		var violations
			= PluginManifestRequirements.Evaluate(MinimalManifest(), PluginManifestValidationLevel.Development);

		Assert.That(violations, Is.Empty);
	}

	// --- Scenario 7 ---------------------------------------------------------------------------------

	[TestCase(PluginManifestValidationLevel.Package)]
	[TestCase(PluginManifestValidationLevel.Publication)]
	public void The_minimal_manifest_reports_every_publication_gap_at_package_and_publication_level(
		PluginManifestValidationLevel level)
	{
		var violations = PluginManifestRequirements.Evaluate(MinimalManifest(), level);

		Assert.That(violations, Has.Count.EqualTo(6));
		Assert.That(violations.Select(violation => violation.Pointer), Is.EquivalentTo(_publicationGapPointers));
		Assert.That(violations,
			Has.All.Matches<PluginManifestRequirementViolation>(violation =>
				violation.RequiredBy == PluginManifestValidationLevel.Publication));
		Assert.That(violations.Select(violation => violation.Pointer), Has.None.EqualTo("/publisher/name"));
	}

	// --- Scenario 8 ---------------------------------------------------------------------------------

	[Test]
	public void A_publisher_block_without_a_name_reports_the_name_pointer_not_the_block_pointer()
	{
		var manifest = MinimalManifest() with
		{
			Publisher = new PluginPublisher { Name = "", Id = "com.example" }
		};

		var violations = PluginManifestRequirements.Evaluate(manifest, PluginManifestValidationLevel.Publication);
		var publisherViolations = violations
			.Where(violation => violation.Pointer.StartsWith("/publisher", StringComparison.Ordinal))
			.ToList();

		Assert.That(publisherViolations, Has.Count.EqualTo(1));
		Assert.That(publisherViolations[0].Pointer, Is.EqualTo("/publisher/name"));
	}

	// --- Scenario 9 ---------------------------------------------------------------------------------

	[Test]
	public void Compatibility_is_satisfied_by_any_one_declared_member()
	{
		Assert.Multiple(() =>
		{
			Assert.That(EvaluateCompatibility(new PluginCompatibility { MacroDeck = ">=3.0.0" }), Is.Empty);
			Assert.That(EvaluateCompatibility(new PluginCompatibility
					{ Protocol = new ProtocolVersionRange { Minimum = 1, Maximum = 1 } }),
				Is.Empty);
			Assert.That(EvaluateCompatibility(new PluginCompatibility { Sdk = ">=1.0.0" }), Is.Empty);

			var withoutCompatibility = EvaluateCompatibility(null);
			Assert.That(withoutCompatibility, Has.Count.EqualTo(1));
			Assert.That(withoutCompatibility[0].Pointer, Is.EqualTo("/compatibility"));

			var withEmptyCompatibility = EvaluateCompatibility(new PluginCompatibility());
			Assert.That(withEmptyCompatibility, Has.Count.EqualTo(1));
			Assert.That(withEmptyCompatibility[0].Pointer, Is.EqualTo("/compatibility"));
		});
	}

	private static IReadOnlyList<PluginManifestRequirementViolation> EvaluateCompatibility(
		PluginCompatibility? compatibility)
	{
		var manifest = PublicationCompleteManifest() with { Compatibility = compatibility };
		return PluginManifestRequirements.Evaluate(manifest, PluginManifestValidationLevel.Publication);
	}

	// --- Scenario 10 ---------------------------------------------------------------------------------

	[Test]
	public void Evaluate_never_reports_a_payload_problem_because_it_never_sees_a_payload()
	{
		var manifest = PublicationCompleteManifest() with
		{
			Icon = "assets/does-not-exist.png",
			Entrypoints = new Dictionary<string, PluginEntrypoint>
			{
				["win-x64"] = new() { Executable = "runtimes/win-x64/Missing.exe" },
				["osx-arm64"] = new() { Executable = "runtimes/osx-arm64/Missing" },
				["linux-x64"] = new() { Executable = "runtimes/linux-x64/Missing" }
			}
		};

		var violations = PluginManifestRequirements.Evaluate(manifest, PluginManifestValidationLevel.Publication);

		Assert.That(violations, Is.Empty);
	}

	// --- Scenario 11 ---------------------------------------------------------------------------------

	[TestCase(PluginManifestValidationLevel.Development)]
	[TestCase(PluginManifestValidationLevel.Package)]
	[TestCase(PluginManifestValidationLevel.Publication)]
	public void A_hand_authored_files_or_signature_block_is_reported_as_generated_and_never_blocks(
		PluginManifestValidationLevel level)
	{
		var manifest = PublicationCompleteManifest() with
		{
			Files =
			[
				new PluginFileDigest
				{
					Path = "MinimalPlugin.exe", Sha256 = "sha256:" + new string('a', 64), Size = 1024
				}
			],
			Signature = new PluginSignature { Algorithm = "ed25519", KeyId = "key-1", Value = "AAAA" }
		};

		var violations = PluginManifestRequirements.Evaluate(manifest, level);

		Assert.Multiple(() =>
		{
			Assert.That(violations.Select(violation => violation.Pointer), Is.EquivalentTo(_filesAndSignaturePointers));
			Assert.That(violations,
				Has.All.Matches<PluginManifestRequirementViolation>(violation =>
					violation.Requirement == PluginManifestFieldRequirement.Generated));
			Assert.That(violations,
				Has.None.Matches<PluginManifestRequirementViolation>(violation =>
					violation.Requirement is PluginManifestFieldRequirement.Runtime
						or PluginManifestFieldRequirement.Publication));
		});
	}

	// --- Scenario 12 ---------------------------------------------------------------------------------

	[Test]
	public void Level_containment_holds_for_an_arbitrary_manifest()
	{
		var manifest = MinimalManifest() with
		{
			Publisher = new PluginPublisher { Name = "Example Publisher" },
			Icon = "icon.png",
			Repository = "https://github.com/example/thing"
		};

		var development = PluginManifestRequirements.Evaluate(manifest, PluginManifestValidationLevel.Development);
		var package = PluginManifestRequirements.Evaluate(manifest, PluginManifestValidationLevel.Package);
		var publication = PluginManifestRequirements.Evaluate(manifest, PluginManifestValidationLevel.Publication);

		var expectedGapPointers = new[] { "/description", "/license", "/compatibility" };

		Assert.Multiple(() =>
		{
			Assert.That(development, Is.Empty);
			Assert.That(package.Select(violation => violation.Pointer), Is.EquivalentTo(expectedGapPointers));
			Assert.That(publication.Select(violation => violation.Pointer), Is.EquivalentTo(expectedGapPointers));

			var developmentPointers = development.Select(violation => violation.Pointer).ToHashSet();
			var packagePointers = package.Select(violation => violation.Pointer).ToHashSet();
			var publicationPointers = publication.Select(violation => violation.Pointer).ToHashSet();

			Assert.That(developmentPointers.IsSubsetOf(packagePointers), Is.True);
			Assert.That(packagePointers.IsSubsetOf(publicationPointers), Is.True);
		});
	}

	// --- Scenario 13 ---------------------------------------------------------------------------------

	[Test]
	public void The_schema_annotations_and_the_requirement_table_agree()
	{
		using var schema = JsonDocument.Parse(File.ReadAllText(SchemaPath()));

		var schemaAnnotations = new Dictionary<string, string>();
		CollectAnnotations(schema.RootElement, string.Empty, schemaAnnotations);

		Assert.Multiple(() =>
		{
			foreach (var (pointer, requirement) in schemaAnnotations)
			{
				Assert.That(PluginManifestRequirements.For(pointer).ToString().ToLowerInvariant(),
					Is.EqualTo(requirement),
					$"Schema annotation at '{pointer}' disagrees with For('{pointer}').");
			}

			foreach (var pointer in ModelPointers())
			{
				Assert.That(schemaAnnotations.ContainsKey(pointer) ||
					PluginManifestRequirements.All.Any(rule => PointerMatchesPattern(pointer, rule.PointerPattern)),
					Is.True,
					$"Model pointer '{pointer}' has no schema annotation.");
			}
		});
	}

	private static void CollectAnnotations(JsonElement objectSchema,
		string basePointer,
		Dictionary<string, string> found)
	{
		if (!objectSchema.TryGetProperty("properties", out var properties))
		{
			return;
		}

		foreach (var property in properties.EnumerateObject())
		{
			var pointer = basePointer + "/" + property.Name;

			if (property.Value.TryGetProperty("x-macrodeck-requirement", out var requirement))
			{
				found[pointer] = requirement.GetString()!;
			}
		}
	}

	// --- Scenario 14 ---------------------------------------------------------------------------------

	[Test]
	public void The_annotated_schema_still_validates_the_documented_examples()
	{
		var schemaJson = File.ReadAllText(SchemaPath());
		var schema = JsonSchema.FromText(schemaJson);
		using var document = JsonDocument.Parse(schemaJson);

		var examples = document.RootElement.GetProperty("examples");

		Assert.Multiple(() =>
		{
			foreach (var example in examples.EnumerateArray())
			{
				var results = schema.Evaluate(example, new EvaluationOptions { OutputFormat = OutputFormat.List });
				Assert.That(results.IsValid,
					Is.True,
					string.Join(Environment.NewLine,
						(results.Details ?? []).Where(detail => !detail.IsValid)
						.SelectMany(detail => detail.Errors?.Values ?? Enumerable.Empty<string>())));
			}
		});
	}
}
