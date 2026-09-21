using System.Text.Json;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.Manifests;

internal static class ManifestDocumentProblems
{
	private const string AdditionalLinksPointer = "/additionalLinks";

	// Filtered after the schema drops its applicator follow-ons, or a bare root problem survives.
	public static List<ManifestProblem> Evaluate(JsonElement manifest)
	{
		var problems = PluginManifestSchema.Validate(manifest)
			.Where(problem => problem.Pointer is not { } pointer ||
				(pointer != AdditionalLinksPointer &&
					!pointer.StartsWith(AdditionalLinksPointer + "/", StringComparison.Ordinal)))
			.ToList();

		if (manifest.ValueKind == JsonValueKind.Object &&
			TryGetAdditionalLinks(manifest, out var name, out var additionalLinks))
		{
			problems.AddRange(PluginManifestLinks.Validate(additionalLinks)
				.Select(problem => ToProblem(problem, "/" + name)));
		}

		return problems;
	}

	private static bool TryGetAdditionalLinks(JsonElement manifest, out string name, out JsonElement additionalLinks)
	{
		foreach (var property in manifest.EnumerateObject())
		{
			if (string.Equals(property.Name, "additionalLinks", StringComparison.OrdinalIgnoreCase))
			{
				name = property.Name;
				additionalLinks = property.Value;
				return true;
			}
		}

		name = string.Empty;
		additionalLinks = default;
		return false;
	}

	private static ManifestProblem ToProblem(PluginManifestLinkProblem problem, string pointer)
	{
		if (problem.Index is { } index)
		{
			pointer += "/" + index;
			if (problem.Property is { } property)
			{
				pointer += "/" + property;
			}
		}

		return problem.Severity == PluginManifestLinkProblemSeverity.Warning
			? new ManifestProblem
			{
				Severity = ManifestProblemSeverity.Warning,
				Code = "unknown-link-type",
				Message = problem.Message,
				Pointer = pointer
			}
			: new ManifestProblem
			{
				Severity = ManifestProblemSeverity.Error,
				Code = "invalid-additional-link",
				Message = problem.Message,
				Pointer = pointer
			};
	}
}
