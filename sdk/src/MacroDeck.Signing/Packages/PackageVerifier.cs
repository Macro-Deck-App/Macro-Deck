using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.Keys;

namespace MacroDeck.Signing.Packages;

/// <summary>
/// Verifies a signed archive of any <see cref="SignablePackageFormat"/>. Every check is over the archive's
/// exact bytes; nothing here consults revocation. The certificate - always read from the archive's own
/// <c>certificate.json</c> / <c>certificate.sig</c>, since a signed package always verifies on its own -
/// has its validity window evaluated at the embedded signature's <c>signedAt</c>, not at the instant
/// verification runs.
/// </summary>
public static class PackageVerifier
{
	/// <summary>Verifies <paramref name="packagePath"/>. <paramref name="rootPublicKey"/> defaults to
	/// <see cref="MacroDeckRootKey"/> when omitted. <paramref name="manifestReader"/> is only consulted for
	/// a <see cref="SignablePackageFormat.Plugin"/> archive.</summary>
	public static async Task<PackageVerifyResult> VerifyAsync(string packagePath,
		IPluginManifestReader manifestReader,
		ReadOnlyMemory<byte>? rootPublicKey = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
		ArgumentNullException.ThrowIfNull(manifestReader);

		if (SignablePackageFormats.Resolve(packagePath) is not { } format)
		{
			return PackageVerifyResult.Fail(SigningError.PackageFormatUnsupported,
				$"'{Path.GetExtension(packagePath)}' is not a signable package format.");
		}

		ZipPackageEntrySource source;
		try
		{
			source = await ZipPackageEntrySource.OpenAsync(packagePath, cancellationToken);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
		{
			return PackageVerifyResult.Fail(SigningError.PackageUnreadable, ex.Message);
		}

		await using (source)
		{
			return await VerifyCoreAsync(source, format, manifestReader, rootPublicKey, cancellationToken);
		}
	}

	/// <summary>Verifies an already-extracted package tree rooted at <paramref name="rootDirectory"/>,
	/// running the identical checks as <see cref="VerifyAsync"/> over files on disk rather than archive
	/// entries, so an installed package can be re-verified without keeping the archive it came from.
	/// <paramref name="format"/> is explicit because there is no file extension to resolve it from.</summary>
	public static async Task<PackageVerifyResult> VerifyExtractedAsync(string rootDirectory,
		SignablePackageFormat format,
		IPluginManifestReader manifestReader,
		ReadOnlyMemory<byte>? rootPublicKey = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
		ArgumentNullException.ThrowIfNull(manifestReader);

		DirectoryPackageEntrySource source;
		try
		{
			source = DirectoryPackageEntrySource.Open(rootDirectory);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return PackageVerifyResult.Fail(SigningError.PackageUnreadable, ex.Message);
		}

		await using (source)
		{
			return await VerifyCoreAsync(source, format, manifestReader, rootPublicKey, cancellationToken);
		}
	}

	private static async Task<PackageVerifyResult> VerifyCoreAsync(IPackageEntrySource source,
		SignablePackageFormat format,
		IPluginManifestReader manifestReader,
		ReadOnlyMemory<byte>? rootPublicKey,
		CancellationToken cancellationToken)
	{
		var manifestEntryName = PackageManifestEntry.NameFor(format);

		try
		{
			var manifestEntry = source.Find(manifestEntryName);
			if (manifestEntry is null)
			{
				return PackageVerifyResult.Fail(SigningError.ManifestMissing,
					$"The package contains no root '{manifestEntryName}'.");
			}

			if (manifestEntry.Length is <= 0 or > PluginArtifactLimits.MaxManifestBytes)
			{
				return PackageVerifyResult.Fail(SigningError.ManifestTooLarge,
					$"The manifest exceeds the {PluginArtifactLimits.MaxManifestBytes}-byte limit.");
			}

			string manifestJson;
			await using (var manifestStream = await source.OpenAsync(manifestEntry, cancellationToken))
			using (var reader = new StreamReader(manifestStream, Encoding.UTF8))
			{
				manifestJson = await reader.ReadToEndAsync(cancellationToken);
			}

			JsonObject manifestNode;
			try
			{
				manifestNode = JsonNode.Parse(manifestJson) as JsonObject ??
					throw new JsonException("The manifest document must contain a JSON object.");
			}
			catch (JsonException ex)
			{
				return PackageVerifyResult.Fail(SigningError.ManifestMalformed, ex.Message);
			}

			if (!EmbeddedPackageSignature.IsPresent(manifestNode))
			{
				return PackageVerifyResult.Fail(SigningError.SignatureMissing,
					"The manifest carries no 'signature' object.");
			}

			if (!EmbeddedPackageSignature.TryParse(manifestNode, out var signature))
			{
				return PackageVerifyResult.Fail(SigningError.SignatureMalformed,
					"The manifest's 'signature' object has an unsupported shape.");
			}

			if (!string.Equals(signature.Algorithm, "ed25519", StringComparison.OrdinalIgnoreCase))
			{
				return PackageVerifyResult.Fail(SigningError.SignatureAlgorithmUnsupported,
					$"The signature uses '{signature.Algorithm}', which this library cannot verify.");
			}

			var signatureBytes = Base64Material.TryDecode(signature.Value, Ed25519KeyPair.SignatureLength);
			if (signatureBytes is null)
			{
				return PackageVerifyResult.Fail(SigningError.SignatureMalformed,
					"The signature value is not a valid base64-encoded Ed25519 signature.");
			}

			var certificateEntry = source.Find(PluginArtifactFiles.CertificateFileName);
			var certificateSignatureEntry = source.Find(PluginArtifactFiles.CertificateSignatureFileName);
			if (certificateEntry is null || certificateSignatureEntry is null)
			{
				return PackageVerifyResult.Fail(SigningError.CertificateUnreadable,
					$"The package carries no root '{PluginArtifactFiles.CertificateFileName}' / " +
					$"'{PluginArtifactFiles.CertificateSignatureFileName}'.");
			}

			var certificateBytes = await ReadEntryAsync(source, certificateEntry, cancellationToken);
			var certificateSignatureBytes = await ReadEntryAsync(source, certificateSignatureEntry, cancellationToken);

			var chainResult = SigningCertificateChain.Verify(certificateBytes,
				certificateSignatureBytes,
				rootPublicKey.HasValue ? rootPublicKey.Value.Span : MacroDeckRootKey.PublicKey,
				SigningCertificateChain.PackageKeyUsage);
			if (!chainResult.Success)
			{
				return PackageVerifyResult.Fail(chainResult.Error!.Value, chainResult.Message!);
			}

			var trusted = chainResult.TrustedCertificate!;

			if (!string.Equals(signature.KeyId, trusted.Certificate.CertificateId, StringComparison.Ordinal))
			{
				return PackageVerifyResult.Fail(SigningError.SignatureKeyIdMismatch,
					"The signature's keyId does not match the archive's certificate.");
			}

			if (SigningCertificateChain.EnsureValidAt(trusted.Certificate, signature.SignedAt) is { } validityFailure)
			{
				return PackageVerifyResult.Fail(validityFailure.Error, validityFailure.Message);
			}

			var filesFailure
				= await PackageFileValidator.ValidateAsync(source, manifestNode, manifestEntryName, cancellationToken);
			if (filesFailure is not null)
			{
				return PackageVerifyResult.Fail(filesFailure.Error, filesFailure.Message);
			}

			var digestResult = PackageDigest.Compute(format, manifestNode, manifestJson, manifestReader);
			if (!digestResult.Success)
			{
				return PackageVerifyResult.Fail(digestResult.Error!.Value, digestResult.Message!);
			}

			if (!trusted.VerifySignature(digestResult.Digest!, signatureBytes))
			{
				return PackageVerifyResult.Fail(SigningError.SignatureInvalid,
					"The package digest signature is not valid.");
			}

			return PackageVerifyResult.Ok(format, trusted.Certificate.CertificateId);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return PackageVerifyResult.Fail(SigningError.PackageUnreadable, ex.Message);
		}
	}

	private static async Task<byte[]> ReadEntryAsync(IPackageEntrySource source,
		PackageEntry entry,
		CancellationToken cancellationToken)
	{
		await using var stream = await source.OpenAsync(entry, cancellationToken);
		using var buffer = new MemoryStream();
		await stream.CopyToAsync(buffer, cancellationToken);
		return buffer.ToArray();
	}
}

/// <summary>The outcome of <see cref="PackageVerifier.VerifyAsync"/>.</summary>
public sealed record PackageVerifyResult
{
	public required bool Success { get; init; }

	public SignablePackageFormat? Format { get; init; }

	public string? CertificateId { get; init; }

	public SigningError? Error { get; init; }

	public string? Message { get; init; }

	public static PackageVerifyResult Ok(SignablePackageFormat format, string certificateId) =>
		new() { Success = true, Format = format, CertificateId = certificateId };

	public static PackageVerifyResult Fail(SigningError error, string message) =>
		new() { Success = false, Error = error, Message = message };
}
