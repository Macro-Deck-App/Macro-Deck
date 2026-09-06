using System.Text.Json;
using MacroDeck.Plugin.Packaging.Versioning;
using MacroDeckHost.Application.Store.Model;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Store;

public sealed class StoreRegistryReader
{
	private const string IndexFileName = "index.json";
	private const string SecurityFileName = "security.json";
	private const string PackageManifestFileName = "manifest.json";

	private readonly ILogger _logger;

	public StoreRegistryReader(ILogger logger)
	{
		_logger = logger.ForContext<StoreRegistryReader>();
	}

	public StoreCatalogSnapshot? Read(string registryRoot,
		long sequence,
		DateTimeOffset? generatedAt,
		DateTimeOffset? signedAt,
		DateTimeOffset fetchedAt)
	{
		var index = ReadDocument<RegistryIndexDocument>(Path.Combine(registryRoot, IndexFileName));
		if (index is null)
		{
			return null;
		}

		// Only manifest-declared files are ever fetched, so a security.json the manifest omits would
		// silently read as "nothing revoked, nothing removed" instead of failing closed.
		var security = ReadDocument<RegistrySecurityDocument>(Path.Combine(registryRoot, SecurityFileName));
		if (security is null)
		{
			_logger.Warning("The signed store registry does not describe {File}.", SecurityFileName);
			return null;
		}

		var entries = new List<StoreCatalogEntry>();
		if (!Collect(registryRoot, "plugins", index.Plugins, StoreExtensionKind.Plugin, entries) ||
			!Collect(registryRoot, "icon-packs", index.IconPacks, StoreExtensionKind.IconPack, entries) ||
			!Collect(registryRoot,
				"templates/profiles",
				index.Templates.Profiles,
				StoreExtensionKind.ProfileTemplate,
				entries))
		{
			return null;
		}

		return new StoreCatalogSnapshot
		{
			Sequence = sequence,
			GeneratedAt = generatedAt,
			SignedAt = signedAt,
			FetchedAt = fetchedAt,
			Entries = entries,
			RemovedPackages = security.RemovedPackages
				.Where(package => !string.IsNullOrWhiteSpace(package.Id))
				.Select(package => new StoreRemovedPackage
				{
					Id = package.Id,
					Version = package.Version,
					Reason = package.Reason,
					Replacement = package.Replacement
				})
				.ToList(),
			RevokedKeyIds = security.RevokedKeys
				.Select(key => key.KeyId)
				.Where(keyId => !string.IsNullOrWhiteSpace(keyId))
				.ToList(),
			Featured = ReadFeatured(index.Featured)
		};
	}

	public IReadOnlyList<string> ReadRevokedKeyIds(string registryRoot)
	{
		var security = ReadDocument<RegistrySecurityDocument>(Path.Combine(registryRoot, SecurityFileName));
		return security is null
			? []
			: security.RevokedKeys.Select(key => key.KeyId).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
	}

	// Unlike Collect, an entry naming a package the verified tree does not describe is dropped rather
	// than failing the refresh: a curated pick omits nothing from the catalog, and letting an editorial
	// typo refuse the whole snapshot would also stop removed-package and key-revocation propagation.
	// Resolution happens at query time, so a pick that is later removed stops being featured by itself.
	private static List<StoreFeaturedRef> ReadFeatured(List<RegistryFeaturedEntry> featured)
	{
		var refs = new List<StoreFeaturedRef>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var entry in featured)
		{
			if (string.IsNullOrWhiteSpace(entry.Id) || !TryParseKind(entry.Kind, out var kind))
			{
				continue;
			}

			if (seen.Add($"{kind}:{entry.Id}"))
			{
				refs.Add(new StoreFeaturedRef { Kind = kind, Id = entry.Id });
			}
		}

		return refs;
	}

	// The registry names kinds the way its own directories and package manifests do; the enum spelling
	// is accepted too so a pipeline cannot silently lose a pick to a plausible synonym.
	private static bool TryParseKind(string kind, out StoreExtensionKind parsed)
	{
		switch (kind.Trim().ToLowerInvariant())
		{
			case "plugin":
				parsed = StoreExtensionKind.Plugin;
				return true;
			case "icon-pack":
			case "iconpack":
				parsed = StoreExtensionKind.IconPack;
				return true;
			case "profile":
			case "profiletemplate":
				parsed = StoreExtensionKind.ProfileTemplate;
				return true;
			default:
				parsed = default;
				return false;
		}
	}

	public static string DirectorySegment(StoreExtensionKind kind) => kind switch
	{
		StoreExtensionKind.Plugin => "plugins",
		StoreExtensionKind.IconPack => "icon-packs",
		StoreExtensionKind.ProfileTemplate => "templates/profiles",
		_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
	};

	// An index entry the verified tree does not describe means the signed snapshot is internally
	// inconsistent, so the whole refresh is refused rather than silently serving a partial catalog.
	private bool Collect(string registryRoot,
		string segment,
		IReadOnlyList<string> ids,
		StoreExtensionKind kind,
		List<StoreCatalogEntry> entries)
	{
		foreach (var id in ids)
		{
			if (string.IsNullOrWhiteSpace(id) || id.Contains('/') || id.Contains('\\') || id.Contains(".."))
			{
				_logger.Warning("Store registry lists an unusable package id {PackageId}.", id);
				return false;
			}

			var packageDirectory = Path.Combine(registryRoot, Path.Combine(segment.Split('/')), id);
			var package = ReadDocument<RegistryPackageDocument>(Path.Combine(packageDirectory,
				PackageManifestFileName));
			if (package is null || string.IsNullOrWhiteSpace(package.LatestVersion))
			{
				_logger.Warning("Store registry is missing the manifest for {PackageId}.", id);
				return false;
			}

			var versionDirectory = Path.Combine(packageDirectory, "versions", package.LatestVersion);
			var release = ReadDocument<RegistryReleaseDocument>(Path.Combine(versionDirectory,
				PackageManifestFileName));
			if (release is null ||
				!Uri.TryCreate(release.Url, UriKind.Absolute, out var artifactUrl) ||
				!StoreHttp.IsHttps(artifactUrl) ||
				string.IsNullOrWhiteSpace(release.Sha256))
			{
				_logger.Warning("Store registry has no usable release for {PackageId}.", id);
				return false;
			}

			entries.Add(new StoreCatalogEntry
			{
				Kind = kind,
				Id = id,
				Name = string.IsNullOrWhiteSpace(package.Name) ? id : package.Name,
				LatestVersion = package.LatestVersion,
				Description = package.Description,
				Publisher = package.Publisher,
				Repository = package.Repository,
				License = package.License,
				CreatedAt = package.CreatedAt,
				UpdatedAt = package.UpdatedAt,
				SupportedRids = package.SupportedRids ?? [],
				Languages = package.Languages ?? [],
				Changelog = ReadText(Path.Combine(versionDirectory, "changelog.md")),
				LongDescription = ReadText(Path.Combine(versionDirectory, "description.md")),
				History = ReadHistory(Path.Combine(packageDirectory, "versions")),
				LatestRelease = new StoreReleaseManifest
				{
					Version = package.LatestVersion,
					ArtifactUrl = artifactUrl,
					Sha256 = release.Sha256,
					Size = release.Size,
					UploadedAt = release.UploadedAt,
					Icon = ToAsset(release.Media?.Icon),
					Screenshots = release.Media?.Screenshots
							.Select(ToAsset)
							.Where(asset => asset is not null)
							.Select(asset => asset!)
							.ToList() ??
						[]
				}
			});
		}

		return true;
	}

	// Every published version of a package lives in the signed tree, so the release notes for older
	// versions are already local: no extra fetch, and nothing here is trusted that the manifest did not
	// cover. Newest first, and a version whose directory carries no notes still appears.
	private List<StoreVersionHistoryEntry> ReadHistory(string versionsDirectory)
	{
		if (!Directory.Exists(versionsDirectory))
		{
			return [];
		}

		var entries = new List<StoreVersionHistoryEntry>();
		foreach (var directory in Directory.EnumerateDirectories(versionsDirectory))
		{
			var version = Path.GetFileName(directory);
			if (string.IsNullOrWhiteSpace(version))
			{
				continue;
			}

			var release = ReadDocument<RegistryReleaseDocument>(Path.Combine(directory, PackageManifestFileName));

			entries.Add(new StoreVersionHistoryEntry
			{
				Version = version,
				ReleasedAt = release?.UploadedAt,
				Changelog = ReadText(Path.Combine(directory, "changelog.md"))
			});
		}

		return entries
			.OrderByDescending(entry => SemanticVersion.TryParse(entry.Version, out var parsed) ? parsed : null)
			.ThenByDescending(entry => entry.Version, StringComparer.Ordinal)
			.ToList();
	}

	private static StoreMediaAsset? ToAsset(RegistryMediaAsset? asset)
	{
		if (asset is null ||
			!Uri.TryCreate(asset.Url, UriKind.Absolute, out var url) ||
			!StoreHttp.IsHttps(url) ||
			string.IsNullOrWhiteSpace(asset.Sha256))
		{
			return null;
		}

		return new StoreMediaAsset
		{
			Url = url,
			Sha256 = asset.Sha256,
			Size = asset.Size,
			ContentType = asset.ContentType,
			Caption = asset.Caption
		};
	}

	private T? ReadDocument<T>(string path)
		where T : class
	{
		try
		{
			if (!File.Exists(path))
			{
				return null;
			}

			return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), StoreRegistryJson.Options);
		}
		catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
		{
			_logger.Warning(ex, "Store registry document {Path} could not be read.", path);
			return null;
		}
	}

	private static string? ReadText(string path)
	{
		try
		{
			return File.Exists(path) ? File.ReadAllText(path) : null;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}
}
