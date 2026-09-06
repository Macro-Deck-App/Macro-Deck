using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.Keys;

namespace MacroDeck.Signing.Registry;

/// <summary>
/// Verifies a Macro Deck Store Registry manifest: that <c>registry-signature.json</c> is a valid signature
/// over the exact bytes of <c>registry-manifest.json</c>, made by a <c>registry</c>-usage certificate, and
/// that every file the manifest declares exists on disk with the declared size and SHA-256. This library
/// never signs a registry manifest or issues certificates - both happen offline, outside Macro Deck.
/// </summary>
public static class RegistryManifestVerifier
{
	private const string RegistryManifestFileName = "registry-manifest.json";

	private const string RegistrySignatureFileName = "registry-signature.json";

	/// <summary>Verifies <paramref name="signaturePath"/> against <paramref name="manifestPath"/>'s exact
	/// bytes, and every file <paramref name="manifestPath"/> declares against the files on disk beside it.
	/// <paramref name="rootPublicKey"/> defaults to <see cref="MacroDeckRootKey"/> when omitted.</summary>
	public static async Task<RegistryManifestVerifyResult> VerifyAsync(string manifestPath,
		string signaturePath,
		byte[] certificateBytes,
		byte[] certificateSignatureBytes,
		ReadOnlyMemory<byte>? rootPublicKey = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(signaturePath);
		ArgumentNullException.ThrowIfNull(certificateBytes);
		ArgumentNullException.ThrowIfNull(certificateSignatureBytes);

		byte[] manifestBytes;
		try
		{
			manifestBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return RegistryManifestVerifyResult.Fail(SigningError.PackageUnreadable, ex.Message);
		}

		if (ValidateManifestFiles(manifestBytes, manifestPath) is { } manifestFailure)
		{
			return RegistryManifestVerifyResult.Fail(manifestFailure.Error, manifestFailure.Message);
		}

		byte[] signatureDocumentBytes;
		try
		{
			signatureDocumentBytes = await File.ReadAllBytesAsync(signaturePath, cancellationToken);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return RegistryManifestVerifyResult.Fail(SigningError.SignatureMissing, ex.Message);
		}

		RegistrySignatureDocument? document;
		try
		{
			document = JsonSerializer.Deserialize<RegistrySignatureDocument>(signatureDocumentBytes,
				SigningJson.Options);
		}
		catch (JsonException ex)
		{
			return RegistryManifestVerifyResult.Fail(SigningError.SignatureMalformed, ex.Message);
		}

		if (document is null || document.SchemaVersion != 1)
		{
			return RegistryManifestVerifyResult.Fail(SigningError.SignatureMalformed,
				"The signature document has an unsupported shape.");
		}

		if (!string.Equals(document.Algorithm, "ed25519", StringComparison.OrdinalIgnoreCase))
		{
			return RegistryManifestVerifyResult.Fail(SigningError.SignatureAlgorithmUnsupported,
				$"The signature uses '{document.Algorithm}', which this library cannot verify.");
		}

		var signatureBytes = Base64Material.TryDecode(document.Value, Ed25519KeyPair.SignatureLength);
		if (signatureBytes is null)
		{
			return RegistryManifestVerifyResult.Fail(SigningError.SignatureMalformed,
				"The signature value is not a valid base64-encoded Ed25519 signature.");
		}

		var chainResult = SigningCertificateChain.Verify(certificateBytes,
			certificateSignatureBytes,
			rootPublicKey.HasValue ? rootPublicKey.Value.Span : MacroDeckRootKey.PublicKey,
			SigningCertificateChain.RegistryKeyUsage);
		if (!chainResult.Success)
		{
			return RegistryManifestVerifyResult.Fail(chainResult.Error!.Value, chainResult.Message!);
		}

		var trusted = chainResult.TrustedCertificate!;

		if (!string.Equals(document.KeyId, trusted.Certificate.CertificateId, StringComparison.Ordinal))
		{
			return RegistryManifestVerifyResult.Fail(SigningError.SignatureKeyIdMismatch,
				"The signature's keyId does not match the supplied certificate.");
		}

		if (SigningCertificateChain.EnsureValidAt(trusted.Certificate, document.SignedAt) is { } validityFailure)
		{
			return RegistryManifestVerifyResult.Fail(validityFailure.Error, validityFailure.Message);
		}

		if (!trusted.VerifySignature(manifestBytes, signatureBytes))
		{
			return RegistryManifestVerifyResult.Fail(SigningError.SignatureInvalid,
				"The registry manifest signature is not valid.");
		}

		return RegistryManifestVerifyResult.Ok(trusted.Certificate.CertificateId);
	}

	/// <summary>Validates the manifest's own shape and every file it declares: safe relative paths, no
	/// duplicates, ordinally sorted, present on disk with the declared size and SHA-256, and reached
	/// without crossing a symbolic link.</summary>
	private static SigningFailure? ValidateManifestFiles(byte[] manifestBytes, string manifestPath)
	{
		JsonObject manifest;
		try
		{
			manifest = JsonNode.Parse(manifestBytes) as JsonObject ??
				throw new JsonException("The registry manifest must contain a JSON object.");
		}
		catch (JsonException ex)
		{
			return new SigningFailure(SigningError.ManifestMalformed, ex.Message);
		}

		if (manifest["schemaVersion"]?.GetValue<int>() != 1 ||
			manifest["sequence"] is not { } sequenceNode ||
			sequenceNode.GetValue<long>() < 1 ||
			manifest["files"] is not JsonArray files ||
			files.Count == 0)
		{
			return new SigningFailure(SigningError.ManifestMalformed,
				"The registry manifest has an unsupported or incomplete shape.");
		}

		var registryRoot = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
		var previousPath = string.Empty;
		var seen = new HashSet<string>(StringComparer.Ordinal);

		foreach (var node in files)
		{
			if (node is not JsonObject entry)
			{
				return new SigningFailure(SigningError.ManifestMalformed,
					"Every registry files entry must be an object.");
			}

			if (entry["path"]?.GetValue<string>() is not { Length: > 0 } relativePath)
			{
				return new SigningFailure(SigningError.ManifestMalformed, "A registry files entry has no 'path'.");
			}

			if (!IsSafeRegistryPath(relativePath, registryRoot))
			{
				return new SigningFailure(SigningError.UnsafeEntry, $"Unsafe registry path '{relativePath}'.");
			}

			if (!seen.Add(relativePath))
			{
				return new SigningFailure(SigningError.ManifestMalformed, $"Duplicate registry path '{relativePath}'.");
			}

			if (previousPath.Length > 0 && string.CompareOrdinal(previousPath, relativePath) >= 0)
			{
				return new SigningFailure(SigningError.ManifestMalformed,
					"Registry manifest paths must be sorted ordinally.");
			}

			previousPath = relativePath;

			if (EnsurePathHasNoSymbolicLinks(registryRoot, relativePath) is { } symlinkFailure)
			{
				return symlinkFailure;
			}

			var filePath = Path.GetFullPath(Path.Combine(registryRoot, relativePath));
			if (!File.Exists(filePath))
			{
				return new SigningFailure(SigningError.DeclaredFileMissing,
					$"Registry file '{relativePath}' does not exist.");
			}

			var info = new FileInfo(filePath);
			if (entry["size"] is not { } sizeNode)
			{
				return new SigningFailure(SigningError.ManifestMalformed,
					$"Registry file '{relativePath}' has no 'size'.");
			}

			if (info.Length != sizeNode.GetValue<long>())
			{
				return new SigningFailure(SigningError.FileSizeMismatch,
					$"Size mismatch for registry file '{relativePath}'.");
			}

			if (entry["sha256"]?.GetValue<string>() is not { Length: > 0 } expectedDigest)
			{
				return new SigningFailure(SigningError.ManifestMalformed,
					$"Registry file '{relativePath}' has no 'sha256'.");
			}

			using var content = info.OpenRead();
			var actualDigest = Convert.ToHexStringLower(SHA256.HashData(content));
			if (!string.Equals(expectedDigest, actualDigest, StringComparison.Ordinal))
			{
				return new SigningFailure(SigningError.FileDigestMismatch,
					$"SHA-256 mismatch for registry file '{relativePath}'.");
			}
		}

		return null;
	}

	private static bool IsSafeRegistryPath(string path, string registryRoot)
	{
		if (path is RegistryManifestFileName or RegistrySignatureFileName)
		{
			return false;
		}

		return PluginArtifactEntryPolicy.Resolve(path, registryRoot).Allowed;
	}

	/// <summary>Walks every path segment on disk, refusing one that crosses a symbolic link - the same
	/// guard <see cref="PluginArtifactEntryPolicy"/> cannot provide, since it judges a zip entry's name,
	/// never a real filesystem path.</summary>
	private static SigningFailure? EnsurePathHasNoSymbolicLinks(string registryRoot, string relativePath)
	{
		var current = registryRoot;
		foreach (var segment in relativePath.Split('/'))
		{
			current = Path.Combine(current, segment);
			FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
			if (info.LinkTarget is not null)
			{
				return new SigningFailure(SigningError.UnsafeEntry,
					$"Registry path '{relativePath}' traverses symbolic link '{current}'.");
			}
		}

		return null;
	}
}

/// <summary>The outcome of <see cref="RegistryManifestVerifier.VerifyAsync"/>.</summary>
public sealed record RegistryManifestVerifyResult
{
	public required bool Success { get; init; }

	public string? CertificateId { get; init; }

	public SigningError? Error { get; init; }

	public string? Message { get; init; }

	public static RegistryManifestVerifyResult Ok(string certificateId) =>
		new() { Success = true, CertificateId = certificateId };

	public static RegistryManifestVerifyResult Fail(SigningError error, string message) =>
		new() { Success = false, Error = error, Message = message };
}
