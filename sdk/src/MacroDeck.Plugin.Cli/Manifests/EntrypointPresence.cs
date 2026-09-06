using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.Manifests;

/// <summary>
/// Checks a manifest's declared entrypoints against the set of entries a payload actually carries.
/// Shared by <c>pack</c> and <c>inspect</c> so the two commands can never disagree about what "missing"
/// means - see issue #556: <c>PluginManifestReader</c> only ever existence-checks the entrypoint for the
/// <em>current</em> runtime identifier, so a foreign RID's binary is never verified anywhere else.
/// </summary>
internal static class EntrypointPresence
{
	/// <summary>Normalizes a manifest-declared executable path for comparison against archive/directory
	/// entry names: splits on both <c>/</c> and <c>\</c>, drops empty and <c>.</c> segments, and re-joins
	/// with <c>/</c>. <see cref="PluginEntrypoint.Executable" /> is carried verbatim from the manifest JSON
	/// - the manifest reader validates it (no <c>..</c> segments, stays inside the version directory) but
	/// never rewrites it - so a value like <c>.\bin\app.exe</c> really can arrive here and must still match
	/// an archive entry recorded as <c>bin/app.exe</c>.</summary>
	public static string Normalize(string executablePath)
	{
		var segments = executablePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
			.Where(segment => segment != ".");

		return string.Join('/', segments);
	}

	/// <summary>Yields one diagnostic per declared entrypoint whose normalized executable is not present in
	/// <paramref name="presentEntryNames" />, ordered by runtime identifier (ordinal) for stable output.
	/// The diagnostic message keeps the original, un-normalized executable text so it matches what the
	/// manifest author actually wrote.</summary>
	/// <param name="presentEntryNames">The archive/directory's own entry names, already normalized and
	/// compared <see cref="StringComparer.OrdinalIgnoreCase" /> - callers build this set themselves so the
	/// comparer choice lives in one place.</param>
	/// <param name="subjectNoun">What <paramref name="presentEntryNames" /> represents in the message -
	/// <c>"artifact"</c>, <c>"source tree"</c> or <c>"directory"</c>.</param>
	public static IReadOnlyList<CliDiagnostic> Missing(PluginManifest manifest,
		IReadOnlySet<string> presentEntryNames,
		string subjectNoun)
	{
		return manifest.Entrypoints
			.Where(pair => !presentEntryNames.Contains(Normalize(pair.Value.Executable)))
			.OrderBy(pair => pair.Key, StringComparer.Ordinal)
			.Select(pair => new CliDiagnostic("entrypoint-not-packed",
				$"Entrypoint '{pair.Key}' declares '{pair.Value.Executable}', which is not in the {subjectNoun}."))
			.ToList();
	}

	/// <summary>The pointer-carrying counterpart to <see cref="Missing" />, for <c>validate</c>'s own
	/// package-level payload check: every declared entrypoint checked against <paramref name="presentEntryNames" />,
	/// not only the current host's - the whole point being that <c>PluginManifestReader</c> deliberately
	/// only ever existence-checks the current runtime identifier's own entrypoint, leaving a foreign RID's
	/// binary unverified everywhere else.</summary>
	public static IReadOnlyList<ManifestProblem> MissingProblems(PluginManifest manifest,
		IReadOnlySet<string> presentEntryNames)
	{
		return manifest.Entrypoints
			.Where(pair => !presentEntryNames.Contains(Normalize(pair.Value.Executable)))
			.OrderBy(pair => pair.Key, StringComparer.Ordinal)
			.Select(pair => new ManifestProblem
			{
				Severity = ManifestProblemSeverity.Error,
				Code = "entrypoint-not-packed",
				Message = $"Entrypoint '{pair.Key}' declares '{pair.Value.Executable}', which is not in the artifact.",
				Pointer = $"/entrypoints/{EscapePointerSegment(pair.Key)}/executable",
				Level = PluginManifestValidationLevel.Package
			})
			.ToList();
	}

	private static string EscapePointerSegment(string segment) =>
		segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
