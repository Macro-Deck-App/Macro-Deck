using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeck.Plugin.Cli.Manifests;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.Packing;

/// <summary>
/// Builds a <c>.macroDeckPlugin</c> artifact from a source tree.
/// <para>
/// Deliberately independent of <c>host/tests/MacroDeckHost.Tests.UnitTests/.../PluginArtifactBuilder.cs</c>,
/// the host's own hand-rolled test artifact writer - sharing code with it would let this packer and the
/// installer's own test fixtures drift in the same direction and still agree with each other, which is
/// exactly the blind spot <c>PluginInstallerTests</c> exists to avoid. This type shares only the
/// <em>reused, validated</em> policy (<see cref="PluginArtifactEntryPolicy" />, <see cref="PluginArtifactLimits" />)
/// every other artifact writer or reader in this codebase is bound by, never a shortcut around it.
/// </para>
/// <para>
/// Every check below runs against the source tree <em>before</em> a single byte is written to the output
/// path, so a rejected entry never produces a partial or invalid artifact.
/// </para>
/// </summary>
internal static class PluginPacker
{
	/// <summary>
	/// A local copy of <see cref="PluginManifestJson.Options" /> - never that shared instance itself - with
	/// null-valued optional properties suppressed on write. <see cref="PluginManifestJson.Options" /> is
	/// also <c>PersistenceJsonOptions.Default</c> in the host, the identical singleton every host store
	/// (profiles, icon packs, scripts, ADB state, and more) serializes through; adding
	/// <see cref="JsonIgnoreCondition.WhenWritingNull" /> there would silently change the on-disk shape of
	/// all of them, none of which declare their own per-property <c>[JsonIgnore]</c> for it today. This
	/// packer is the first thing in the repository to serialize a whole <see cref="PluginManifest" /> back
	/// out rather than only ever reading one, and the embedded schema requires an absent optional property,
	/// never an explicit <c>null</c> - so the fix belongs here, scoped to this one write.
	/// </summary>
	private static readonly JsonSerializerOptions _packedManifestWriteOptions =
		new(PluginManifestJson.Options) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

	/// <param name="sourceDirectory">The payload tree to pack.</param>
	/// <param name="manifestPath">The manifest to validate and rebuild <c>files[]</c> into.</param>
	/// <param name="resolveOutputPath">Computes the output path from the validated manifest - deferred
	/// rather than a plain string, since the default output path (<c>&lt;id&gt;-&lt;version&gt;.macroDeckPlugin</c>)
	/// is only knowable once the manifest has actually been read.</param>
	/// <param name="force">Overwrite an existing file at the resolved output path.</param>
	public static async Task<PluginPackResult> PackAsync(string sourceDirectory,
		string manifestPath,
		Func<PluginManifest, string> resolveOutputPath,
		bool force,
		CancellationToken cancellationToken = default)
	{
		if (!Directory.Exists(sourceDirectory))
		{
			return PluginPackResult.Fail(PluginPackFailureReason.SourceNotFound,
				$"No source directory at '{CliText.DisplayPath(sourceDirectory)}'.");
		}

		// The same validation validate itself runs - a bad manifest can never become an artifact through
		// this command.
		var validation = await ManifestValidator
			.ValidateManifestFileAsync(manifestPath, cancellationToken: cancellationToken)
			.ConfigureAwait(false);

		if (!validation.Valid || validation.Manifest is not { } manifest)
		{
			return PluginPackResult.ValidationFailed(validation);
		}

		var outputPath = resolveOutputPath(manifest);

		if (File.Exists(outputPath) && !force)
		{
			return PluginPackResult.Fail(PluginPackFailureReason.OutputExists,
				$"'{CliText.DisplayPath(outputPath)}' already exists. Pass --force to overwrite it.");
		}

		var sourceRoot = Path.GetFullPath(sourceDirectory);
		var fullManifestPath = Path.GetFullPath(manifestPath);

		var payloadResult = CollectPayloadEntries(sourceRoot, Path.GetFullPath(outputPath));
		if (payloadResult.Rejection is { } rejection)
		{
			return rejection;
		}

		var payload = payloadResult.Entries;

		if (payload.Count + 1 > PluginArtifactLimits.MaxEntries)
		{
			return PluginPackResult.Fail(PluginPackFailureReason.LimitExceeded,
				$"The source tree contains more than {PluginArtifactLimits.MaxEntries - 1} files.");
		}

		var digestFiles = new List<PluginFileDigest>(payload.Count);
		long totalUncompressedBytes = 0;

		foreach (var entry in payload)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var length = entry.FileInfo.Length;
			if (length > PluginArtifactLimits.MaxEntryBytes)
			{
				return PluginPackResult.Fail(PluginPackFailureReason.LimitExceeded,
					$"'{entry.RelativePath}' is {length} bytes, over the {PluginArtifactLimits.MaxEntryBytes}-byte " +
					"per-entry limit.");
			}

			totalUncompressedBytes += length;

			digestFiles.Add(new PluginFileDigest
			{
				Path = entry.RelativePath,
				Sha256 = ComputeSha256(entry.FileInfo.FullName),
				Size = length
			});
		}

		var manifestDirectory = Path.GetDirectoryName(fullManifestPath) ?? sourceRoot;

		// Only recomputed where the resource files can actually be seen - a project tree. Packing a staged
		// or published payload discovers nothing, and there the manifest's own value is the one thing that
		// still knows which languages went into the build, so it is carried rather than cleared.
		var discoveredLanguages = PluginLanguages.Discover(manifestDirectory);

		// Recomputed unconditionally: whatever files[] the source manifest declared (if any) reflected a
		// previous, possibly stale, build. signature passes through untouched - this tool cannot sign, so
		// preserving whatever was already there is the only honest choice; a real signer re-signs after
		// the files[] this recompute just produced.
		var packagedManifest = manifest with
		{
			Files = [.. digestFiles.OrderBy(file => file.Path, StringComparer.Ordinal)],
			Languages = discoveredLanguages.Count > 0 ? discoveredLanguages : manifest.Languages
		};
		var manifestJson = JsonSerializer.SerializeToUtf8Bytes(packagedManifest, _packedManifestWriteOptions);

		if (manifestJson.Length > PluginArtifactLimits.MaxManifestBytes)
		{
			return PluginPackResult.Fail(PluginPackFailureReason.LimitExceeded,
				$"The recomputed manifest is {manifestJson.Length} bytes, over the " +
				$"{PluginArtifactLimits.MaxManifestBytes}-byte limit.");
		}

		totalUncompressedBytes += manifestJson.Length;

		if (totalUncompressedBytes > PluginArtifactLimits.MaxTotalUncompressedBytes)
		{
			return PluginPackResult.Fail(PluginPackFailureReason.LimitExceeded,
				$"The archive would total {totalUncompressedBytes} uncompressed bytes, over the " +
				$"{PluginArtifactLimits.MaxTotalUncompressedBytes}-byte limit.");
		}

		// Checked before a byte is written, consistent with this type's own invariant (see remarks) - a
		// warning about what the artifact will contain is still worth computing from the payload list
		// alone, without touching disk again.
		var warnings = new List<CliDiagnostic>();
		var packedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			PluginArtifactFiles.ManifestFileName
		};
		foreach (var entry in payload)
		{
			packedEntryNames.Add(EntrypointPresence.Normalize(entry.RelativePath));
		}

		warnings.AddRange(EntrypointPresence.Missing(packagedManifest, packedEntryNames, "artifact"));

		// Publication readiness is always evaluated - pack has no --level flag - and never changes the exit
		// code (see PluginPackReporter). The generated-field check runs against the pre-repack source
		// manifest, never packagedManifest: that one's Files is always populated by this point (recomputed
		// just above), which would make it warn on every single pack.
		warnings.AddRange(RequirementProblems.PublicationWarnings(packagedManifest));
		warnings.AddRange(RequirementProblems.GeneratedFieldWarnings(manifest,
			ManifestValidator.HasProjectFile(manifestDirectory)));

		warnings.AddRange(PluginLanguages.RecomputedWarnings(manifest.Languages, discoveredLanguages));

		if (LooksLikeDebugBuild(sourceRoot))
		{
			warnings.Add(new CliDiagnostic("source-looks-like-debug-build",
				$"'{CliText.DisplayPath(sourceDirectory)}' looks like a Debug build (bin/Debug/...). " +
				"Pack a Release build for distribution."));
		}

		var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
		var createdOutputDirectory = !string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory)
			? outputDirectory
			: null;

		try
		{
			await WriteArchiveAsync(outputPath, manifestJson, payload, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return PluginPackResult.Fail(PluginPackFailureReason.WriteFailed, ex.Message);
		}

		return PluginPackResult.Ok(outputPath,
			packagedManifest.Id,
			packagedManifest.Version,
			payload.Count + 1,
			totalUncompressedBytes,
			warnings,
			createdOutputDirectory);
	}

	/// <summary>Detects a Debug-looking source tree by path <em>segments</em>, not substring, on the fully
	/// resolved source path: a segment equal to <c>Debug</c> (case-insensitive) whose immediately preceding
	/// segment is <c>bin</c>. Segment matching so <c>.../MyDebugger/bin/Release/...</c> never triggers it,
	/// and <see cref="Path.GetFullPath(string)" /> so a relative <c>--source bin/Debug/net10.0</c> still
	/// does.</summary>
	private static bool LooksLikeDebugBuild(string fullSourcePath)
	{
		var segments = fullSourcePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

		for (var i = 1; i < segments.Length; i++)
		{
			if (string.Equals(segments[i], "Debug", StringComparison.OrdinalIgnoreCase) &&
				string.Equals(segments[i - 1], "bin", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private static async Task WriteArchiveAsync(string outputPath,
		byte[] manifestJson,
		IReadOnlyList<PayloadEntry> payload,
		CancellationToken cancellationToken)
	{
		var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		// Written to a sibling temporary file and moved into place, so a reader can never observe a
		// half-written archive at outputPath, and an interrupted pack never leaves outputPath corrupt when
		// --force is overwriting one that already worked.
		var temporaryPath = outputPath + ".tmp";

		try
		{
			await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
			await using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
			{
				var manifestEntry = archive.CreateEntry(PluginArtifactFiles.ManifestFileName, CompressionLevel.Optimal);
				await using (var entryStream = manifestEntry.Open())
				{
					await entryStream.WriteAsync(manifestJson, cancellationToken).ConfigureAwait(false);
				}

				// Sorted so two packs of an unchanged source tree produce the same entry order - not a
				// byte-for-byte reproducibility guarantee (timestamps and the deflate stream are not
				// pinned), but not left to directory enumeration order either.
				foreach (var entry in payload.OrderBy(e => e.RelativePath, StringComparer.Ordinal))
				{
					cancellationToken.ThrowIfCancellationRequested();

					var zipEntry = archive.CreateEntry(entry.RelativePath, CompressionLevel.Optimal);
					await using var entryStream = zipEntry.Open();
					await using var source = File.OpenRead(entry.FileInfo.FullName);
					await source.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
				}
			}

			File.Move(temporaryPath, outputPath, overwrite: true);
		}
		finally
		{
			if (File.Exists(temporaryPath))
			{
				File.Delete(temporaryPath);
			}
		}
	}

	private static (IReadOnlyList<PayloadEntry> Entries, PluginPackResult? Rejection) CollectPayloadEntries(
		string sourceRoot,
		string fullOutputPath)
	{
		var entries = new List<PayloadEntry>();

		foreach (var absolutePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
		{
			if (PluginArtifactFiles.HasArtifactExtension(absolutePath) ||
				string.Equals(absolutePath,
					fullOutputPath,
					OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var relativePath = Path.GetRelativePath(sourceRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');

			// The archive's manifest.json is always the recomputed one built from --manifest's content,
			// never a raw copy of whatever the source tree happens to contain under that name - see this
			// type's own remarks on why files[] is always recomputed.
			if (string.Equals(relativePath, PluginArtifactFiles.ManifestFileName, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var fileInfo = new FileInfo(absolutePath);

			if (fileInfo.LinkTarget is not null)
			{
				return (entries, PluginPackResult.Fail(PluginPackFailureReason.SourceEntryRejected,
					$"'{relativePath}' is a symbolic link, which this tool refuses to pack: its target may " +
					"resolve outside the source tree, and a fresh copy written by CreateEntry carries no mode " +
					"bits to preserve it as one on the other end anyway."));
			}

			// The same path-safety judgement a real archive entry is judged against - illegal characters,
			// reserved names, and the length/depth limits - applied here, before packing, so this tool can
			// never produce an artifact IPluginArtifactReader would itself reject on the way back in.
			var decision = PluginArtifactEntryPolicy.Resolve(relativePath, sourceRoot);
			if (!decision.Allowed)
			{
				return (entries, PluginPackResult.Fail(PluginPackFailureReason.SourceEntryRejected,
					$"'{relativePath}': {decision.Message}"));
			}

			entries.Add(new PayloadEntry(relativePath, fileInfo));
		}

		return (entries, null);
	}

	private static string ComputeSha256(string path)
	{
		using var stream = File.OpenRead(path);
		return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(stream));
	}

	private readonly record struct PayloadEntry(string RelativePath, FileInfo FileInfo);
}
