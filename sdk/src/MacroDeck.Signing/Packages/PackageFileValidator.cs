using System.Security.Cryptography;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeck.Signing.Packages;

/// <summary>
/// The declared-files check shared by every <see cref="SignablePackageFormat"/>, run before signing (so a
/// broken archive is never signed) and after verifying a signature (so a tampered archive never passes):
/// every <c>files</c> entry exists with the declared size and SHA-256, and the package contains no entry
/// beyond those and the root manifest, <c>certificate.json</c> and <c>certificate.sig</c>.
/// </summary>
internal static class PackageFileValidator
{
	public static async Task<SigningFailure?> ValidateAsync(IPackageEntrySource source,
		JsonObject manifest,
		string manifestEntryName,
		CancellationToken cancellationToken)
	{
		if (!DeclaredPackageFiles.TryParse(manifest, out var declared, out var parseError))
		{
			return new SigningFailure(SigningError.ManifestMalformed, parseError!);
		}

		// A throwaway rooted path so PluginArtifactEntryPolicy.Resolve runs the identical safety check the
		// installer's extractor uses; nothing is written here.
		var notionalRoot = Path.Combine(Path.GetTempPath(), "macrodeck-signing");
		var archiveEntryNames = new HashSet<string>(StringComparer.Ordinal);

		foreach (var entry in source.Entries)
		{
			var decision = PluginArtifactEntryPolicy.Resolve(entry.Name, notionalRoot);
			if (!decision.Allowed)
			{
				return new SigningFailure(SigningError.UnsafeEntry, decision.Message!);
			}

			if (!entry.IsRegularFile)
			{
				return new SigningFailure(SigningError.UnsafeEntry,
					$"The package entry '{entry.Name}' is not a regular file.");
			}

			if (!archiveEntryNames.Add(entry.Name))
			{
				return new SigningFailure(SigningError.UnsafeEntry,
					$"The package contains a duplicate entry '{entry.Name}'.");
			}
		}

		var declaredPaths = new HashSet<string>(StringComparer.Ordinal);
		foreach (var file in declared)
		{
			if (!declaredPaths.Add(file.Path))
			{
				return new SigningFailure(SigningError.ManifestMalformed,
					$"'{file.Path}' is declared more than once.");
			}

			var entry = source.Find(file.Path);
			if (entry is null)
			{
				return new SigningFailure(SigningError.DeclaredFileMissing,
					$"'{file.Path}' is declared in 'files' but missing from the package.");
			}

			if (entry.Length != file.Size)
			{
				return new SigningFailure(SigningError.FileSizeMismatch,
					$"'{file.Path}' is {entry.Length} bytes; the manifest declares {file.Size}.");
			}

			var actualSha256 = await ComputeEntrySha256Async(source, entry, cancellationToken);
			var declaredSha256 = DeclaredPackageFiles.StripSha256Prefix(file.Sha256);
			if (!string.Equals(actualSha256, declaredSha256, StringComparison.OrdinalIgnoreCase))
			{
				return new SigningFailure(SigningError.FileDigestMismatch,
					$"'{file.Path}' does not match its declared digest.");
			}
		}

		foreach (var name in archiveEntryNames)
		{
			if (IsRootSignatureMaterial(name, manifestEntryName) || declaredPaths.Contains(name))
			{
				continue;
			}

			return new SigningFailure(SigningError.UndeclaredFile,
				$"'{name}' is present in the package but not declared in 'files'.");
		}

		return null;
	}

	private static bool IsRootSignatureMaterial(string entryFullName, string manifestEntryName) =>
		entryFullName == manifestEntryName ||
		entryFullName is PluginArtifactFiles.CertificateFileName or PluginArtifactFiles.CertificateSignatureFileName;

	private static async Task<string> ComputeEntrySha256Async(IPackageEntrySource source,
		PackageEntry entry,
		CancellationToken cancellationToken)
	{
		await using var stream = await source.OpenAsync(entry, cancellationToken);
		var hash = await SHA256.HashDataAsync(stream, cancellationToken);
		return Convert.ToHexStringLower(hash);
	}
}
