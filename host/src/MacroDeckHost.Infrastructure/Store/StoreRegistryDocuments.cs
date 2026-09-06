namespace MacroDeckHost.Infrastructure.Store;

// Wire shapes of the registry's public files. Everything read through these is untrusted until
// RegistryManifestVerifier has confirmed the signature and every declared digest.
internal sealed record RegistryManifestDocument
{
	public int SchemaVersion { get; init; }

	public long Sequence { get; init; }

	public DateTimeOffset? GeneratedAt { get; init; }

	public List<RegistryManifestFile> Files { get; init; } = [];
}

internal sealed record RegistryManifestFile
{
	public string Path { get; init; } = string.Empty;

	public string Sha256 { get; init; } = string.Empty;

	public long Size { get; init; }
}

internal sealed record RegistrySignatureFile
{
	public int SchemaVersion { get; init; }

	public string Algorithm { get; init; } = string.Empty;

	public string KeyId { get; init; } = string.Empty;

	public string Value { get; init; } = string.Empty;

	public DateTimeOffset SignedAt { get; init; }
}

internal sealed record RegistryIndexDocument
{
	public List<string> Plugins { get; init; } = [];

	public List<string> IconPacks { get; init; } = [];

	public RegistryTemplateIndex Templates { get; init; } = new();

	public List<RegistryFeaturedEntry> Featured { get; init; } = [];
}

internal sealed record RegistryFeaturedEntry
{
	public string Kind { get; init; } = string.Empty;

	public string Id { get; init; } = string.Empty;
}

internal sealed record RegistryTemplateIndex
{
	public List<string> Profiles { get; init; } = [];

	public List<string> Folders { get; init; } = [];

	public List<string> Widgets { get; init; } = [];

	public List<string> Automations { get; init; } = [];
}

internal sealed record RegistrySecurityDocument
{
	public List<RegistryRevokedKey> RevokedKeys { get; init; } = [];

	public List<RegistryRemovedPackage> RemovedPackages { get; init; } = [];
}

internal sealed record RegistryRevokedKey
{
	public string KeyId { get; init; } = string.Empty;

	public string? Reason { get; init; }

	public DateTimeOffset? RevokedAt { get; init; }
}

internal sealed record RegistryRemovedPackage
{
	public string Id { get; init; } = string.Empty;

	public string? Version { get; init; }

	public string? Reason { get; init; }

	public string? Replacement { get; init; }
}

internal sealed record RegistryPackageDocument
{
	public string Kind { get; init; } = string.Empty;

	public string Id { get; init; } = string.Empty;

	public string Name { get; init; } = string.Empty;

	public string LatestVersion { get; init; } = string.Empty;

	public string? Description { get; init; }

	public string? Publisher { get; init; }

	public string? Repository { get; init; }

	public string? License { get; init; }

	public DateTimeOffset? CreatedAt { get; init; }

	public DateTimeOffset? UpdatedAt { get; init; }

	public List<string>? SupportedRids { get; init; }

	public List<string>? Languages { get; init; }
}

internal sealed record RegistryReleaseDocument
{
	public string Url { get; init; } = string.Empty;

	public string Sha256 { get; init; } = string.Empty;

	public long Size { get; init; }

	public DateTimeOffset? UploadedAt { get; init; }

	public RegistryReleaseMedia? Media { get; init; }
}

internal sealed record RegistryReleaseMedia
{
	public RegistryMediaAsset? Icon { get; init; }

	public List<RegistryMediaAsset> Screenshots { get; init; } = [];
}

internal sealed record RegistryMediaAsset
{
	public string Url { get; init; } = string.Empty;

	public string Sha256 { get; init; } = string.Empty;

	public long Size { get; init; }

	public string? ContentType { get; init; }

	public string? Caption { get; init; }
}
