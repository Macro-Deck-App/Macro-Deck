using System.Text.Json;
using Json.Schema;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.Manifests;

/// <summary>
/// Evaluates a manifest document against the embedded <c>plugin-manifest-v1.schema.json</c> - the same
/// file <c>MacroDeck.Plugin.Packaging</c> embeds for exactly this purpose, read from its assembly rather
/// than copied into this project so there is only ever one copy of the published schema.
/// <para>
/// This is a second, independent check from <see cref="IPluginManifestReader" />: the reader enforces
/// this format's semantic rules (an id matches its directory, an entrypoint resolves safely, and so on),
/// while the schema enforces the document's own published shape - the wrong JSON type for a field, an
/// unexpected structure - and, unlike the reader, can point at exactly where in the document a problem
/// lives.
/// </para>
/// </summary>
internal static class PluginManifestSchema
{
	private static readonly Lazy<JsonSchema> _schema = new(LoadEmbedded);

	/// <summary>Every schema violation found, each carrying the JSON pointer
	/// <see cref="JsonSchema.Evaluate(JsonElement, EvaluationOptions)" /> located it at.</summary>
	public static IReadOnlyList<ManifestProblem> Validate(JsonElement manifestInstance)
	{
		// Hierarchical, not List: the Collect guard below skips a valid node *and everything under it*, which
		// only means anything while the results are still a tree. Flattened, an inner subschema whose local
		// "failure" is exactly how its parent "not" succeeds sits at the top level with nothing to suppress
		// it, and every legal entrypoint executable is reported as a pattern violation.
		var results = _schema.Value.Evaluate(manifestInstance,
			new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical });

		var problems = new List<ManifestProblem>();
		var seen = new HashSet<(string Pointer, string Keyword)>();

		Collect(results, problems, seen);

		return DropApplicatorFollowOns(problems);
	}

	/// <summary>Keywords that only ever say "something below me failed". None of them names the offending
	/// field, so each is a follow-on whenever the real problem is also reported underneath it.</summary>
	private static readonly HashSet<string> _applicatorCodes =
	[
		"schema:properties",
		"schema:additionalProperties",
		"schema:patternProperties",
		"schema:propertyNames",
		"schema:items",
		"schema:prefixItems",
		"schema:contains",
		"schema:allOf",
		"schema:anyOf",
		"schema:oneOf",
		"schema:$ref"
	];

	/// <summary>
	/// Drops a pure applicator's problem when the document also carries a problem at a location beneath it.
	/// Reporting both makes one defect look like two, and the child problem is the one that names the
	/// offending field.
	/// </summary>
	private static List<ManifestProblem> DropApplicatorFollowOns(List<ManifestProblem> problems)
	{
		var locations = problems.Select(problem => problem.Pointer ?? string.Empty).ToList();

		return problems
			.Where((problem, index) => !_applicatorCodes.Contains(problem.Code) ||
				!locations.Where((_, other) => other != index)
					.Any(location => IsBeneath(location, locations[index])))
			.ToList();
	}

	/// <summary>Whether one RFC 6901 pointer names a location strictly inside another. The root pointer is
	/// the empty string, so every non-empty pointer is beneath it.</summary>
	private static bool IsBeneath(string candidate, string ancestor) =>
		ancestor.Length == 0
			? candidate.Length > 0
			: candidate.Length > ancestor.Length &&
			candidate.StartsWith(ancestor, StringComparison.Ordinal) &&
			candidate[ancestor.Length] == '/';

	private static void Collect(EvaluationResults node,
		List<ManifestProblem> problems,
		HashSet<(string Pointer, string Keyword)> seen)
	{
		// A node that is itself valid never contributes a problem, and neither does anything under it -
		// even when a child's own local match looks "invalid" by design, which is exactly what a "not"
		// keyword's inner subschema reports when the document correctly avoids matching it (e.g. an
		// entrypoint executable that does *not* end in .sh/.bat/...). Without this guard, that expected
		// internal non-match would surface as a false-positive schema error on an otherwise valid document.
		if (node.IsValid)
		{
			return;
		}

		if (node.Errors is { Count: > 0 } errors)
		{
			var pointer = node.InstanceLocation.ToString();

			foreach (var (keyword, message) in errors)
			{
				if (seen.Add((pointer, keyword)))
				{
					problems.Add(new ManifestProblem
					{
						Severity = ManifestProblemSeverity.Error,
						Code = "schema:" + keyword,
						Message = message,
						Pointer = pointer
					});
				}
			}
		}

		foreach (var child in node.Details ?? [])
		{
			Collect(child, problems, seen);
		}
	}

	private static JsonSchema LoadEmbedded()
	{
		var assembly = typeof(PluginManifest).Assembly;

		using var stream = assembly.GetManifestResourceStream("plugin-manifest-v1.schema.json") ??
			throw new InvalidOperationException(
				$"'{assembly.FullName}' does not embed 'plugin-manifest-v1.schema.json'.");

		using var reader = new StreamReader(stream);
		return JsonSchema.FromText(reader.ReadToEnd());
	}
}
