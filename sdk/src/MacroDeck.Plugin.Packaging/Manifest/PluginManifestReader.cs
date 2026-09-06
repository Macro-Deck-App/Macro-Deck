using System.Globalization;
using System.Text.Json;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Versioning;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Handshake;

namespace MacroDeck.Plugin.Packaging.Manifest;

/// <summary>
/// Reads and validates <c>manifest.json</c>. <see cref="PluginManifest.ManifestVersion"/> is probed
/// before the full deserialize so an unsupported version is reported precisely, rather than surfacing
/// as whatever unrelated deserialization error the rest of the (possibly future-shaped) document would
/// produce. Every entrypoint's path is validated for safety regardless of RID - a manifest with an
/// escaping path is invalid on every platform. Only the entrypoint(s) matching the current RID have
/// their file existence checked here; a manifest with no entrypoint for this host is still a valid
/// manifest; that is <see cref="PluginSupervisorError.NoEntrypointForRuntime"/>, decided by the
/// supervisor at launch time, not a read failure.
/// </summary>
public sealed class PluginManifestReader : IPluginManifestReader
{
	/// <summary>Matches <see cref="PluginId.MaxLength"/> and the protocol's declared-name bound.</summary>
	private const int MaxNameLength = 128;

	private static readonly string[] _forbiddenScriptExtensions = [".sh", ".bat", ".cmd", ".ps1", ".command"];

	public PluginManifestReadResult Read(string manifestPath, string expectedPluginId, string expectedVersion)
	{
		FileInfo fileInfo;
		try
		{
			fileInfo = new FileInfo(manifestPath);
			if (!fileInfo.Exists)
			{
				return PluginManifestReadResult.Fail(PluginManifestError.NotFound,
					$"No manifest at '{manifestPath}'.");
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
		{
			return PluginManifestReadResult.Fail(PluginManifestError.NotFound, ex.Message);
		}

		if (fileInfo.Length > PluginArtifactLimits.MaxManifestBytes)
		{
			return PluginManifestReadResult.Fail(PluginManifestError.Malformed,
				$"Manifest exceeds the {PluginArtifactLimits.MaxManifestBytes}-byte limit.");
		}

		string json;
		try
		{
			json = File.ReadAllText(manifestPath);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return PluginManifestReadResult.Fail(PluginManifestError.NotFound, ex.Message);
		}

		return ReadFromJson(json, fileInfo.DirectoryName, expectedPluginId, expectedVersion);
	}

	public PluginManifestReadResult ReadFromJson(string json,
		string? versionDirectory,
		string expectedPluginId,
		string expectedVersion)
	{
		int manifestVersion;
		try
		{
			using var probe = JsonDocument.Parse(json);
			if (!TryGetPropertyCaseInsensitive(probe.RootElement, "manifestVersion", out var versionElement) ||
				versionElement.ValueKind != JsonValueKind.Number ||
				!versionElement.TryGetInt32(out manifestVersion))
			{
				return PluginManifestReadResult.Fail(PluginManifestError.Malformed,
					"Manifest has no numeric 'manifestVersion' property.");
			}
		}
		catch (JsonException ex)
		{
			return PluginManifestReadResult.Fail(PluginManifestError.Malformed, ex.Message);
		}

		if (manifestVersion != PluginManifest.SupportedManifestVersion)
		{
			return PluginManifestReadResult.Fail(PluginManifestError.UnsupportedManifestVersion,
				$"Manifest version {manifestVersion} is not supported; only " +
				$"{PluginManifest.SupportedManifestVersion} is understood.");
		}

		PluginManifest? manifest;
		try
		{
			manifest = JsonSerializer.Deserialize<PluginManifest>(json, PluginManifestJson.Options);
		}
		catch (JsonException ex)
		{
			return PluginManifestReadResult.Fail(PluginManifestError.Malformed, ex.Message);
		}

		if (manifest is null)
		{
			return PluginManifestReadResult.Fail(PluginManifestError.Malformed, "Manifest deserialized to null.");
		}

		if (!PluginId.IsValid(manifest.Id))
		{
			return PluginManifestReadResult.Fail(PluginManifestError.InvalidPluginId,
				$"'{manifest.Id}' is not a valid plugin id.");
		}

		if (!string.Equals(manifest.Id, expectedPluginId, StringComparison.Ordinal))
		{
			return PluginManifestReadResult.Fail(PluginManifestError.IdMismatch,
				$"Manifest id '{manifest.Id}' does not match its directory '{expectedPluginId}'.");
		}

		if (!IsLegalPathSegment(manifest.Version) ||
			!string.Equals(manifest.Version, expectedVersion, StringComparison.Ordinal))
		{
			return PluginManifestReadResult.Fail(PluginManifestError.VersionMismatch,
				$"Manifest version '{manifest.Version}' does not match its directory '{expectedVersion}'.");
		}

		if (ValidateName(manifest) is { } nameError)
		{
			return nameError;
		}

		if (manifest.Entrypoints.Count == 0)
		{
			return PluginManifestReadResult.Fail(PluginManifestError.NoEntrypoints,
				"Manifest declares no entrypoints.");
		}

		foreach (var (rid, entrypoint) in manifest.Entrypoints)
		{
			string? resolvedPath = null;
			if (versionDirectory is not null)
			{
				if (!TryResolveWithinVersionDirectory(versionDirectory, entrypoint.Executable, out var resolved))
				{
					return PluginManifestReadResult.Fail(PluginManifestError.EntrypointOutsideVersionDirectory,
						$"Entrypoint '{rid}' executable '{entrypoint.Executable}' escapes the version directory.");
				}

				resolvedPath = resolved;
			}
			else if (!IsSafeRelativeExecutablePath(entrypoint.Executable))
			{
				return PluginManifestReadResult.Fail(PluginManifestError.EntrypointOutsideVersionDirectory,
					$"Entrypoint '{rid}' executable '{entrypoint.Executable}' is not a safe relative path.");
			}

			// Only the entrypoint(s) this host could actually launch need to exist on disk - a manifest
			// legitimately ships entrypoints for platforms it was not built for on this machine. There is
			// nothing to check against until a version directory exists on disk.
			if (resolvedPath is not null &&
				PluginRuntimeIdentifiers.CandidatesFor(PluginRuntimeIdentifiers.Current).Contains(rid) &&
				!File.Exists(resolvedPath))
			{
				return PluginManifestReadResult.Fail(PluginManifestError.EntrypointMissing,
					$"Entrypoint '{rid}' resolves to '{resolvedPath}', which does not exist.");
			}
		}

		if (ValidateEntrypointRuntimes(manifest) is { } entrypointRuntimeError)
		{
			return entrypointRuntimeError;
		}

		if (ValidateIcon(manifest) is { } iconError)
		{
			return iconError;
		}

		if (ValidatePublisherAndLinks(manifest) is { } publisherError)
		{
			return publisherError;
		}

		if (ValidateCompatibility(manifest) is { } compatibilityError)
		{
			return compatibilityError;
		}

		if (ValidateDependenciesAndConflicts(manifest) is { } dependencyError)
		{
			return dependencyError;
		}

		if (ValidateIconPacks(manifest) is { } iconPackError)
		{
			return iconPackError;
		}

		if (ValidatePermissions(manifest) is { } permissionError)
		{
			return permissionError;
		}

		if (ValidateLanguages(manifest) is { } languageError)
		{
			return languageError;
		}

		if (ValidateFiles(manifest) is { } fileError)
		{
			return fileError;
		}

		if (ValidateSignature(manifest) is { } signatureError)
		{
			return signatureError;
		}

		var clamped = ClampSettings(manifest);
		return PluginManifestReadResult.Ok(clamped);
	}

	/// <summary>The script-extension check applies even when <see cref="PluginEntrypoint.Runtime"/> is
	/// absent - absent means self-contained, and a script entrypoint is an install script in disguise,
	/// which the artifact format forbids regardless of how the entrypoint declares itself.</summary>
	private static PluginManifestReadResult? ValidateEntrypointRuntimes(PluginManifest manifest)
	{
		foreach (var (rid, entrypoint) in manifest.Entrypoints)
		{
			if (HasForbiddenScriptExtension(entrypoint.Executable))
			{
				return PluginManifestReadResult.Fail(PluginManifestError.InvalidEntrypointRuntime,
					$"Entrypoint '{rid}' executable '{entrypoint.Executable}' looks like a script, which " +
					"the artifact format forbids.");
			}

			// An absent runtime block means self-contained, so the self-contained rules below have to
			// apply to it too. Treating "absent" as "unvalidated" would let exactly the manifests that
			// declare nothing skip the check.
			var runtime = entrypoint.Runtime ?? new PluginEntrypointRuntime();

			if (runtime.Kind == PluginEntrypointRuntimeKind.FrameworkDependent)
			{
				if (!IsValidDotnetMajorMinor(runtime.DotnetVersion))
				{
					return PluginManifestReadResult.Fail(PluginManifestError.InvalidEntrypointRuntime,
						$"Entrypoint '{rid}' has an invalid Runtime.DotnetVersion '{runtime.DotnetVersion}'; " +
						"expected 'major.minor'.");
				}

				if (!entrypoint.Executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
				{
					return PluginManifestReadResult.Fail(PluginManifestError.InvalidEntrypointRuntime,
						$"Entrypoint '{rid}' is framework-dependent but its executable does not end in '.dll'.");
				}
			}
			else if (entrypoint.Executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
			{
				return PluginManifestReadResult.Fail(PluginManifestError.InvalidEntrypointRuntime,
					$"Entrypoint '{rid}' is self-contained but its executable ends in '.dll'.");
			}
		}

		return null;
	}

	/// <summary>Rejects blank, control-character-containing, or over-long names. Never trims or clamps -
	/// silently altering a name the user chose is worse than refusing the manifest.</summary>
	private static PluginManifestReadResult? ValidateName(PluginManifest manifest)
	{
		if (string.IsNullOrWhiteSpace(manifest.Name))
		{
			return PluginManifestReadResult.Fail(PluginManifestError.InvalidName, "Name must not be empty.");
		}

		if (manifest.Name.Any(char.IsControl))
		{
			return PluginManifestReadResult.Fail(PluginManifestError.InvalidName,
				"Name must not contain control characters.");
		}

		if (manifest.Name.Length > MaxNameLength)
		{
			return PluginManifestReadResult.Fail(PluginManifestError.InvalidName,
				$"Name exceeds the {MaxNameLength}-character limit.");
		}

		return null;
	}

	private static PluginManifestReadResult? ValidatePublisherAndLinks(PluginManifest manifest)
	{
		if (manifest.Publisher is { } publisher)
		{
			if (string.IsNullOrWhiteSpace(publisher.Name))
			{
				return PluginManifestReadResult.Fail(PluginManifestError.InvalidPublisher,
					"Publisher.Name must not be empty.");
			}

			if (publisher.Id is not null && !PluginId.IsValid(publisher.Id))
			{
				return PluginManifestReadResult.Fail(PluginManifestError.InvalidPublisher,
					$"Publisher id '{publisher.Id}' is not a valid plugin id.");
			}

			if (publisher.Url is not null && !PluginManifestUrls.IsAbsoluteHttpUrl(publisher.Url))
			{
				return PluginManifestReadResult.Fail(PluginManifestError.InvalidPublisher,
					$"Publisher url '{publisher.Url}' must be an absolute http or https URL.");
			}
		}

		if (manifest.Homepage is not null && !PluginManifestUrls.IsAbsoluteHttpUrl(manifest.Homepage))
		{
			return PluginManifestReadResult.Fail(PluginManifestError.Malformed,
				$"Homepage '{manifest.Homepage}' must be an absolute http or https URL.");
		}

		if (manifest.Repository is not null && !PluginManifestUrls.IsAbsoluteHttpUrl(manifest.Repository))
		{
			return PluginManifestReadResult.Fail(PluginManifestError.Malformed,
				$"Repository '{manifest.Repository}' must be an absolute http or https URL.");
		}

		return null;
	}

	/// <summary>Only validates that the declared ranges parse. Whether the running host actually
	/// satisfies them is the installer's decision, not the reader's.</summary>
	private static PluginManifestReadResult? ValidateCompatibility(PluginManifest manifest)
	{
		if (manifest.Compatibility is not { } compatibility)
		{
			return null;
		}

		if (compatibility.Sdk is not null && !SemanticVersionRange.TryParse(compatibility.Sdk, out _))
		{
			return PluginManifestReadResult.Fail(PluginManifestError.InvalidCompatibility,
				$"Compatibility.Sdk '{compatibility.Sdk}' is not a valid version range.");
		}

		if (compatibility.MacroDeck is not null && !SemanticVersionRange.TryParse(compatibility.MacroDeck, out _))
		{
			return PluginManifestReadResult.Fail(PluginManifestError.InvalidCompatibility,
				$"Compatibility.MacroDeck '{compatibility.MacroDeck}' is not a valid version range.");
		}

		if (compatibility.Protocol is { } protocolRange &&
			(protocolRange.Minimum < 1 || protocolRange.Maximum < protocolRange.Minimum))
		{
			return PluginManifestReadResult.Fail(PluginManifestError.InvalidCompatibility,
				$"Compatibility.Protocol range [{protocolRange.Minimum}, {protocolRange.Maximum}] is invalid.");
		}

		return null;
	}

	/// <summary>Beyond shape, rejects authoring errors that can never be satisfied: a self-reference, a
	/// duplicated id within either list, and an id declared in both lists at once.</summary>
	private static PluginManifestReadResult? ValidateDependenciesAndConflicts(PluginManifest manifest)
	{
		var dependencyIds = new HashSet<string>(StringComparer.Ordinal);
		if (manifest.Dependencies is { } dependencies)
		{
			foreach (var dependency in dependencies)
			{
				if (ValidateRelationship(manifest.Id, dependency, "Dependency") is { } error)
				{
					return error;
				}

				if (!dependencyIds.Add(dependency.Id))
				{
					return PluginManifestReadResult.Fail(PluginManifestError.InvalidDependency,
						$"Dependency id '{dependency.Id}' is declared more than once.");
				}
			}
		}

		if (manifest.Conflicts is { } conflicts)
		{
			var conflictIds = new HashSet<string>(StringComparer.Ordinal);
			foreach (var conflict in conflicts)
			{
				if (ValidateRelationship(manifest.Id, conflict, "Conflict") is { } error)
				{
					return error;
				}

				if (!conflictIds.Add(conflict.Id))
				{
					return PluginManifestReadResult.Fail(PluginManifestError.InvalidDependency,
						$"Conflict id '{conflict.Id}' is declared more than once.");
				}

				if (dependencyIds.Contains(conflict.Id))
				{
					return PluginManifestReadResult.Fail(PluginManifestError.InvalidDependency,
						$"'{conflict.Id}' cannot be both a dependency and a conflict.");
				}
			}
		}

		return null;
	}

	private static PluginManifestReadResult? ValidateRelationship(string ownPluginId,
		PluginDependency relationship,
		string kind)
	{
		if (!PluginId.IsValid(relationship.Id))
		{
			return PluginManifestReadResult.Fail(PluginManifestError.InvalidDependency,
				$"{kind} id '{relationship.Id}' is not a valid plugin id.");
		}

		if (string.Equals(relationship.Id, ownPluginId, StringComparison.Ordinal))
		{
			return PluginManifestReadResult.Fail(PluginManifestError.InvalidDependency,
				$"{kind} '{relationship.Id}' cannot equal the manifest's own id.");
		}

		if (relationship.VersionRange is not null && !SemanticVersionRange.TryParse(relationship.VersionRange, out _))
		{
			return PluginManifestReadResult.Fail(PluginManifestError.InvalidDependency,
				$"{kind} '{relationship.Id}' has an invalid VersionRange.");
		}

		return null;
	}

	/// <summary>Icon pack ids are reverse-domain too. Resolution is not implemented; this only validates
	/// shape and rejects duplicate ids.</summary>
	private static PluginManifestReadResult? ValidateIconPacks(PluginManifest manifest)
	{
		if (manifest.IconPacks is not { } iconPacks)
		{
			return null;
		}

		var seenIds = new HashSet<string>(StringComparer.Ordinal);
		foreach (var iconPack in iconPacks)
		{
			if (!PluginId.IsValid(iconPack.Id))
			{
				return PluginManifestReadResult.Fail(PluginManifestError.InvalidDependency,
					$"Icon pack id '{iconPack.Id}' is not a valid plugin id.");
			}

			if (iconPack.VersionRange is not null && !SemanticVersionRange.TryParse(iconPack.VersionRange, out _))
			{
				return PluginManifestReadResult.Fail(PluginManifestError.InvalidDependency,
					$"Icon pack '{iconPack.Id}' has an invalid VersionRange.");
			}

			if (!seenIds.Add(iconPack.Id))
			{
				return PluginManifestReadResult.Fail(PluginManifestError.InvalidDependency,
					$"Icon pack id '{iconPack.Id}' is declared more than once.");
			}
		}

		return null;
	}

	/// <summary>An unknown permission string is not a rejection - forward compatibility matters more, and
	/// the installer reports unknown permissions as an advisory warning.</summary>
	private static PluginManifestReadResult? ValidatePermissions(PluginManifest manifest)
	{
		if (manifest.Permissions is not { } permissions)
		{
			return null;
		}

		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var permission in permissions)
		{
			if (string.IsNullOrWhiteSpace(permission))
			{
				return PluginManifestReadResult.Fail(PluginManifestError.InvalidPermission,
					"Permission entries must not be empty.");
			}

			if (!seen.Add(permission))
			{
				return PluginManifestReadResult.Fail(PluginManifestError.InvalidPermission,
					$"Permission '{permission}' is declared more than once.");
			}
		}

		return null;
	}

	/// <summary>An unrecognised language tag is not a rejection, for the same forward-compatibility reason
	/// an unknown permission is not: the tag is a declaration a store reads, and a manifest naming a
	/// language this build has never heard of must still install. Shape is what the published schema and
	/// the CLI check; the reader only refuses a tag that can carry no meaning at all.</summary>
	private static PluginManifestReadResult? ValidateLanguages(PluginManifest manifest)
	{
		if (manifest.Languages is not { } languages)
		{
			return null;
		}

		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var language in languages)
		{
			if (string.IsNullOrWhiteSpace(language))
			{
				return PluginManifestReadResult.Fail(PluginManifestError.InvalidLanguage,
					"Language entries must not be empty.");
			}

			if (!seen.Add(language))
			{
				return PluginManifestReadResult.Fail(PluginManifestError.InvalidLanguage,
					$"Language '{language}' is declared more than once.");
			}
		}

		return null;
	}

	private static PluginManifestReadResult? ValidateFiles(PluginManifest manifest)
	{
		if (manifest.Files is not { } files)
		{
			return null;
		}

		var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var file in files)
		{
			if (!IsSafeRelativeForwardSlashPath(file.Path))
			{
				return PluginManifestReadResult.Fail(PluginManifestError.InvalidFileDigest,
					$"File path '{file.Path}' is not a safe relative path.");
			}

			if (!AssetContentHash.IsValid(file.Sha256))
			{
				return PluginManifestReadResult.Fail(PluginManifestError.InvalidFileDigest,
					$"File '{file.Path}' has an invalid Sha256 digest.");
			}

			if (file.Size < 0)
			{
				return PluginManifestReadResult.Fail(PluginManifestError.InvalidFileDigest,
					$"File '{file.Path}' has a negative Size.");
			}

			if (!seenPaths.Add(file.Path))
			{
				return PluginManifestReadResult.Fail(PluginManifestError.InvalidFileDigest,
					$"File path '{file.Path}' is declared more than once.");
			}
		}

		return null;
	}

	/// <summary>Only the shape of a declared icon is checked - never its existence. This reader gates every
	/// supervisor launch, so an existence check here would let a stripped or quarantined icon file stop an
	/// otherwise-working plugin from running. It would also be inconsistent to enforce: the installer calls
	/// <see cref="ReadFromJson"/> with <c>versionDirectory: null</c>, before any version directory exists to
	/// check against.</summary>
	private static PluginManifestReadResult? ValidateIcon(PluginManifest manifest)
	{
		if (manifest.Icon is not { } icon)
		{
			return null;
		}

		if (!IsSafeRelativeForwardSlashPath(icon))
		{
			return PluginManifestReadResult.Fail(PluginManifestError.InvalidIcon,
				$"Icon path '{icon}' is not a safe relative path.");
		}

		return null;
	}

	/// <summary>Only the shape is validated here - the algorithm name and the decoded length are the
	/// signature verifier's job, so a scheme this reader does not know about is never rejected.</summary>
	private static PluginManifestReadResult? ValidateSignature(PluginManifest manifest)
	{
		if (manifest.Signature is not { } signature)
		{
			return null;
		}

		if (string.IsNullOrWhiteSpace(signature.Algorithm) ||
			string.IsNullOrWhiteSpace(signature.KeyId) ||
			string.IsNullOrWhiteSpace(signature.Value))
		{
			return PluginManifestReadResult.Fail(PluginManifestError.InvalidSignature,
				"Signature.Algorithm, Signature.KeyId and Signature.Value must not be empty.");
		}

		if (!IsValidBase64(signature.Value))
		{
			return PluginManifestReadResult.Fail(PluginManifestError.InvalidSignature,
				"Signature.Value is not valid base64.");
		}

		return null;
	}

	/// <summary>Settings are clamped rather than rejected - an out-of-range value is a foot-gun, not a
	/// reason to refuse the whole manifest.</summary>
	private static PluginManifest ClampSettings(PluginManifest manifest)
	{
		var shutdown = manifest.Shutdown is { } s
			? s with { GracefulTimeoutSeconds = Math.Clamp(s.GracefulTimeoutSeconds, 1, 60) }
			: null;

		var health = manifest.Health is { } h
			? h with
			{
				IntervalSeconds = Math.Clamp(h.IntervalSeconds, 5, 120),
				TimeoutSeconds = Math.Clamp(h.TimeoutSeconds, 1, 10),
				UnhealthyThreshold = Math.Clamp(h.UnhealthyThreshold, 2, 10)
			}
			: null;

		return manifest.Shutdown == shutdown && manifest.Health == health
			? manifest
			: manifest with { Shutdown = shutdown, Health = health };
	}

	/// <summary>Relative, no <c>..</c> segments, no absolute/rooted path. This is the safety rule alone;
	/// <see cref="TryResolveWithinVersionDirectory"/> layers actual resolution on top of it for the
	/// callers that have a real directory to resolve against.</summary>
	private static bool IsSafeRelativeExecutablePath(string executable)
	{
		if (string.IsNullOrWhiteSpace(executable) || Path.IsPathRooted(executable))
		{
			return false;
		}

		var segments = executable.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
		return !segments.Any(segment => segment == "..");
	}

	/// <summary>Forward-slash separated (a literal backslash is never legal here, unlike an executable
	/// path where either separator is tolerated), relative, no <c>..</c> segment, not rooted.</summary>
	private static bool IsSafeRelativeForwardSlashPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\'))
		{
			return false;
		}

		var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
		return segments.Length > 0 && !segments.Any(segment => segment == "..");
	}

	/// <summary>Relative, no <c>..</c> segments, no absolute/rooted path, resolves to a path that stays
	/// inside <paramref name="versionDirectory"/>.</summary>
	private static bool TryResolveWithinVersionDirectory(string versionDirectory,
		string executable,
		out string resolvedPath)
	{
		resolvedPath = string.Empty;

		if (!IsSafeRelativeExecutablePath(executable))
		{
			return false;
		}

		var normalizedRoot = Path.GetFullPath(versionDirectory);
		var candidate = Path.GetFullPath(Path.Combine(normalizedRoot, executable));

		var rootWithSeparator = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
			? normalizedRoot
			: normalizedRoot + Path.DirectorySeparatorChar;

		if (!candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal))
		{
			return false;
		}

		resolvedPath = candidate;
		return true;
	}

	private static bool HasForbiddenScriptExtension(string executable)
		=> _forbiddenScriptExtensions.Any(extension =>
			executable.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

	/// <summary><c>major.minor</c> where both parts are non-negative integers, e.g. <c>"10.0"</c>.</summary>
	private static bool IsValidDotnetMajorMinor(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		var parts = value.Split('.');
		return parts.Length == 2 &&
			int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out _) &&
			int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out _);
	}

	private static bool IsValidBase64(string value)
	{
		Span<byte> buffer = value.Length == 0 ? [] : new byte[value.Length];
		return Convert.TryFromBase64String(value, buffer, out _);
	}

	private static bool IsLegalPathSegment(string? segment)
	{
		if (string.IsNullOrWhiteSpace(segment) || segment is "." or "..")
		{
			return false;
		}

		return segment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
			!segment.Contains('/') &&
			!segment.Contains('\\');
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
