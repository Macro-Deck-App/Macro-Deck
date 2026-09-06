using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.Manifests;

/// <summary>
/// Maps <see cref="PluginManifestRequirementViolation" /> onto the two console-facing shapes
/// <c>validate</c>, <c>pack</c> and <c>build</c> each need: a full <see cref="ManifestProblem" /> for
/// <c>validate</c>'s own report, or a plain <see cref="CliDiagnostic" /> warning for <c>pack</c>'s and
/// <c>build</c>'s warnings collections, which never carry a pointer or a level, only a code and a message.
/// </summary>
internal static class RequirementProblems
{
	/// <param name="looksLikeSourceTree">Whether the manifest being evaluated sits beside an unbuilt
	/// project - see <see cref="ManifestValidator.HasProjectFile" />. Gates <c>generated-field-authored</c>
	/// only; an unsatisfied <see cref="PluginManifestFieldRequirement.Publication" /> field is reported
	/// regardless.</param>
	public static IReadOnlyList<ManifestProblem> From(IReadOnlyList<PluginManifestRequirementViolation> violations,
		PluginManifestValidationLevel level,
		bool looksLikeSourceTree)
	{
		var problems = new List<ManifestProblem>();

		foreach (var violation in violations)
		{
			switch (violation.Requirement)
			{
				case PluginManifestFieldRequirement.Publication:
					problems.Add(new ManifestProblem
					{
						Severity = level == PluginManifestValidationLevel.Publication
							? ManifestProblemSeverity.Error
							: ManifestProblemSeverity.Warning,
						Code = "publication-metadata-missing",
						Message = violation.Message,
						Pointer = violation.Pointer,
						Level = PluginManifestValidationLevel.Publication
					});
					break;

				case PluginManifestFieldRequirement.Generated when looksLikeSourceTree:
					problems.Add(new ManifestProblem
					{
						Severity = ManifestProblemSeverity.Warning,
						Code = "generated-field-authored",
						Message = violation.Message,
						Pointer = violation.Pointer,

						// Deliberately not violation.RequiredBy (always Development): no validation level
						// actually requires a generated field to be absent, so rendering "(development)" as
						// its requiring context would misleadingly suggest one does.
						Level = null
					});
					break;
			}
		}

		return problems;
	}

	/// <summary>The publication-readiness subset of <see cref="From" />, as plain warnings - what
	/// <c>pack</c> and <c>build</c> append to their own warnings collections. Both commands always evaluate
	/// at publication readiness; neither exposes a <c>--level</c> flag.</summary>
	public static IReadOnlyList<CliDiagnostic> PublicationWarnings(PluginManifest manifest) =>
		PluginManifestRequirements.Evaluate(manifest, PluginManifestValidationLevel.Publication)
			.Where(violation => violation.Requirement == PluginManifestFieldRequirement.Publication)
			.Select(violation => new CliDiagnostic("publication-metadata-missing", violation.Message))
			.ToList();

	/// <summary>The generated-field subset, silent unless <paramref name="looksLikeSourceTree" /> - see
	/// <see cref="ManifestValidator.HasProjectFile" />.</summary>
	public static IReadOnlyList<CliDiagnostic> GeneratedFieldWarnings(PluginManifest manifest, bool looksLikeSourceTree)
	{
		if (!looksLikeSourceTree)
		{
			return [];
		}

		return PluginManifestRequirements.Evaluate(manifest, PluginManifestValidationLevel.Development)
			.Where(violation => violation.Requirement == PluginManifestFieldRequirement.Generated)
			.Select(violation => new CliDiagnostic("generated-field-authored", violation.Message))
			.ToList();
	}
}
