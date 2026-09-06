using System.Security.Cryptography;
using System.Text.Json;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Plugin.Packaging.Versioning;

namespace MacroDeck.Plugin.Cli.Manifests;

/// <summary>
/// Validates a manifest through the same four layers <c>macrodeck-plugin validate</c> and
/// <c>macrodeck-plugin pack</c> both need - and the only place either of them runs those layers, so the
/// two commands can never quietly drift apart on what "valid" means:
/// <list type="number">
/// <item>The real <see cref="IPluginManifestReader" /> (or <see cref="IPluginArtifactReader" /> for an
/// artifact) - manifest version, plugin id, entrypoint path safety, compatibility shape, dependencies,
/// icon packs, permission shape and signature shape all come from here, unmodified.</item>
/// <item>The embedded <c>plugin-manifest-v1.schema.json</c>, evaluated independently - see
/// <see cref="PluginManifestSchema" /> for why this is not redundant with the reader.</item>
/// <item>The permission vocabulary (<see cref="PluginPermissions.IsKnown" />) - an unknown permission is
/// a warning, never an error, matching the host's own installer.</item>
/// <item>Declared <c>files[]</c> digests checked against real bytes on disk - the one thing neither the
/// reader nor the schema can do, since both work from the manifest document alone.</item>
/// </list>
/// </summary>
internal static class ManifestValidator
{
	private static readonly IPluginManifestReader _manifestReader = ArtifactReaders.ManifestReader;

	private static readonly IPluginArtifactReader _artifactReader = ArtifactReaders.ArtifactReader;

	/// <summary>Validates a standalone <c>manifest.json</c>. <paramref name="manifestPath" />'s own
	/// directory is used as the version directory, so a declared entrypoint's existence is checked against
	/// whatever binaries already sit next to the manifest - useful before packaging, when there is no
	/// artifact yet to inspect.</summary>
	public static async Task<ManifestValidationResult> ValidateManifestFileAsync(string manifestPath,
		PluginManifestValidationLevel level = PluginManifestValidationLevel.Development,
		CancellationToken cancellationToken = default)
	{
		if (!File.Exists(manifestPath))
		{
			return Failure(ExitCode.InputUnreadable,
				"manifest-not-found",
				$"No manifest at '{CliText.DisplayPath(manifestPath)}'.",
				CliText.DisplayPath(manifestPath),
				level);
		}

		string json;
		try
		{
			json = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return Failure(ExitCode.InputUnreadable,
				"manifest-not-found",
				ex.Message,
				CliText.DisplayPath(manifestPath),
				level);
		}

		// Probed rather than supplied by a caller: a bare manifest.json has no enclosing <id>/versions/
		// <version> directory structure to check against - the same situation IPluginArtifactReader.Inspect
		// is in, and it makes the same choice (see its own remarks on why it probes id/version out of the
		// raw JSON). The manifest is therefore checked against itself: does it internally make sense, not
		// "does it sit in the directory a real install would expect".
		var (probedId, probedVersion) = ProbeIdAndVersion(json);
		var readResult = _manifestReader.Read(manifestPath, probedId, probedVersion);

		// Computed here rather than only once the reader succeeds: Task D's source-directory diagnosis and
		// the malformed-JSON message below both need it while the reader has still failed.
		var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? Directory.GetCurrentDirectory();

		if (!readResult.Success)
		{
			var error = readResult.Error!.Value;
			var problems = new List<ManifestProblem>
			{
				DescribeReaderFailure(error,
					readResult.ErrorMessage,
					manifestPath,
					baseDirectory,
					json)
			};

			// A document that parses as JSON but does not bind to the manifest shape also comes back as
			// Malformed, carrying System.Text.Json's own deserialization text - a CLR generic type name and a
			// 0-based Path/LineNumber suffix. The schema layer describes that same defect properly, naming the
			// field and the expected type, so the reader's version is dropped whenever the schema has anything
			// to say. Only when it has nothing does that message stay, as the sole account of the failure.
			var jsonParses = TryParseJson(json, out var parsed, out _);
			parsed?.Dispose();
			var schemaSupersedesReader = error == PluginManifestError.Malformed && jsonParses;

			// A schema-shaped, merely semantically-wrong document (e.g. a bad id or a version that does not
			// match its directory) still parses as JSON, so the schema layer can run over it too and surface
			// every other independent defect in the same pass. Genuinely unparseable JSON cannot be handed
			// to the schema evaluator at all, so that case keeps reporting the single reader problem, as
			// before.
			if (TryEvaluateSchema(json, out var schemaProblems))
			{
				var readerPointer = ReaderErrorPointer(error);
				foreach (var schemaProblem in schemaProblems)
				{
					// The reader's own message is the specific, actionable one; the schema's is Json.Schema's
					// generic keyword text about the same defect. Suppress the follow-on schema problem, never
					// the reader's - and only at exactly the location the reader spoke about, since a schema
					// defect deeper inside the same object (another entrypoint, another permission) is a
					// separate finding the reader never made.
					if (readerPointer is not null && schemaProblem.Pointer == readerPointer)
					{
						continue;
					}

					problems.Add(schemaProblem);
				}

				if (schemaSupersedesReader && schemaProblems.Count > 0)
				{
					problems.RemoveAt(0);
				}
			}

			problems.AddRange(EvaluateVersion(probedVersion));

			return new ManifestValidationResult
			{
				Valid = false,
				ExitCode = PluginManifestErrorExitCode.For(error),
				PluginId = probedId.Length > 0 ? probedId : null,
				Version = probedVersion.Length > 0 ? probedVersion : null,
				Problems = problems,
				Subject = CliText.DisplayPath(manifestPath),
				Level = level
			};
		}

		var manifest = readResult.Manifest!;

		var successProblems = new List<ManifestProblem>();
		successProblems.AddRange(EvaluateSchema(json));
		successProblems.AddRange(EvaluateVersion(manifest.Version));
		successProblems.AddRange(EvaluatePermissionVocabulary(manifest));

		// Extra files alongside a loose manifest.json are ordinary build output (obj/, bin/ siblings, a
		// README) rather than something the manifest claims to be a complete inventory of - unlike a packed
		// artifact, whose files[] the installer treats as exhaustive. Only "declared but wrong" is checked
		// here; ValidateArtifactAsync below additionally checks "present but undeclared".
		successProblems.AddRange(
			EvaluateDeclaredFilesAgainstDisk(manifest, baseDirectory, checkForUndeclaredFiles: false));

		var looksLikeSourceTree = HasProjectFile(baseDirectory);
		successProblems.AddRange(RequirementProblems.From(PluginManifestRequirements.Evaluate(manifest, level),
			level,
			looksLikeSourceTree));
		successProblems.AddRange(EvaluateEntrypointLayout(manifest, level));

		if (level >= PluginManifestValidationLevel.Package && !looksLikeSourceTree)
		{
			successProblems.AddRange(EvaluatePayloadConsistency(manifest, EnumeratePresentEntryNames(baseDirectory)));
		}

		return Build(manifest, successProblems, CliText.DisplayPath(manifestPath), level);
	}

	/// <summary>Validates <c>manifest.json</c> inside <paramref name="versionDirectory" />.</summary>
	public static Task<ManifestValidationResult> ValidateDirectoryAsync(string versionDirectory,
		PluginManifestValidationLevel level = PluginManifestValidationLevel.Development,
		CancellationToken cancellationToken = default)
		=> ValidateManifestFileAsync(Path.Combine(versionDirectory, PluginArtifactFiles.ManifestFileName),
			level,
			cancellationToken);

	/// <summary>Validates a packed <c>.macroDeckPlugin</c> artifact through <see cref="IPluginArtifactReader" />.
	/// Extracts to a temporary directory it always deletes, purely to check schema and declared file
	/// digests against real bytes - never to install or keep anything.</summary>
	public static async Task<ManifestValidationResult> ValidateArtifactAsync(string artifactPath,
		PluginManifestValidationLevel level = PluginManifestValidationLevel.Package,
		CancellationToken cancellationToken = default)
	{
		if (!File.Exists(artifactPath))
		{
			return Failure(ExitCode.InputUnreadable,
				"artifact-not-found",
				$"No artifact at '{CliText.DisplayPath(artifactPath)}'.",
				CliText.DisplayPath(artifactPath),
				level);
		}

		var inspection = await _artifactReader.Inspect(artifactPath, cancellationToken).ConfigureAwait(false);
		if (!inspection.Success || inspection.Manifest is not { } manifest)
		{
			var error = inspection.Error ?? PluginInstallError.Failed;
			var diagnostic = ArtifactErrorText.Describe(artifactPath, error, inspection.ErrorMessage);
			return Failure(PluginInstallErrorExitCode.For(error),
				diagnostic.Code,
				diagnostic.Message,
				CliText.DisplayPath(artifactPath),
				level);
		}

		var problems = new List<ManifestProblem>();
		var extractDirectory = Directory.CreateTempSubdirectory("macrodeck-plugin-validate-").FullName;

		try
		{
			var extraction = await _artifactReader.ExtractTo(artifactPath, extractDirectory, cancellationToken)
				.ConfigureAwait(false);

			if (!extraction.Success)
			{
				problems.Add(Problem(ManifestProblemSeverity.Error,
					"artifact-unreadable-after-inspection",
					extraction.ErrorMessage ?? "The artifact could not be re-read to verify its contents."));
				return Build(manifest, problems, CliText.DisplayPath(artifactPath), level);
			}

			var manifestPath = Path.Combine(extractDirectory, PluginArtifactFiles.ManifestFileName);
			var json = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
			problems.AddRange(EvaluateSchema(json));
			problems.AddRange(EvaluateVersion(manifest.Version));
			problems.AddRange(EvaluatePermissionVocabulary(manifest));

			// An artifact's files[] is a complete inventory once it is declared at all (see
			// plugin-hosting.md's install rejection table), so an undeclared extra file is checked here,
			// unlike the loose-directory path above. The same walk this needs to find undeclared entries
			// also produces the present-entry-name set the payload-consistency check below reuses, rather
			// than re-walking the extracted directory a second time.
			var declaredFileProblems = EvaluateDeclaredFilesAgainstDisk(manifest,
				extractDirectory,
				checkForUndeclaredFiles: true,
				out var presentEntryNames);
			problems.AddRange(declaredFileProblems);

			// A packed artifact is never an unbuilt source tree - HasProjectFile only matters for the loose
			// manifest.json / version-directory path above, where a project file next to the manifest means
			// "not built yet". Evaluated anyway (rather than hardcoding false) so a corrupt extraction that
			// somehow carries a project file is not silently exempted.
			var looksLikeSourceTree = HasProjectFile(extractDirectory);
			problems.AddRange(RequirementProblems.From(PluginManifestRequirements.Evaluate(manifest, level),
				level,
				looksLikeSourceTree));
			problems.AddRange(EvaluateEntrypointLayout(manifest, level));

			if (level >= PluginManifestValidationLevel.Package && !looksLikeSourceTree)
			{
				problems.AddRange(EvaluatePayloadConsistency(manifest, presentEntryNames));
			}
		}
		finally
		{
			TryDeleteDirectory(extractDirectory);
		}

		return Build(manifest, problems, CliText.DisplayPath(artifactPath), level);
	}

	/// <summary>Builds the reader-failure problem, special-casing the two situations Task C/D of issue #556
	/// call out: JSON that fails to even parse (named file, a real line/position) and an entrypoint that is
	/// missing only because the directory being validated is a source tree rather than build output.</summary>
	private static ManifestProblem DescribeReaderFailure(PluginManifestError error,
		string? readerMessage,
		string manifestPath,
		string baseDirectory,
		string json)
	{
		if (error == PluginManifestError.Malformed &&
			!TryParseJson(json, out _, out var jsonException) &&
			jsonException is not null)
		{
			return Problem(ManifestProblemSeverity.Error,
				"malformed",
				DescribeMalformedJson(manifestPath, jsonException));
		}

		if (error == PluginManifestError.EntrypointMissing && HasProjectFile(baseDirectory))
		{
			return Problem(ManifestProblemSeverity.Error,
				"source-directory",
				$"{readerMessage ?? "The manifest is invalid."} This looks like a source directory - " +
				"validate the build output instead, e.g. bin/Release/net10.0.");
		}

		return Problem(ManifestProblemSeverity.Error,
			CliText.KebabCase(error.ToString()),
			readerMessage ?? "The manifest is invalid.");
	}

	/// <summary>The pointer a reader error concerns, when it concerns one specific part of the document at
	/// all - used only to decide which follow-on schema problems are genuine duplicates of the reader's own
	/// finding. Only <c>Malformed</c> and <c>NotFound</c> are about the document as a whole, so only they map
	/// to nothing.</summary>
	private static string? ReaderErrorPointer(PluginManifestError error) => error switch
	{
		PluginManifestError.UnsupportedManifestVersion => "/manifestVersion",
		PluginManifestError.InvalidSettings => "/settings",
		PluginManifestError.InvalidDependency => "/dependencies",
		PluginManifestError.InvalidIcon => "/icon",
		PluginManifestError.InvalidPluginId or PluginManifestError.IdMismatch => "/id",
		PluginManifestError.VersionMismatch => "/version",
		PluginManifestError.InvalidName => "/name",
		PluginManifestError.NoEntrypoints
			or PluginManifestError.EntrypointMissing
			or PluginManifestError.EntrypointOutsideVersionDirectory
			or PluginManifestError.InvalidEntrypointRuntime => "/entrypoints",
		PluginManifestError.InvalidSignature => "/signature",
		PluginManifestError.InvalidFileDigest => "/files",
		PluginManifestError.InvalidPermission => "/permissions",
		PluginManifestError.InvalidCompatibility => "/compatibility",
		PluginManifestError.InvalidPublisher => "/publisher",
		_ => null
	};

	/// <summary><c>true</c> when <paramref name="directory" /> holds a project file - the signal that a
	/// missing entrypoint means "this is the source tree, not the build output" rather than "the plugin is
	/// broken". Internal rather than private: <c>PluginBuilder</c> and <c>PluginPacker</c> both need the
	/// same "is this an unbuilt source tree" judgement for their own generated-field warnings.</summary>
	internal static bool HasProjectFile(string directory)
	{
		try
		{
			return Directory.EnumerateFiles(directory, "*.csproj").Any() ||
				Directory.EnumerateFiles(directory, "*.fsproj").Any() ||
				Directory.EnumerateFiles(directory, "*.vbproj").Any();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	/// <summary>A version string is only ever an error here for not being SemVer - anything else about it
	/// (a mismatch with its directory, an unsafe path segment) is <see cref="IPluginManifestReader" />'s
	/// own, separate concern. Silent when <paramref name="version" /> is null or empty: an absent version is
	/// somebody else's problem to report, not this check's.</summary>
	private static IEnumerable<ManifestProblem> EvaluateVersion(string? version)
	{
		if (string.IsNullOrEmpty(version))
		{
			yield break;
		}

		if (!SemanticVersion.TryParse(version, out _))
		{
			yield return Problem(ManifestProblemSeverity.Error,
				"invalid-version",
				$"'{version}' is not a valid SemVer version.",
				"/version");
		}
	}

	/// <summary>Guarded schema evaluation: <c>false</c> means <paramref name="json" /> does not even parse,
	/// so there is no document to run <see cref="PluginManifestSchema" /> over at all. Safe to call from the
	/// reader-failure branch, where - unlike every other caller - nothing has proven the JSON parses yet.</summary>
	private static bool TryEvaluateSchema(string json, out IReadOnlyList<ManifestProblem> problems)
	{
		if (!TryParseJson(json, out var document, out _))
		{
			problems = [];
			return false;
		}

		using (document)
		{
			problems = PluginManifestSchema.Validate(document!.RootElement);
		}

		return true;
	}

	private static IEnumerable<ManifestProblem> EvaluateSchema(string json)
	{
		// Unguarded call is safe here: every caller of this overload already has proof the document parses -
		// either the real reader just parsed it, or the artifact reader's own extraction succeeded.
		return TryEvaluateSchema(json, out var problems) ? problems : [];
	}

	private static bool TryParseJson(string? json, out JsonDocument? document, out JsonException? exception)
	{
		try
		{
			document = JsonDocument.Parse(json ?? string.Empty);
			exception = null;
			return true;
		}
		catch (JsonException ex)
		{
			document = null;
			exception = ex;
			return false;
		}
	}

	/// <summary>Rebuilds a malformed-JSON message from the exception itself rather than passing System.Text.Json's
	/// verbatim text through: it names neither the file nor renders <see cref="JsonException.LineNumber" />
	/// and <see cref="JsonException.BytePositionInLine" /> - both 0-based and nullable - as the 1-based
	/// coordinates a human expects.</summary>
	private static string DescribeMalformedJson(string manifestPath, JsonException exception)
	{
		var displayPath = CliText.DisplayPath(manifestPath);

		var rawMessage = exception.Message;
		var cutoff = rawMessage.IndexOf(" LineNumber:", StringComparison.Ordinal);
		if (cutoff < 0)
		{
			cutoff = rawMessage.IndexOf(" Path:", StringComparison.Ordinal);
		}

		var reason = cutoff >= 0 ? rawMessage[..cutoff] : rawMessage;

		var position = exception.LineNumber is { } line && exception.BytePositionInLine is { } bytePosition
			? $" (line {line + 1}, position {bytePosition + 1})"
			: string.Empty;

		return $"'{displayPath}' is not valid JSON: {reason}{position}.";
	}

	private static IEnumerable<ManifestProblem> EvaluatePermissionVocabulary(PluginManifest manifest)
	{
		if (manifest.Permissions is not { } permissions)
		{
			yield break;
		}

		for (var index = 0; index < permissions.Count; index++)
		{
			var permission = permissions[index];
			if (!PluginPermissions.IsKnown(permission))
			{
				yield return Problem(ManifestProblemSeverity.Warning,
					"unknown-permission",
					$"'{permission}' is not a known permission.",
					$"/permissions/{index}");
			}
		}
	}

	private static List<ManifestProblem> EvaluateDeclaredFilesAgainstDisk(PluginManifest manifest,
		string baseDirectory,
		bool checkForUndeclaredFiles)
		=> EvaluateDeclaredFilesAgainstDisk(manifest, baseDirectory, checkForUndeclaredFiles, out _);

	/// <param name="presentEntryNames">The directory's own entry names, normalized and built during the same
	/// walk the undeclared-file check below already does - empty unless <paramref name="checkForUndeclaredFiles" />
	/// is true, since only <see cref="ValidateArtifactAsync" /> needs it and only that caller already pays
	/// for the walk.</param>
	private static List<ManifestProblem> EvaluateDeclaredFilesAgainstDisk(PluginManifest manifest,
		string baseDirectory,
		bool checkForUndeclaredFiles,
		out IReadOnlySet<string> presentEntryNames)
	{
		var problems = new List<ManifestProblem>();
		presentEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		if (manifest.Files is not { Count: > 0 } declared)
		{
			declared = [];
		}

		var declaredPaths = new HashSet<string>(declared.Select(file => file.Path), StringComparer.OrdinalIgnoreCase);

		for (var index = 0; index < declared.Count; index++)
		{
			var file = declared[index];
			var pointer = $"/files/{index}";
			var absolutePath = Path.Combine(baseDirectory, file.Path.Replace('/', Path.DirectorySeparatorChar));

			if (!File.Exists(absolutePath))
			{
				problems.Add(Problem(ManifestProblemSeverity.Error,
					"file-missing",
					$"'{file.Path}' is declared in 'files' but is missing.",
					pointer));
				continue;
			}

			var info = new FileInfo(absolutePath);
			if (info.Length != file.Size)
			{
				problems.Add(Problem(ManifestProblemSeverity.Error,
					"file-size-mismatch",
					$"'{file.Path}' is {info.Length} bytes; the manifest declares {file.Size}.",
					pointer));
				continue;
			}

			var actualSha256 = ComputeSha256(absolutePath);
			if (!string.Equals(actualSha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				problems.Add(Problem(ManifestProblemSeverity.Error,
					"file-digest-mismatch",
					$"'{file.Path}' does not match its declared digest.",
					pointer));
			}
		}

		if (!checkForUndeclaredFiles)
		{
			return problems;
		}

		var signed = manifest.Signature is not null;
		var entryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var actualPath in Directory.EnumerateFiles(baseDirectory, "*", SearchOption.AllDirectories))
		{
			var relative = Path.GetRelativePath(baseDirectory, actualPath).Replace(Path.DirectorySeparatorChar, '/');
			entryNames.Add(EntrypointPresence.Normalize(relative));

			if (string.Equals(relative, PluginArtifactFiles.ManifestFileName, StringComparison.OrdinalIgnoreCase) ||
				(signed &&
					(string.Equals(relative,
							PluginArtifactFiles.CertificateFileName,
							StringComparison.OrdinalIgnoreCase) ||
						string.Equals(relative,
							PluginArtifactFiles.CertificateSignatureFileName,
							StringComparison.OrdinalIgnoreCase))) ||
				declaredPaths.Contains(relative))
			{
				continue;
			}

			problems.Add(Problem(ManifestProblemSeverity.Error,
				"undeclared-file",
				$"'{relative}' is present in the artifact but not declared in 'files'."));
		}

		presentEntryNames = entryNames;
		return problems;
	}

	/// <summary>Walks <paramref name="baseDirectory" /> for its own sake - unlike the artifact path, the
	/// loose manifest/version-directory path never already walks the directory for another reason, so this
	/// is a fresh, cheap directory listing rather than a second pass over content already read.</summary>
	private static HashSet<string> EnumeratePresentEntryNames(string baseDirectory)
	{
		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var actualPath in Directory.EnumerateFiles(baseDirectory, "*", SearchOption.AllDirectories))
		{
			var relative = Path.GetRelativePath(baseDirectory, actualPath).Replace(Path.DirectorySeparatorChar, '/');
			names.Add(EntrypointPresence.Normalize(relative));
		}

		return names;
	}

	/// <summary>Package-level-and-up payload checks that the manifest reader deliberately never runs: every
	/// declared entrypoint (every RID, not only the current host's - see <see cref="EntrypointPresence" />)
	/// and a declared icon must both actually be present in the packaged content.</summary>
	private static IEnumerable<ManifestProblem> EvaluatePayloadConsistency(PluginManifest manifest,
		IReadOnlySet<string> presentEntryNames)
	{
		foreach (var missing in EntrypointPresence.MissingProblems(manifest, presentEntryNames))
		{
			yield return missing;
		}

		if (!string.IsNullOrEmpty(manifest.Icon) &&
			!presentEntryNames.Contains(EntrypointPresence.Normalize(manifest.Icon)))
		{
			yield return new ManifestProblem
			{
				Severity = ManifestProblemSeverity.Error,
				Code = "icon-declared-not-present",
				Message = $"'{manifest.Icon}' is declared as 'icon' but is not present in the packaged content.",
				Pointer = "/icon",
				Level = PluginManifestValidationLevel.Package
			};
		}
	}

	/// <summary>Package-level-and-up: the same judgement <c>build</c> hard-fails on, reused here so
	/// <c>validate</c> can never disagree with it about the same manifest - see <see cref="EntrypointLayout" />.</summary>
	private static IEnumerable<ManifestProblem> EvaluateEntrypointLayout(PluginManifest manifest,
		PluginManifestValidationLevel level)
	{
		if (level < PluginManifestValidationLevel.Package || manifest.Entrypoints.Count == 0)
		{
			yield break;
		}

		var rids = manifest.Entrypoints.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList();
		var resolution = EntrypointLayout.Resolve(manifest, rids);

		if (resolution.Failure is not { } failure)
		{
			yield break;
		}

		foreach (var rid in resolution.OffendingRids)
		{
			yield return new ManifestProblem
			{
				Severity = ManifestProblemSeverity.Error,
				Code = "entrypoint-layout-invalid",
				Message = failure,
				Pointer = $"/entrypoints/{EscapePointerSegment(rid)}/executable",
				Level = PluginManifestValidationLevel.Package
			};
		}
	}

	private static string EscapePointerSegment(string segment) =>
		segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

	private static (string Id, string Version) ProbeIdAndVersion(string json)
	{
		try
		{
			using var document = JsonDocument.Parse(json);
			var id = GetStringPropertyCaseInsensitive(document.RootElement, "id") ?? string.Empty;
			var version = GetStringPropertyCaseInsensitive(document.RootElement, "version") ?? string.Empty;
			return (id, version);
		}
		catch (JsonException)
		{
			return (string.Empty, string.Empty);
		}
	}

	private static string? GetStringPropertyCaseInsensitive(JsonElement element, string name)
	{
		foreach (var property in element.EnumerateObject())
		{
			if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
				property.Value.ValueKind == JsonValueKind.String)
			{
				return property.Value.GetString();
			}
		}

		return null;
	}

	private static string ComputeSha256(string path)
	{
		using var stream = File.OpenRead(path);
		return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(stream));
	}

	private static ManifestValidationResult Build(PluginManifest manifest,
		List<ManifestProblem> problems,
		string subject,
		PluginManifestValidationLevel level)
	{
		var hasError = problems.Any(problem => problem.Severity == ManifestProblemSeverity.Error);

		return new ManifestValidationResult
		{
			Valid = !hasError,
			ExitCode = hasError ? ExitCode.SubjectInvalid : ExitCode.Success,
			PluginId = manifest.Id,
			Version = manifest.Version,
			Problems = problems,
			Manifest = manifest,
			Subject = subject,
			Level = level
		};
	}

	private static ManifestValidationResult Failure(int exitCode,
		string code,
		string message,
		string subject,
		PluginManifestValidationLevel level = PluginManifestValidationLevel.Development)
	{
		return new ManifestValidationResult
		{
			Valid = false,
			ExitCode = exitCode,
			Problems = [Problem(ManifestProblemSeverity.Error, code, message)],
			Subject = subject,
			Level = level
		};
	}

	private static ManifestProblem Problem(ManifestProblemSeverity severity,
		string code,
		string message,
		string? pointer = null)
		=> new() { Severity = severity, Code = code, Message = message, Pointer = pointer };

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// Best effort: this is temp-directory cleanup, never something a validation result should fail
			// over.
		}
	}
}
