using System.Text.Json;

namespace MacroDeck.Plugin.Packaging.Manifest;

#pragma warning disable CA1720 // "pointer"/"Pointer" is the RFC 6901 term of art this whole contract is built on, matching MacroDeck.Plugin.Cli.Manifests.ManifestProblem.Pointer.

/// <summary>
/// How strictly a manifest is expected to be filled in. Each level is a superset of the checks the
/// previous one performs, so <c>level &gt;= X</c> is a meaningful comparison.
/// </summary>
public enum PluginManifestValidationLevel
{
	/// <summary>What <see cref="IPluginManifestReader"/> already enforces at install time. This is the
	/// floor: it never becomes stricter, so a manifest that runs locally today keeps running locally on
	/// every future version of this contract.</summary>
	Development,

	/// <summary>Development plus every <see cref="PluginManifestFieldRequirement.Publication"/> field.
	/// Intended for a local pack/build step that wants to warn early about what publication will demand.</summary>
	Package,

	/// <summary>Package plus nothing further today; the level a plugin must clear to be accepted into the
	/// public Macro Deck plugin ecosystem. Kept distinct from <see cref="Package"/> so a future, stricter
	/// publication-only rule has somewhere to live without becoming a Package-time warning too.</summary>
	Publication
}

/// <summary>The category a manifest field belongs to, as declared by the published JSON Schema's
/// <c>x-macrodeck-requirement</c> annotation.</summary>
public enum PluginManifestFieldRequirement
{
	/// <summary>Required for the plugin to install and run at all; <see cref="IPluginManifestReader"/>
	/// already rejects a manifest missing one of these.</summary>
	Runtime,

	/// <summary>Not required to develop or run the plugin locally, but required before it is accepted into
	/// the public Macro Deck plugin ecosystem.</summary>
	Publication,

	/// <summary>Never required at any level; filling it in only improves what the plugin can do or how it
	/// is presented.</summary>
	Recommended,

	/// <summary>Produced by the packaging pipeline itself (file digests, the detached signature), never by
	/// a plugin author. A hand-authored value here is never an error - <see cref="PluginManifestRequirements.Evaluate"/>
	/// only reports that the field is present, at every level, and leaves whether that is expected to the
	/// caller, which is the only place that also knows whether it is looking at a source manifest or an
	/// installed/packaged one.</summary>
	Generated
}

/// <summary>One row of the manifest requirement table, read from the published schema's
/// <c>x-macrodeck-requirement</c> annotations.</summary>
public sealed record PluginManifestFieldRule
{
	/// <summary>An RFC 6901 JSON pointer in which a literal <c>*</c> segment stands for any map key or
	/// array index, e.g. <c>/entrypoints/*/executable</c> or <c>/files/*/sha256</c>. Deliberately not
	/// named <c>Pointer</c>: a concrete RFC 6901 pointer may legally contain a literal <c>*</c> segment, and
	/// naming this the same thing would blur a pattern with a value it could be mistaken for.</summary>
	public required string PointerPattern { get; init; }

	public required PluginManifestFieldRequirement Requirement { get; init; }

	/// <summary>The field's schema <c>description</c>, carried here so a consumer of the table does not
	/// need to also parse the schema to explain a field to a plugin author.</summary>
	public required string Summary { get; init; }
}

/// <summary>One field of a manifest that falls short of the <see cref="RequiredBy"/> level's requirements,
/// or (for a <see cref="PluginManifestFieldRequirement.Generated"/> field) simply a report that the field
/// is present in a manifest that presumably should not be hand-authoring it.</summary>
public sealed record PluginManifestRequirementViolation
{
	/// <summary>A concrete, resolved RFC 6901 pointer (<c>~0</c>/<c>~1</c> escaped where needed) into the
	/// manifest that was evaluated - never a <see cref="PluginManifestFieldRule.PointerPattern"/>.</summary>
	public required string Pointer { get; init; }

	public required PluginManifestFieldRequirement Requirement { get; init; }

	/// <summary>The level that requires this field, not the level <see cref="PluginManifestRequirements.Evaluate"/>
	/// was called with. A gap in a <see cref="PluginManifestFieldRequirement.Publication"/> field always
	/// reports <see cref="PluginManifestValidationLevel.Publication"/> here, whether <c>Evaluate</c> was
	/// asked for <see cref="PluginManifestValidationLevel.Package"/> or <see cref="PluginManifestValidationLevel.Publication"/>.
	/// For a <see cref="PluginManifestFieldRequirement.Generated"/> report this is always
	/// <see cref="PluginManifestValidationLevel.Development"/>, reflecting that the fact is surfaced at
	/// every level rather than being required by one.</summary>
	public required PluginManifestValidationLevel RequiredBy { get; init; }

	public required string Message { get; init; }
}

/// <summary>
/// The single source of truth for which manifest fields are required at which
/// <see cref="PluginManifestValidationLevel"/>, read from the <c>x-macrodeck-requirement</c> annotations on
/// the published <c>plugin-manifest-v1.schema.json</c> rather than duplicated in code. Deliberately does
/// not re-check anything <see cref="IPluginManifestReader"/> already enforces (the <see cref="PluginManifestFieldRequirement.Runtime"/>
/// category exists precisely so that duplicated definition does not creep back in).
/// </summary>
public static class PluginManifestRequirements
{
	private static readonly Lazy<IReadOnlyList<PluginManifestFieldRule>> _rules = new(LoadRules);

	/// <summary>Every field the schema annotates, one row per pointer pattern. Built once from the embedded
	/// schema and cached for the lifetime of the process.</summary>
	public static IReadOnlyList<PluginManifestFieldRule> All => _rules.Value;

	/// <summary>
	/// Resolves a concrete pointer (e.g. <c>/entrypoints/win-x64/executable</c>) against <see cref="All"/>'s
	/// patterns. Matching is exact per segment, never by prefix, so <c>/publisher/id</c> does not match the
	/// <c>/publisher</c> pattern just because it starts with it. An unknown, empty, or malformed pointer
	/// never throws: it returns <see langword="false"/> and sets <paramref name="requirement"/> to
	/// <see cref="PluginManifestFieldRequirement.Recommended"/>, so a manifest field this build of the
	/// table does not yet know about degrades to "nice to have" rather than crashing forward-compatible
	/// tooling.
	/// </summary>
	public static bool TryGetRequirement(string pointer, out PluginManifestFieldRequirement requirement)
	{
		if (TryMatch(pointer, out var matched))
		{
			requirement = matched;
			return true;
		}

		requirement = PluginManifestFieldRequirement.Recommended;
		return false;
	}

	/// <summary>Same lookup as <see cref="TryGetRequirement"/>, returning <see cref="PluginManifestFieldRequirement.Recommended"/>
	/// for anything unmatched instead of a success flag.</summary>
	public static PluginManifestFieldRequirement For(string pointer)
	{
		TryGetRequirement(pointer, out var requirement);
		return requirement;
	}

	/// <summary>
	/// Evaluates one manifest at one level. A pure function of its two arguments: it never touches the
	/// filesystem, the current directory, or any other ambient state, and it cannot tell a source manifest
	/// from an already-extracted install directory - it only ever sees the manifest.
	/// <para>
	/// Emits only two kinds of finding: an unsatisfied <see cref="PluginManifestFieldRequirement.Publication"/>
	/// field, and only when <paramref name="level"/> is <see cref="PluginManifestValidationLevel.Package"/>
	/// or higher (so <see cref="PluginManifestValidationLevel.Development"/> always returns an empty list);
	/// and a present <see cref="PluginManifestFieldRequirement.Generated"/> field, reported at every level
	/// as a fact, never as an error - it is the caller's job to decide whether a hand-authored value there
	/// is expected.
	/// </para>
	/// </summary>
	public static IReadOnlyList<PluginManifestRequirementViolation> Evaluate(PluginManifest manifest,
		PluginManifestValidationLevel level)
	{
		var violations = new List<PluginManifestRequirementViolation>();

		if (manifest.Files is not null)
		{
			violations.Add(GeneratedViolation("/files",
				"'files' is populated by the packaging pipeline from the artifact payload. A hand-authored value is never used."));
		}

		if (manifest.Signature is not null)
		{
			violations.Add(GeneratedViolation("/signature",
				"'signature' is populated by the packaging pipeline when the artifact is signed. A hand-authored value is never used."));
		}

		if (level >= PluginManifestValidationLevel.Package)
		{
			CheckPublicationString(manifest.Description, "/description", "description", violations);
			CheckPublicationString(manifest.Icon, "/icon", "icon", violations);
			CheckPublicationString(manifest.License, "/license", "license", violations);
			CheckPublicationString(manifest.Repository, "/repository", "repository", violations);
			CheckCompatibility(manifest.Compatibility, violations);
			CheckPublisher(manifest.Publisher, violations);
		}

		return violations;
	}

	private static void CheckPublicationString(string? value,
		string pointer,
		string fieldName,
		List<PluginManifestRequirementViolation> violations)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			violations.Add(PublicationViolation(pointer,
				$"'{fieldName}' is required to publish to the Macro Deck plugin ecosystem. It is not required to develop or run this plugin locally."));
		}
	}

	private static void CheckCompatibility(PluginCompatibility? compatibility,
		List<PluginManifestRequirementViolation> violations)
	{
		var satisfied = compatibility is not null &&
			(!string.IsNullOrWhiteSpace(compatibility.Sdk) ||
				compatibility.Protocol is not null ||
				!string.IsNullOrWhiteSpace(compatibility.MacroDeck));

		if (!satisfied)
		{
			violations.Add(PublicationViolation("/compatibility",
				"'compatibility' is required to publish to the Macro Deck plugin ecosystem: declare at least one of 'sdk', 'protocol' or 'macroDeck'. It is not required to develop or run this plugin locally."));
		}
	}

	private static void CheckPublisher(PluginPublisher? publisher, List<PluginManifestRequirementViolation> violations)
	{
		if (publisher is null)
		{
			violations.Add(PublicationViolation("/publisher",
				"'publisher' is required to publish to the Macro Deck plugin ecosystem. It is not required to develop or run this plugin locally."));

			return;
		}

		if (string.IsNullOrWhiteSpace(publisher.Name))
		{
			violations.Add(PublicationViolation("/publisher/name",
				"'publisher.name' is required to publish to the Macro Deck plugin ecosystem. It is not required to develop or run this plugin locally."));
		}
	}

	private static PluginManifestRequirementViolation PublicationViolation(string pointer, string message) =>
		new()
		{
			Pointer = pointer,
			Requirement = For(pointer),
			RequiredBy = PluginManifestValidationLevel.Publication,
			Message = message
		};

	private static PluginManifestRequirementViolation GeneratedViolation(string pointer, string message) =>
		new()
		{
			Pointer = pointer,
			Requirement = For(pointer),
			RequiredBy = PluginManifestValidationLevel.Development,
			Message = message
		};

	private static bool TryMatch(string pointer, out PluginManifestFieldRequirement requirement)
	{
		requirement = default;

		if (string.IsNullOrEmpty(pointer) || pointer[0] != '/')
		{
			return false;
		}

		var segments = pointer.Split('/');

		foreach (var rule in All)
		{
			var patternSegments = rule.PointerPattern.Split('/');
			if (patternSegments.Length != segments.Length)
			{
				continue;
			}

			var isMatch = true;
			for (var i = 1; i < segments.Length; i++)
			{
				if (patternSegments[i] == "*")
				{
					continue;
				}

				if (!string.Equals(patternSegments[i], segments[i], StringComparison.Ordinal))
				{
					isMatch = false;
					break;
				}
			}

			if (isMatch)
			{
				requirement = rule.Requirement;
				return true;
			}
		}

		return false;
	}

	private static List<PluginManifestFieldRule> LoadRules()
	{
		using var stream = typeof(PluginManifestRequirements).Assembly
				.GetManifestResourceStream("plugin-manifest-v1.schema.json") ??
			throw new InvalidOperationException(
				$"'{typeof(PluginManifestRequirements).Assembly.FullName}' does not embed 'plugin-manifest-v1.schema.json'.");

		using var document = JsonDocument.Parse(stream);
		var root = document.RootElement;

		var defs = root.TryGetProperty("$defs", out var defsElement)
			? defsElement
			: default;

		var rules = new List<PluginManifestFieldRule>();
		WalkObjectSchema(root, string.Empty, defs, rules);

		return rules;
	}

	/// <summary>
	/// Descends the schema exactly the way the published contract needs to be read: a property's own
	/// <c>x-macrodeck-requirement</c> is recorded first, then its substructure - resolved through a local
	/// <c>$ref</c>, an array's <c>items</c>, or a map's <c>additionalProperties</c> - is walked with a
	/// concrete pointer segment or a <c>*</c> wildcard appended, as appropriate. Only local <c>#/$defs/...</c>
	/// references exist in this schema, so that is the only <c>$ref</c> form resolved.
	/// </summary>
	private static void WalkObjectSchema(JsonElement objectSchema,
		string basePointer,
		JsonElement defs,
		List<PluginManifestFieldRule> rules)
	{
		if (!objectSchema.TryGetProperty("properties", out var properties))
		{
			return;
		}

		foreach (var property in properties.EnumerateObject())
		{
			var pointer = basePointer + "/" + EscapePointerSegment(property.Name);
			var propertySchema = property.Value;

			if (propertySchema.TryGetProperty("x-macrodeck-requirement", out var requirementElement) &&
				requirementElement.ValueKind == JsonValueKind.String)
			{
				var summary = propertySchema.TryGetProperty("description", out var descriptionElement)
					? descriptionElement.GetString() ?? string.Empty
					: string.Empty;

				rules.Add(new PluginManifestFieldRule
				{
					PointerPattern = pointer,
					Requirement = ParseRequirement(requirementElement.GetString()),
					Summary = summary
				});
			}

			if (TryResolveSubSchema(propertySchema, defs, out var referenced))
			{
				WalkObjectSchema(referenced, pointer, defs, rules);
			}

			if (propertySchema.TryGetProperty("items", out var itemsSchema) &&
				TryResolveSubSchema(itemsSchema, defs, out var itemTarget))
			{
				WalkObjectSchema(itemTarget, pointer + "/*", defs, rules);
			}

			if (propertySchema.TryGetProperty("additionalProperties", out var additionalPropertiesSchema) &&
				additionalPropertiesSchema.ValueKind == JsonValueKind.Object &&
				TryResolveSubSchema(additionalPropertiesSchema, defs, out var valueTarget))
			{
				WalkObjectSchema(valueTarget, pointer + "/*", defs, rules);
			}
		}
	}

	private static bool TryResolveSubSchema(JsonElement schema, JsonElement defs, out JsonElement resolved)
	{
		if (schema.TryGetProperty("$ref", out var refElement) &&
			refElement.ValueKind == JsonValueKind.String &&
			TryResolveDefRef(refElement.GetString(), defs, out resolved))
		{
			return true;
		}

		if (schema.TryGetProperty("properties", out _))
		{
			resolved = schema;
			return true;
		}

		resolved = default;
		return false;
	}

	private static bool TryResolveDefRef(string? reference, JsonElement defs, out JsonElement resolved)
	{
		resolved = default;

		const string prefix = "#/$defs/";
		if (reference is null ||
			!reference.StartsWith(prefix, StringComparison.Ordinal) ||
			defs.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		var name = reference[prefix.Length..];
		return defs.TryGetProperty(name, out resolved);
	}

	private static string EscapePointerSegment(string segment) =>
		segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

	private static PluginManifestFieldRequirement ParseRequirement(string? value) => value switch
	{
		"runtime" => PluginManifestFieldRequirement.Runtime,
		"publication" => PluginManifestFieldRequirement.Publication,
		"generated" => PluginManifestFieldRequirement.Generated,
		_ => PluginManifestFieldRequirement.Recommended
	};
}
