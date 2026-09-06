using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Packaging.Manifest;
using Serilog;

namespace MacroDeck.Plugin.Packaging.Artifacts;

/// <summary>
/// <see cref="IPluginArtifactReader"/> implementation. Every byte total is counted while streaming
/// through the entry, never taken from <see cref="ZipArchiveEntry.Length"/> - a hostile archive lies
/// about it, the same lesson <c>PortableArchive</c> already records. The manifest's own <c>id</c> and
/// <c>version</c> are probed out of the raw JSON (mirroring how <c>PluginManifestReader</c> probes
/// <c>manifestVersion</c>) because the archive defines them; there is no directory name to check against
/// yet.
/// </summary>
public sealed class PluginArtifactReader : IPluginArtifactReader
{
	private const int CopyBufferSize = 81_920;

	private readonly IPluginManifestReader _manifestReader;
	private readonly ILogger _logger;

	public PluginArtifactReader(IPluginManifestReader manifestReader, ILogger logger)
	{
		_manifestReader = manifestReader;
		_logger = logger.ForContext<PluginArtifactReader>();
	}

	public async Task<PluginArtifactInspectionResult> Inspect(string artifactPath,
		CancellationToken cancellationToken = default)
	{
		var fileInfoResult = TryGetFileInfo(artifactPath);
		if (fileInfoResult.Error is { } sizeError)
		{
			return PluginArtifactInspectionResult.Fail(sizeError, fileInfoResult.Message!);
		}

		var fileInfo = fileInfoResult.FileInfo!;

		ZipArchive archive;
		try
		{
			archive = await ZipFile.OpenReadAsync(artifactPath, cancellationToken);
		}
		catch (InvalidDataException ex)
		{
			return PluginArtifactInspectionResult.Fail(PluginInstallError.InvalidArchive, ex.Message);
		}

		await using (archive)
		{
			if (archive.Entries.Count > PluginArtifactLimits.MaxEntries)
			{
				return PluginArtifactInspectionResult.Fail(PluginInstallError.ArtifactLimitExceeded,
					$"The archive contains more than {PluginArtifactLimits.MaxEntries} entries.");
			}

			// A throwaway rooted path so PluginArtifactEntryPolicy.Resolve runs the identical safety
			// check ExtractTo uses. Inspect writes nothing; only the Allowed/Rejection verdict matters.
			var notionalRoot = Path.Combine(Path.GetTempPath(), "inspect");
			var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			byte[]? manifestBytes = null;
			var totalUncompressed = 0L;

			foreach (var entry in archive.Entries)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var entryFailure = ValidateEntry(entry, notionalRoot, seenNames, out var decision);
				if (entryFailure is { } failure)
				{
					return PluginArtifactInspectionResult.Fail(failure.Error, failure.Message);
				}

				if (IsDirectoryEntry(entry.FullName))
				{
					continue;
				}

				if (IsRootManifestEntry(entry.FullName))
				{
					manifestBytes = await ReadEntryMaterialized(entry,
						PluginArtifactLimits.MaxManifestBytes,
						cancellationToken);
					if (manifestBytes is null)
					{
						return PluginArtifactInspectionResult.Fail(PluginInstallError.ManifestInvalid,
							$"The manifest exceeds the {PluginArtifactLimits.MaxManifestBytes}-byte limit.");
					}

					totalUncompressed += manifestBytes.Length;
				}
				else
				{
					var counted = await CountEntryBytes(entry, PluginArtifactLimits.MaxEntryBytes, cancellationToken);
					if (counted is null)
					{
						return PluginArtifactInspectionResult.Fail(PluginInstallError.ArtifactLimitExceeded,
							$"The archive entry '{entry.FullName}' exceeds the " +
							$"{PluginArtifactLimits.MaxEntryBytes}-byte per-entry limit.");
					}

					totalUncompressed += counted.Value;
				}

				if (totalUncompressed > PluginArtifactLimits.MaxTotalUncompressedBytes)
				{
					return PluginArtifactInspectionResult.Fail(PluginInstallError.ArtifactLimitExceeded,
						$"The archive exceeds the {PluginArtifactLimits.MaxTotalUncompressedBytes}-byte total " +
						"uncompressed limit.");
				}
			}

			if (manifestBytes is null)
			{
				return PluginArtifactInspectionResult.Fail(PluginInstallError.ManifestMissing,
					$"The archive contains no '{PluginArtifactFiles.ManifestFileName}' at its root.");
			}

			var manifestResult = ReadManifestCore(manifestBytes, _manifestReader);
			if (!manifestResult.Success)
			{
				return PluginArtifactInspectionResult.Fail(manifestResult.Error!.Value, manifestResult.Message!);
			}

			if (ExceedsCompressionRatio(totalUncompressed, fileInfo.Length))
			{
				return PluginArtifactInspectionResult.Fail(PluginInstallError.ArtifactLimitExceeded,
					"The archive's compression ratio exceeds the configured limit.");
			}

			return PluginArtifactInspectionResult.Ok(manifestResult.Manifest!,
				archive.Entries.Count,
				totalUncompressed);
		}
	}

	public async Task<PluginArtifactExtractResult> ExtractTo(string artifactPath,
		string targetDirectory,
		CancellationToken cancellationToken = default)
	{
		var fileInfoResult = TryGetFileInfo(artifactPath);
		if (fileInfoResult.Error is { } sizeError)
		{
			return PluginArtifactExtractResult.Fail(sizeError, fileInfoResult.Message!);
		}

		var fileInfo = fileInfoResult.FileInfo!;

		ZipArchive archive;
		try
		{
			archive = await ZipFile.OpenReadAsync(artifactPath, cancellationToken);
		}
		catch (InvalidDataException ex)
		{
			return PluginArtifactExtractResult.Fail(PluginInstallError.InvalidArchive, ex.Message);
		}

		await using (archive)
		{
			if (archive.Entries.Count > PluginArtifactLimits.MaxEntries)
			{
				return PluginArtifactExtractResult.Fail(PluginInstallError.ArtifactLimitExceeded,
					$"The archive contains more than {PluginArtifactLimits.MaxEntries} entries.");
			}

			Directory.CreateDirectory(targetDirectory);

			var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var extractedFiles = new List<string>();
			var totalUncompressed = 0L;

			try
			{
				foreach (var entry in archive.Entries)
				{
					cancellationToken.ThrowIfCancellationRequested();

					var entryFailure = ValidateEntry(entry, targetDirectory, seenNames, out var decision);
					if (entryFailure is { } failure)
					{
						CleanUp(targetDirectory);
						return PluginArtifactExtractResult.Fail(failure.Error, failure.Message);
					}

					if (IsDirectoryEntry(entry.FullName))
					{
						Directory.CreateDirectory(decision.ResolvedPath!);
						continue;
					}

					var destinationDirectory = Path.GetDirectoryName(decision.ResolvedPath!);
					if (!string.IsNullOrEmpty(destinationDirectory))
					{
						Directory.CreateDirectory(destinationDirectory);
					}

					var written = await CopyEntryBounded(entry,
						decision.ResolvedPath!,
						PluginArtifactLimits.MaxEntryBytes,
						cancellationToken);
					if (written is null)
					{
						CleanUp(targetDirectory);
						return PluginArtifactExtractResult.Fail(PluginInstallError.ArtifactLimitExceeded,
							$"The archive entry '{entry.FullName}' exceeds the " +
							$"{PluginArtifactLimits.MaxEntryBytes}-byte per-entry limit.");
					}

					totalUncompressed += written.Value;
					if (totalUncompressed > PluginArtifactLimits.MaxTotalUncompressedBytes)
					{
						CleanUp(targetDirectory);
						return PluginArtifactExtractResult.Fail(PluginInstallError.ArtifactLimitExceeded,
							$"The archive exceeds the {PluginArtifactLimits.MaxTotalUncompressedBytes}-byte total " +
							"uncompressed limit.");
					}

					extractedFiles.Add(Path.GetRelativePath(targetDirectory, decision.ResolvedPath!)
						.Replace(Path.DirectorySeparatorChar, '/'));
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				CleanUp(targetDirectory);
				return PluginArtifactExtractResult.Fail(PluginInstallError.StagingFailed,
					$"Extracting the plugin artifact failed: {ex.Message}");
			}

			if (ExceedsCompressionRatio(totalUncompressed, fileInfo.Length))
			{
				CleanUp(targetDirectory);
				return PluginArtifactExtractResult.Fail(PluginInstallError.ArtifactLimitExceeded,
					"The archive's compression ratio exceeds the configured limit.");
			}

			return PluginArtifactExtractResult.Ok(extractedFiles, totalUncompressed);
		}
	}

	/// <summary>Runs the entry-name policy, the unsafe-file-type check and duplicate detection shared by
	/// <see cref="Inspect"/> and <see cref="ExtractTo"/>. Returns the failure to report, or null when the
	/// entry is safe to process further.</summary>
	private static (PluginInstallError Error, string Message)? ValidateEntry(ZipArchiveEntry entry,
		string root,
		HashSet<string> seenNames,
		out PluginArtifactEntryDecision decision)
	{
		decision = PluginArtifactEntryPolicy.Resolve(entry.FullName, root);
		if (!decision.Allowed)
		{
			var error = decision.Rejection == PluginArtifactEntryRejection.PathTooLong
				? PluginInstallError.ArtifactLimitExceeded
				: PluginInstallError.UnsafeEntry;
			return (error, decision.Message!);
		}

		if (PluginArtifactEntryPolicy.IsUnsafeFileType(entry.ExternalAttributes))
		{
			return (PluginInstallError.UnsafeEntry,
				$"The archive entry '{entry.FullName}' is not a regular file or directory.");
		}

		if (!seenNames.Add(NormalizeEntryName(entry.FullName)))
		{
			return (PluginInstallError.UnsafeEntry,
				$"The archive contains a duplicate entry '{entry.FullName}'.");
		}

		return null;
	}

	/// <summary>Turns raw manifest bytes into a validated <see cref="PluginManifest"/>, probing <c>id</c>
	/// and <c>version</c> out of the JSON first since the archive defines them and there is no directory
	/// name to check against yet.</summary>
	private static ManifestReadOutcome ReadManifestCore(byte[] manifestBytes, IPluginManifestReader manifestReader)
	{
		var json = DecodeManifestJson(manifestBytes);
		if (!TryProbeIdAndVersion(json, out var manifestId, out var manifestVersion))
		{
			return ManifestReadOutcome.Fail(PluginInstallError.ManifestInvalid,
				"The manifest has no string 'id' and 'version' properties.");
		}

		var result = manifestReader.ReadFromJson(json, versionDirectory: null, manifestId, manifestVersion);
		if (result.Success)
		{
			return ManifestReadOutcome.Ok(result.Manifest!);
		}

		var error = result.Error == PluginManifestError.UnsupportedManifestVersion
			? PluginInstallError.Incompatible
			: PluginInstallError.ManifestInvalid;
		return ManifestReadOutcome.Fail(error, result.ErrorMessage ?? "The manifest is invalid.");
	}

	private readonly record struct ManifestReadOutcome(
		bool Success,
		PluginManifest? Manifest,
		PluginInstallError? Error,
		string? Message)
	{
		public static ManifestReadOutcome Ok(PluginManifest manifest) => new(true, manifest, null, null);

		public static ManifestReadOutcome Fail(PluginInstallError error, string message) =>
			new(false, null, error, message);
	}

	private static bool ExceedsCompressionRatio(long totalUncompressed, long archiveBytes)
	{
		if (archiveBytes <= 0)
		{
			return false;
		}

		var ratio = (double)totalUncompressed / archiveBytes;
		return ratio > PluginArtifactLimits.MaxCompressionRatio;
	}

	private void CleanUp(string targetDirectory)
	{
		try
		{
			if (Directory.Exists(targetDirectory))
			{
				Directory.Delete(targetDirectory, recursive: true);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			PluginArtifactReaderLog.ExtractCleanupFailed(_logger, targetDirectory, ex);
		}
	}

	private static (FileInfo? FileInfo, PluginInstallError? Error, string? Message) TryGetFileInfo(string artifactPath)
	{
		FileInfo fileInfo;
		try
		{
			fileInfo = new FileInfo(artifactPath);
			if (!fileInfo.Exists)
			{
				return (null, PluginInstallError.ArtifactNotFound, $"No artifact at '{artifactPath}'.");
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
		{
			return (null, PluginInstallError.ArtifactNotFound, ex.Message);
		}

		if (fileInfo.Length > PluginArtifactLimits.MaxArchiveBytes)
		{
			return (null, PluginInstallError.ArtifactTooLarge,
				$"The artifact exceeds the {PluginArtifactLimits.MaxArchiveBytes}-byte limit.");
		}

		return (fileInfo, null, null);
	}

	private static string NormalizeEntryName(string entryFullName) => entryFullName.Replace('\\', '/');

	private static bool IsDirectoryEntry(string entryFullName) => NormalizeEntryName(entryFullName).EndsWith('/');

	private static bool IsRootManifestEntry(string entryFullName)
	{
		var normalized = NormalizeEntryName(entryFullName);
		return !normalized.Contains('/') &&
			string.Equals(normalized, PluginArtifactFiles.ManifestFileName, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Reads an entry fully into memory, or returns null once it grows past <paramref
	/// name="limit"/>. Only used for the manifest, which the artifact limits already cap far below any
	/// size worth streaming.</summary>
	private static async Task<byte[]?> ReadEntryMaterialized(ZipArchiveEntry entry,
		long limit,
		CancellationToken cancellationToken)
	{
		await using var source = await entry.OpenAsync(cancellationToken);
		using var buffer = new MemoryStream();
		var chunk = new byte[CopyBufferSize];
		int read;
		while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
		{
			if (buffer.Length + read > limit)
			{
				return null;
			}

			await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
		}

		return buffer.ToArray();
	}

	/// <summary>Streams an entry and counts its bytes without keeping them, for every entry
	/// <see cref="Inspect"/> does not need the content of.</summary>
	private static async Task<long?> CountEntryBytes(ZipArchiveEntry entry,
		long limit,
		CancellationToken cancellationToken)
	{
		await using var source = await entry.OpenAsync(cancellationToken);
		var chunk = new byte[CopyBufferSize];
		var total = 0L;
		int read;
		while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
		{
			total += read;
			if (total > limit)
			{
				return null;
			}
		}

		return total;
	}

	private static async Task<long?> CopyEntryBounded(ZipArchiveEntry entry,
		string destinationPath,
		long limit,
		CancellationToken cancellationToken)
	{
		await using var source = await entry.OpenAsync(cancellationToken);
		await using var destination
			= new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
		var chunk = new byte[CopyBufferSize];
		var total = 0L;
		int read;
		while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
		{
			total += read;
			if (total > limit)
			{
				return null;
			}

			await destination.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
		}

		return total;
	}

	/// <summary>Strips a leading UTF-8 BOM, which <see cref="Encoding.UTF8"/>.GetString does not do on
	/// its own and which would otherwise make <see cref="JsonDocument.Parse(string, JsonDocumentOptions)"/>
	/// reject an otherwise well-formed manifest.</summary>
	private static string DecodeManifestJson(byte[] manifestBytes)
	{
		var span = manifestBytes.AsSpan();
		if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
		{
			span = span[3..];
		}

		return Encoding.UTF8.GetString(span);
	}

	/// <summary>Probes the manifest's own <c>id</c> and <c>version</c> the same way
	/// <c>PluginManifestReader</c> probes <c>manifestVersion</c>: cheaply, before the full deserialize,
	/// and case-insensitively since JSON property casing is not part of the contract.</summary>
	private static bool TryProbeIdAndVersion(string json, out string id, out string version)
	{
		id = string.Empty;
		version = string.Empty;

		try
		{
			using var document = JsonDocument.Parse(json);
			if (!TryGetPropertyCaseInsensitive(document.RootElement, "id", out var idElement) ||
				idElement.ValueKind != JsonValueKind.String ||
				!TryGetPropertyCaseInsensitive(document.RootElement, "version", out var versionElement) ||
				versionElement.ValueKind != JsonValueKind.String)
			{
				return false;
			}

			id = idElement.GetString() ?? string.Empty;
			version = versionElement.GetString() ?? string.Empty;
			return id.Length > 0 && version.Length > 0;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static bool TryGetPropertyCaseInsensitive(JsonElement element, string name, out JsonElement value)
	{
		foreach (var property in element.EnumerateObject())
		{
			if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
			{
				value = property.Value;
				return true;
			}
		}

		value = default;
		return false;
	}
}
