using MacroDeck.Plugin.Cli.Manifests;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.Commands;

internal sealed record InspectedEntrypoint
{
	public required string Rid { get; init; }

	public required string Executable { get; init; }

	public required IReadOnlyList<string> Arguments { get; init; }

	public required string RuntimeKind { get; init; }

	public string? DotnetVersion { get; init; }

	/// <summary><c>null</c> means presence was not checked - a version directory or artifact scan that
	/// never ran - so the report never claims to know something it does not. <c>true</c>/<c>false</c> only
	/// after <see cref="EntrypointPresence" /> actually compared this entrypoint's executable against a
	/// real entry-name set.</summary>
	public bool? Present { get; init; }
}

internal sealed record InspectedPermission
{
	public required string Name { get; init; }

	public required bool Known { get; init; }
}

internal sealed record InspectedRelationship
{
	public required string Id { get; init; }

	public string? VersionRange { get; init; }

	public required bool Optional { get; init; }
}

internal sealed record InspectedCompatibility
{
	public string? Sdk { get; init; }

	public string? MacroDeck { get; init; }

	public int? ProtocolMinimum { get; init; }

	public int? ProtocolMaximum { get; init; }
}

/// <summary>
/// A descriptive read of a manifest's <c>signature</c> block - never a verdict about whether it actually
/// verifies. <see cref="ShapeDescription" /> mirrors the shape rules the host's signature verification
/// applies (unrecognised algorithm is unverifiable, an <c>ed25519</c> value must decode to exactly 64
/// bytes), not a reimplementation of that verification - the CLI has no access to it, being SDK-only, and
/// every shape this type reports on already passed <see cref="IPluginManifestReader" />'s own shape
/// validation (non-empty fields, valid base64) before a <see cref="PluginManifest" /> could exist to build
/// one from.
/// </summary>
internal sealed record InspectedSignature
{
	public required string Algorithm { get; init; }

	public required string KeyId { get; init; }

	public required int ValueByteLength { get; init; }

	public DateTimeOffset? SignedAt { get; init; }

	public required string ShapeDescription { get; init; }

	public static InspectedSignature From(PluginSignature signature)
	{
		var valueBytes = Convert.FromBase64String(signature.Value).Length;
		var isEd25519 = string.Equals(signature.Algorithm, "ed25519", StringComparison.OrdinalIgnoreCase);

		var shape = !isEd25519
			? "unverifiable (unrecognized algorithm)"
			: valueBytes == 64
				? "well-formed ed25519 (not cryptographically verified)"
				: "invalid ed25519 length";

		return new InspectedSignature
		{
			Algorithm = signature.Algorithm,
			KeyId = signature.KeyId,
			ValueByteLength = valueBytes,
			SignedAt = signature.SignedAt,
			ShapeDescription = shape
		};
	}
}

/// <summary>Everything <c>inspect</c> reports about one manifest, plus the raw <see cref="Manifest" />
/// itself so <c>--show-digest</c> can compute
/// <c>MacroDeck.Plugin.Packaging.Artifacts.PluginArtifactDigest.Compute</c> from it.</summary>
internal sealed record PluginInspectionReport
{
	public required string PluginId { get; init; }

	public required string Name { get; init; }

	public required string Version { get; init; }

	public string? Description { get; init; }

	public required IReadOnlyList<InspectedEntrypoint> Entrypoints { get; init; }

	public required IReadOnlyList<InspectedPermission> Permissions { get; init; }

	/// <summary>The BCP-47 tags the manifest declares, in the order it declares them. What the plugin
	/// actually serves once it is running comes from its localization capability, not from here.</summary>
	public required IReadOnlyList<string> Languages { get; init; }

	public required IReadOnlyList<InspectedRelationship> Dependencies { get; init; }

	public required IReadOnlyList<InspectedRelationship> Conflicts { get; init; }

	public required IReadOnlyList<InspectedRelationship> IconPacks { get; init; }

	public InspectedCompatibility? Compatibility { get; init; }

	public InspectedSignature? Signature { get; init; }

	public required int EntryCount { get; init; }

	public required long TotalUncompressedBytes { get; init; }

	/// <summary>Null for a version directory, which has no single archive size.</summary>
	public long? ArchiveBytes { get; init; }

	public double? CompressionRatio { get; init; }

	public required PluginManifest Manifest { get; init; }

	/// <summary>Non-fatal observations - today, only missing entrypoints. Inspect is read-only reporting,
	/// never a second validation pass, so these describe rather than judge and never affect the exit
	/// code.</summary>
	public IReadOnlyList<CliDiagnostic> Warnings { get; init; } = [];

	public static PluginInspectionReport From(PluginManifest manifest,
		int entryCount,
		long totalUncompressedBytes,
		long? archiveBytes)
		=> From(manifest, entryCount, totalUncompressedBytes, archiveBytes, presentEntryNames: null);

	/// <param name="presentEntryNames">The artifact's or directory's own entry names, already normalized
	/// and <see cref="StringComparer.OrdinalIgnoreCase" />. <c>null</c> means "not checked" - every
	/// <see cref="InspectedEntrypoint.Present" /> stays <c>null</c> and no warnings are produced, matching
	/// <see cref="Manifests.ArtifactEntryNames.ReadAsync" />'s "empty means could not check" contract.</param>
	/// <param name="subjectNoun">What <paramref name="presentEntryNames" /> represents in a warning message
	/// - <c>"artifact"</c> for <c>--artifact</c>, <c>"directory"</c> for <c>--directory</c>. Unused when
	/// <paramref name="presentEntryNames" /> is <c>null</c>.</param>
	public static PluginInspectionReport From(PluginManifest manifest,
		int entryCount,
		long totalUncompressedBytes,
		long? archiveBytes,
		IReadOnlySet<string>? presentEntryNames,
		string subjectNoun = "artifact")
	{
		var entrypoints = manifest.Entrypoints
			.Select(pair => new InspectedEntrypoint
			{
				Rid = pair.Key,
				Executable = pair.Value.Executable,
				Arguments = pair.Value.Arguments ?? [],
				RuntimeKind = (pair.Value.Runtime?.Kind ?? PluginEntrypointRuntimeKind.SelfContained).ToString(),
				DotnetVersion = pair.Value.Runtime?.DotnetVersion,
				Present = presentEntryNames is null
					? null
					: presentEntryNames.Contains(EntrypointPresence.Normalize(pair.Value.Executable))
			})
			.OrderBy(entrypoint => entrypoint.Rid, StringComparer.Ordinal)
			.ToList();

		var warnings = presentEntryNames is null
			? []
			: EntrypointPresence.Missing(manifest, presentEntryNames, subjectNoun);

		var permissions = (manifest.Permissions ?? [])
			.Select(permission => new InspectedPermission
				{ Name = permission, Known = PluginPermissions.IsKnown(permission) })
			.ToList();

		var dependencies = (manifest.Dependencies ?? []).Select(ToRelationship).ToList();
		var conflicts = (manifest.Conflicts ?? []).Select(ToRelationship).ToList();
		var iconPacks = (manifest.IconPacks ?? [])
			.Select(iconPack => new InspectedRelationship
				{ Id = iconPack.Id, VersionRange = iconPack.VersionRange, Optional = iconPack.Optional })
			.ToList();

		var compatibility = manifest.Compatibility is { } declared
			? new InspectedCompatibility
			{
				Sdk = declared.Sdk,
				MacroDeck = declared.MacroDeck,
				ProtocolMinimum = declared.Protocol?.Minimum,
				ProtocolMaximum = declared.Protocol?.Maximum
			}
			: null;

		var signature = manifest.Signature is { } declaredSignature ? InspectedSignature.From(declaredSignature) : null;

		double? ratio = archiveBytes is { } bytes and > 0 ? (double)totalUncompressedBytes / bytes : null;

		return new PluginInspectionReport
		{
			PluginId = manifest.Id,
			Name = manifest.Name,
			Version = manifest.Version,
			Description = manifest.Description,
			Entrypoints = entrypoints,
			Permissions = permissions,
			Languages = manifest.Languages ?? [],
			Dependencies = dependencies,
			Conflicts = conflicts,
			IconPacks = iconPacks,
			Compatibility = compatibility,
			Signature = signature,
			EntryCount = entryCount,
			TotalUncompressedBytes = totalUncompressedBytes,
			ArchiveBytes = archiveBytes,
			CompressionRatio = ratio,
			Manifest = manifest,
			Warnings = warnings
		};
	}

	private static InspectedRelationship ToRelationship(PluginDependency dependency)
		=> new() { Id = dependency.Id, VersionRange = dependency.VersionRange, Optional = dependency.Optional };
}
