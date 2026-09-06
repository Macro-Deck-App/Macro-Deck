using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Signing.Keys;

namespace MacroDeck.Signing.Packages;

/// <summary>
/// Signs any of the four <see cref="SignablePackageFormat"/> archives. Every format carries its signature
/// the same way: validated, embedded in its manifest as a <c>signature</c> object, with
/// <c>certificate.json</c> and <c>certificate.sig</c> written verbatim into the archive root so a signed
/// artifact always verifies on its own - Macro Deck packages never carry a detached signature file. Never
/// overwrites an existing output.
/// </summary>
public static class PackageSigner
{
	/// <summary>
	/// Signs <paramref name="packagePath"/> and writes the signed archive to <paramref name="outputPath"/>.
	/// <paramref name="certificateBytes"/> and <paramref name="certificateSignatureBytes"/> are written
	/// verbatim into the output archive - they must be the exact <c>cert_&lt;id&gt;.json</c> and
	/// <c>.sig</c> bytes the certificate was issued as, matching <paramref name="signer"/>'s certificate.
	/// <paramref name="manifestReader"/> is only consulted for a <see cref="SignablePackageFormat.Plugin"/>
	/// archive, which is the only format with a typed manifest model in the SDK.
	/// </summary>
	public static async Task<PackageSignResult> SignAsync(string packagePath,
		string outputPath,
		SigningMaterial signer,
		byte[] certificateBytes,
		byte[] certificateSignatureBytes,
		IPluginManifestReader manifestReader,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
		ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
		ArgumentNullException.ThrowIfNull(signer);
		ArgumentNullException.ThrowIfNull(certificateBytes);
		ArgumentNullException.ThrowIfNull(certificateSignatureBytes);
		ArgumentNullException.ThrowIfNull(manifestReader);

		if (SignablePackageFormats.Resolve(packagePath) is not { } format)
		{
			return PackageSignResult.Fail(SigningError.PackageFormatUnsupported,
				$"'{Path.GetExtension(packagePath)}' is not a signable package format.");
		}

		if (File.Exists(outputPath))
		{
			return PackageSignResult.Fail(SigningError.OutputExists,
				$"Refusing to overwrite existing file '{outputPath}'.");
		}

		var manifestEntryName = PackageManifestEntry.NameFor(format);

		ZipArchive source;
		try
		{
			source = await ZipFile.OpenReadAsync(packagePath, cancellationToken);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
		{
			return PackageSignResult.Fail(SigningError.PackageUnreadable, ex.Message);
		}

		await using (source)
		{
			var manifestEntry = source.GetEntry(manifestEntryName);
			if (manifestEntry is null)
			{
				return PackageSignResult.Fail(SigningError.ManifestMissing,
					$"The archive contains no root '{manifestEntryName}'.");
			}

			if (manifestEntry.Length is <= 0 or > PluginArtifactLimits.MaxManifestBytes)
			{
				return PackageSignResult.Fail(SigningError.ManifestTooLarge,
					$"The manifest exceeds the {PluginArtifactLimits.MaxManifestBytes}-byte limit.");
			}

			string manifestJson;
			await using (var manifestStream = await manifestEntry.OpenAsync(cancellationToken))
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
				return PackageSignResult.Fail(SigningError.ManifestMalformed, ex.Message);
			}

			if (EmbeddedPackageSignature.IsPresent(manifestNode))
			{
				return PackageSignResult.Fail(SigningError.AlreadySigned,
					"The package is already signed; refusing to replace its signature.");
			}

			var filesFailure = await PackageFileValidator.ValidateAsync(ZipPackageEntrySource.Wrap(source),
				manifestNode,
				manifestEntryName,
				cancellationToken);
			if (filesFailure is not null)
			{
				return PackageSignResult.Fail(filesFailure.Error, filesFailure.Message);
			}

			var digestResult = PackageDigest.Compute(format, manifestNode, manifestJson, manifestReader);
			if (!digestResult.Success)
			{
				return PackageSignResult.Fail(digestResult.Error!.Value, digestResult.Message!);
			}

			var digest = digestResult.Digest!;
			var signedAt = DateTimeOffset.UtcNow;
			var signatureBytes = signer.Sign(digest);
			if (!signer.SelfVerify(digest, signatureBytes))
			{
				return PackageSignResult.Fail(SigningError.SelfVerificationFailed,
					"The produced signature failed self-verification.");
			}

			manifestNode["signature"] = new JsonObject
			{
				["algorithm"] = "ed25519",
				["keyId"] = signer.Certificate.CertificateId,
				["value"] = Convert.ToBase64String(signatureBytes),
				["signedAt"] = signedAt.ToString("O")
			};

			var rewrittenManifestBytes = Encoding.UTF8.GetBytes(manifestNode.ToJsonString(SigningJson.Options) + "\n");
			if (rewrittenManifestBytes.Length > PluginArtifactLimits.MaxManifestBytes)
			{
				return PackageSignResult.Fail(SigningError.ManifestTooLarge,
					$"The signed manifest exceeds the {PluginArtifactLimits.MaxManifestBytes}-byte limit.");
			}

			var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
			if (!string.IsNullOrEmpty(outputDirectory))
			{
				Directory.CreateDirectory(outputDirectory);
			}

			var outputCreated = false;
			try
			{
				await using var outputStream
					= new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
				outputCreated = true;
				using (var target = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true))
				{
					foreach (var entry in source.Entries)
					{
						if (IsCertificateEntry(entry.FullName))
						{
							// Rewritten fresh below from the exact supplied bytes; never carried over from
							// the unsigned source, which would otherwise leave two same-named entries.
							continue;
						}

						var copied = target.CreateEntry(entry.FullName, CompressionLevel.Optimal);
						copied.LastWriteTime = entry.LastWriteTime;
						copied.ExternalAttributes = entry.ExternalAttributes;
						await using var writer = copied.Open();

						if (string.Equals(entry.FullName, manifestEntryName, StringComparison.Ordinal))
						{
							await writer.WriteAsync(rewrittenManifestBytes, cancellationToken);
							continue;
						}

						await using var entryStream = await entry.OpenAsync(cancellationToken);
						await entryStream.CopyToAsync(writer, cancellationToken);
					}

					var certificateEntry = target.CreateEntry(PluginArtifactFiles.CertificateFileName,
						CompressionLevel.Optimal);
					await using (var writer = certificateEntry.Open())
					{
						await writer.WriteAsync(certificateBytes, cancellationToken);
					}

					var certificateSignatureEntry = target.CreateEntry(PluginArtifactFiles.CertificateSignatureFileName,
						CompressionLevel.Optimal);
					await using (var writer = certificateSignatureEntry.Open())
					{
						await writer.WriteAsync(certificateSignatureBytes, cancellationToken);
					}
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				if (outputCreated)
				{
					TryDelete(outputPath);
				}

				return PackageSignResult.Fail(SigningError.WriteFailed, ex.Message);
			}
			catch
			{
				if (outputCreated)
				{
					TryDelete(outputPath);
				}

				throw;
			}

			return PackageSignResult.Ok(signer.Certificate.CertificateId, outputPath);
		}
	}

	private static bool IsCertificateEntry(string entryFullName) =>
		entryFullName is PluginArtifactFiles.CertificateFileName or PluginArtifactFiles.CertificateSignatureFileName;

	private static void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (IOException)
		{
			// Best effort: the write failure already being reported tells the caller the output is unusable.
		}
		catch (UnauthorizedAccessException)
		{
			// Same as above.
		}
	}
}

/// <summary>The outcome of <see cref="PackageSigner.SignAsync"/>.</summary>
public sealed record PackageSignResult
{
	public required bool Success { get; init; }

	public string? CertificateId { get; init; }

	public string? OutputPath { get; init; }

	public SigningError? Error { get; init; }

	public string? Message { get; init; }

	public static PackageSignResult Ok(string certificateId, string outputPath) =>
		new() { Success = true, CertificateId = certificateId, OutputPath = outputPath };

	public static PackageSignResult Fail(SigningError error, string message) =>
		new() { Success = false, Error = error, Message = message };
}
